using EnhancedStreamChat.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace EnhancedStreamChat.Chat
{
    /// <summary>
    /// AI-powered TTS (Text-to-Speech) manager for danmaku reading.
    /// Sends chat messages to the GPT-SoVITS proxy backend and plays the synthesized audio.
    ///
    /// 后端接口（AI TTS Proxy v3）：
    ///   POST /api/tts      body: { speaker, text, voice, version, speed }  -> audio/wav
    ///   GET  /api/voices   -> { voices, versions, default_voice, default_version, ... }
    ///   GET  /api/servers  -> { servers, current }
    /// 通过 ?_srv=&lt;下标&gt; 指定由哪台后端合成（单句取勾选的第一台）。
    /// </summary>
    public class ChatSpeechManager : MonoBehaviour
    {
        public static ChatSpeechManager Instance { get; private set; }

        // ── Queue Management ──────────────────────────────────────
        private readonly ConcurrentQueue<IESCChatMessage> _speechQueue = new ConcurrentQueue<IESCChatMessage>();
        private bool _isProcessing;
        private Coroutine _processCoroutine;
        private bool _testSpeechActive;

        /// <summary>弹幕事件可能来自后台线程；协程只能在主线程启动，这里做一次转交。</summary>
        private volatile bool _pendingStart;

        // ── Audio Playback ────────────────────────────────────────
        private AudioSource _audioSource;
        private GameObject _audioGameObject;
        private AudioGainFilter _gainFilter;

        // ── HTTP ──────────────────────────────────────────────────
        private int _serverCount;
        private string _activeBaseUrl;

        private string ActiveBaseUrl =>
            string.IsNullOrEmpty(_activeBaseUrl) ? ChatSpeechConfig.Instance.ServerBaseUrl : _activeBaseUrl;

        private static string BuildUrl(string baseUrl, string path, bool appendServer, int serverIndex = -1, int serverCount = 0)
        {
            var url = (baseUrl ?? string.Empty).TrimEnd('/') + path;
            if (appendServer && serverCount > 0)
            {
                var idx = serverIndex >= 0 ? serverIndex : 0;
                if (idx < 0 || idx >= serverCount) idx = 0;
                url += "?_srv=" + idx.ToString();
            }
            return url;
        }

        // ── Regex for stripping rich text tags ────────────────────
        private static readonly Regex RichTextRegex = new Regex(
            @"<[^>]+>",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );

        // ── JSON DTOs ─────────────────────────────────────────────
        private class TTSRequestBody
        {
            [JsonProperty("speaker")] public string speaker;  // sender name (empty for system/test)
            [JsonProperty("text")] public string text;        // message content
            [JsonProperty("voice")] public string voice;
            [JsonProperty("version")] public string version;  // empty = server default
            [JsonProperty("speed")] public float speed;
        }

        private class VoiceEntry
        {
            [JsonProperty("name")] public string name;
            [JsonProperty("description")] public string description;
        }

        private class VersionEntry
        {
            [JsonProperty("name")] public string name;
            [JsonProperty("label")] public string label;
            [JsonProperty("sample_steps")] public int? sample_steps;
        }

        private class VoiceListResponse
        {
            [JsonProperty("voices")] public VoiceEntry[] voices;
            [JsonProperty("versions")] public VersionEntry[] versions;
            [JsonProperty("default_voice")] public string default_voice;
            [JsonProperty("default_version")] public string default_version;
            [JsonProperty("server_name")] public string server_name;
        }

        private class ServerEntry
        {
            [JsonProperty("name")] public string name;
            [JsonProperty("url")] public string url;
            [JsonProperty("desc")] public string desc;
            [JsonProperty("kind")] public string kind;
            [JsonProperty("detail")] public string detail;
            [JsonProperty("engine")] public bool engine;
        }

        private class ServerListResponse
        {
            [JsonProperty("servers")] public ServerEntry[] servers;
            [JsonProperty("current")] public string current;
        }

        // ── Unity Lifecycle ───────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this.gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(this.gameObject);

            Logger.Info("[TTS] ChatSpeechManager Awake - initializing audio system");
            _audioGameObject = this.gameObject;

            _audioSource = _audioGameObject.GetComponent<AudioSource>() ?? _audioGameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.spatialBlend = 0f; // 2D audio
            _audioSource.volume = 1f;       // 实际音量由 AudioGainFilter 处理（支持 >100%）

            _gainFilter = _audioGameObject.GetComponent<AudioGainFilter>() ?? _audioGameObject.AddComponent<AudioGainFilter>();
            _gainFilter.Gain = ChatSpeechConfig.Instance.SpeechVolume;

            StartCoroutine(DelayedStartup());
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
            if (_audioSource != null)
            {
                _audioSource.Stop();
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (_audioSource != null && _gainFilter != null)
            {
                _gainFilter.Gain = ChatSpeechConfig.Instance.SpeechVolume;
            }

            // 后台线程只负责入队，协程在主线程启动
            if (_pendingStart && !_isProcessing && _processCoroutine == null)
            {
                _pendingStart = false;
                if (!_speechQueue.IsEmpty)
                {
                    _processCoroutine = StartCoroutine(ProcessQueueCoroutine());
                }
            }
        }

        // ── Public API ────────────────────────────────────────────

        /// <summary>Enqueue a chat message for TTS reading.</summary>
        public void EnqueueMessage(IESCChatMessage msg)
        {
            if (msg == null) return;
            if (!ChatSpeechConfig.Instance.SpeechEnabled) return;
            if (!ShouldSpeak(msg)) return;

            var maxQueue = Mathf.Max(1, ChatSpeechConfig.Instance.SpeechMaxQueueSize);
            while (_speechQueue.Count >= maxQueue)
            {
                if (_speechQueue.TryDequeue(out var dropped))
                {
                    Logger.Warn($"[TTS] Queue full ({maxQueue}); dropped: speaker=\"{StripRichText(dropped.Sender?.DisplayName)}\" text=\"{StripRichText(dropped.Message)}\"");
                }
            }

            _speechQueue.Enqueue(msg);
            _pendingStart = true;
            Logger.Info($"[TTS] Enqueued: speaker=\"{StripRichText(msg.Sender?.DisplayName)}\" text=\"{StripRichText(msg.Message)}\" queue={_speechQueue.Count}");
        }

        public void Skip()
        {
            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.Stop();
            }
        }

        public void ClearQueue()
        {
            while (_speechQueue.TryDequeue(out _)) { }
        }

        /// <summary>重新拉取后端服务器 / 音色 / 模型版本列表。</summary>
        public void RefreshVoices()
        {
            Logger.Info("[TTS] RefreshVoices() called — fetching servers/voices...");
            StartCoroutine(RefreshAllCoroutine());
        }

        public void TestSpeech(string text = "欢迎来到直播间")
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (_testSpeechActive) return;

            var clean = StripRichText(text);
            if (string.IsNullOrWhiteSpace(clean)) return;

            _testSpeechActive = true;
            StartCoroutine(TestSpeechCoroutine(clean));
        }

        // ── Filter Logic ──────────────────────────────────────────

        private static bool ShouldSpeak(IESCChatMessage msg)
        {
            if (msg.IsSystemMessage) return false;

            switch (ChatSpeechConfig.Instance.SpeechFilterMode)
            {
                case 0:
                    return true;
                case 1:
                    return msg.IsHighlighted || msg.IsMentioned;
                case 2:
                    return IsGiftOrSuperChatMessage(msg);
                default:
                    return false;
            }
        }

        private static bool IsGiftOrSuperChatMessage(IESCChatMessage msg)
        {
            if (msg.Metadata != null)
            {
                if (msg.Metadata.TryGetValue("msg_type", out var msgType))
                {
                    if (msgType == "send_gift" ||
                        msgType == "guard_buy" ||
                        msgType == "super_chat" ||
                        msgType == "super_chat_jpn" ||
                        msgType == "gift_star")
                    {
                        return true;
                    }
                }
                if (msg.Metadata.TryGetValue("bits", out var bits))
                {
                    return !string.IsNullOrEmpty(bits) && bits != "0";
                }
                if (msg.Metadata.TryGetValue("platform", out var platform) && platform == "bilibili")
                {
                    if (msg.IsHighlighted) return true;
                }
            }

            var message = msg.Message;
            if (!string.IsNullOrEmpty(message))
            {
                if (message.Contains("投喂") || message.Contains("赠送") ||
                    message.Contains("SuperChat") || message.Contains("醒目留言") ||
                    message.Contains("gift"))
                {
                    return true;
                }
            }
            return false;
        }

        // ── Queue Processing ──────────────────────────────────────

        private IEnumerator ProcessQueueCoroutine()
        {
            _isProcessing = true;
            while (_speechQueue.TryDequeue(out var msg))
            {
                while (_testSpeechActive)
                {
                    yield return null;
                }
                if (!ChatSpeechConfig.Instance.SpeechEnabled) break;

                var speaker = StripRichText(msg.Sender?.DisplayName);
                var cleanText = StripRichText(msg.Message);
                if (string.IsNullOrWhiteSpace(cleanText))
                {
                    continue;
                }
                if (cleanText.Length > 200)
                {
                    cleanText = cleanText.Substring(0, 200);
                }

                yield return StartCoroutine(RequestAndPlayTTS(speaker, cleanText));

                var volume = ChatSpeechConfig.Instance.SpeechVolume;
                yield return new WaitForSeconds(volume > 1.5f ? 0.5f : 0.3f);
            }

            _isProcessing = false;
            _processCoroutine = null;
        }

        private IEnumerator TestSpeechCoroutine(string cleanText)
        {
            if (cleanText.Length > 200) cleanText = cleanText.Substring(0, 200);
            yield return StartCoroutine(RequestAndPlayTTS(null, cleanText));
            _testSpeechActive = false;
        }

        // ── Server / Voice List Fetching ──────────────────────────

        private IEnumerator DelayedStartup()
        {
            yield return new WaitForSeconds(8f);
            yield return StartCoroutine(RefreshAllCoroutine());
        }

        private IEnumerator RefreshAllCoroutine()
        {
            yield return StartCoroutine(FetchServersCoroutine());
            yield return StartCoroutine(FetchVoicesCoroutine());
            ChatSpeechConfig.NotifyListsUpdated();
        }

        private IEnumerator FetchServersCoroutine()
        {
            foreach (var baseUrl in ChatSpeechConfig.Instance.GetBaseUrlCandidates())
            {
                var url = BuildUrl(baseUrl, "/api/servers", false);
                string raw = null;
                using (var wr = UnityWebRequest.Get(url))
                {
                    wr.timeout = 10;
                    wr.SetRequestHeader("Accept", "application/json");
                    wr.certificateHandler = new BypassCertificate();
                    yield return wr.SendWebRequest();

                    if (wr.result == UnityWebRequest.Result.Success)
                    {
                        raw = wr.downloadHandler.text;
                    }
                    else
                    {
                        Logger.Warn($"[TTS] Could not fetch server list ({wr.responseCode}) from {url}: {wr.error}");
                    }
                }

                if (string.IsNullOrWhiteSpace(raw))
                {
                    Logger.Warn($"[TTS] Empty server list response from {url}, trying next base URL if any");
                    continue;
                }

                var parsed = false;
                try
                {
                    Logger.Info($"[TTS] Servers response from {baseUrl}: bytes={raw.Length}, body=\"{Truncate(raw, 300)}\"");
                    var data = JsonConvert.DeserializeObject<ServerListResponse>(raw);
                    if (data?.servers == null)
                    {
                        Logger.Warn($"[TTS] Unexpected server list JSON from {url}");
                        continue;
                    }

                    _activeBaseUrl = baseUrl;
                    var servers = data.servers;
                    _serverCount = servers.Length;

                    var names = new List<string>();
                    foreach (var s in servers)
                    {
                        var display = string.IsNullOrEmpty(s.name) ? "未命名后端" : s.name;
                        if (s.kind == "offline")
                        {
                            display += " ✕不可达";
                        }
                        else if (s.engine)
                        {
                            display += " ⚡";
                        }
                        names.Add(display);
                    }
                    ChatSpeechConfig.Instance.SpeechServerNames = string.Join("|", names.ToArray());

                    if (ChatSpeechConfig.Instance.SpeechServerIndex >= _serverCount
                        || ChatSpeechConfig.Instance.SpeechServerIndex < 0)
                    {
                        ChatSpeechConfig.Instance.SpeechServerIndex = 0;
                    }
                    ChatSpeechConfig.Instance.Save();
                    Logger.Info($"[TTS] Loaded {_serverCount} backend server(s) via {baseUrl}: {ChatSpeechConfig.Instance.SpeechServerNames}");
                    parsed = true;
                }
                catch (Exception e)
                {
                    Logger.Warn($"[TTS] Failed to parse server list from {url}: {e.Message}");
                }

                if (parsed)
                {
                    yield break;
                }
            }

            _serverCount = 0;
            Logger.Warn("[TTS] No reachable TTS server; check SpeechServerUrl / SpeechServerUrlAlt");
        }

        private IEnumerator FetchVoicesCoroutine()
        {
            var candidates = new List<string> { ActiveBaseUrl };
            foreach (var b in ChatSpeechConfig.Instance.GetBaseUrlCandidates())
            {
                if (!candidates.Contains(b)) candidates.Add(b);
            }

            foreach (var baseUrl in candidates)
            {
                var url = BuildUrl(baseUrl, "/api/voices", true, ChatSpeechConfig.Instance.SpeechServerIndex, _serverCount);
                string raw = null;
                using (var wr = UnityWebRequest.Get(url))
                {
                    wr.timeout = 15;
                    wr.SetRequestHeader("Accept", "application/json");
                    wr.certificateHandler = new BypassCertificate();
                    yield return wr.SendWebRequest();

                    if (wr.result == UnityWebRequest.Result.Success)
                    {
                        raw = wr.downloadHandler.text;
                    }
                    else
                    {
                        Logger.Warn($"[TTS] Could not fetch voice list ({wr.responseCode}) from {url}: {wr.error}");
                    }
                }

                if (string.IsNullOrWhiteSpace(raw))
                {
                    Logger.Warn($"[TTS] Empty voice list response from {url}, trying next base URL if any");
                    continue;
                }

                var parsed = false;
                try
                {
                    Logger.Info($"[TTS] Voices response from {baseUrl}: bytes={raw.Length}, body=\"{Truncate(raw, 300)}\"");
                    var data = JsonConvert.DeserializeObject<VoiceListResponse>(raw);
                    if (data?.voices == null)
                    {
                        Logger.Warn($"[TTS] Unexpected voice list JSON from {url}");
                        continue;
                    }

                    _activeBaseUrl = baseUrl;
                    var voiceNames = new List<string> { "default" };
                    foreach (var v in data.voices)
                    {
                        if (!string.IsNullOrEmpty(v.name) && !voiceNames.Contains(v.name))
                        {
                            voiceNames.Add(v.name);
                        }
                    }
                    var versionNames = new List<string>();
                    if (data.versions != null)
                    {
                        foreach (var v in data.versions)
                        {
                            if (!string.IsNullOrEmpty(v.name) && !versionNames.Contains(v.name))
                            {
                                versionNames.Add(v.name);
                            }
                        }
                    }

                    var cfg = ChatSpeechConfig.Instance;
                    cfg.SpeechVoices = string.Join(",", voiceNames.ToArray());
                    cfg.SpeechVersions = string.Join(",", versionNames.ToArray());

                    // 首次运行时采用服务器默认值
                    if (string.IsNullOrEmpty(cfg.SpeechVoice) || !voiceNames.Contains(cfg.SpeechVoice))
                    {
                        cfg.SpeechVoice = !string.IsNullOrEmpty(data.default_voice) && voiceNames.Contains(data.default_voice)
                            ? data.default_voice
                            : (voiceNames.Count > 1 ? voiceNames[1] : "default");
                    }
                    if (!string.IsNullOrEmpty(cfg.SpeechVersion) && !versionNames.Contains(cfg.SpeechVersion))
                    {
                        cfg.SpeechVersion = string.Empty;
                    }
                    cfg.Save();

                    Logger.Info($"[TTS] Loaded {voiceNames.Count} voice(s) via {baseUrl}: {cfg.SpeechVoices}");
                    Logger.Info($"[TTS] Loaded {versionNames.Count} version(s): {cfg.SpeechVersions} (selected: \"{cfg.SpeechVersion}\")");
                    parsed = true;
                }
                catch (Exception e)
                {
                    Logger.Warn($"[TTS] Failed to parse voice list from {url}: {e.Message}");
                }

                if (parsed)
                {
                    yield break;
                }
            }
        }

        // ── HTTP Communication ────────────────────────────────────

        private IEnumerator RequestAndPlayTTS(string speaker, string text)
        {
            var cfg = ChatSpeechConfig.Instance;

            var ttsRequest = new TTSRequestBody
            {
                speaker = speaker ?? string.Empty,
                text = text,
                voice = string.IsNullOrEmpty(cfg.SpeechVoice) ? "default" : cfg.SpeechVoice,
                version = cfg.SpeechVersion ?? string.Empty,
                speed = Mathf.Clamp(cfg.SpeechSpeed, 0.5f, 2.0f)
            };
            var jsonPayload = JsonConvert.SerializeObject(ttsRequest);

            var bases = new List<string> { ActiveBaseUrl };
            foreach (var b in cfg.GetBaseUrlCandidates())
            {
                if (!bases.Contains(b)) bases.Add(b);
            }

            // 只使用用户选择的后端；不自动切换到其他引擎
            var indices = new List<int> { _serverCount > 0 ? cfg.SpeechServerIndex : -1 };

            const int maxAttempts = 2;
            foreach (var baseUrl in bases)
            {
                foreach (var serverIndex in indices)
                {
                    var useServer = serverIndex >= 0 && _serverCount > 0;
                    var url = BuildUrl(baseUrl, "/api/tts", useServer, serverIndex, _serverCount);
                    Logger.Info($"[TTS] Requesting: POST {url}");
                    Logger.Info($"[TTS] Payload: {jsonPayload}");

                    var nextCandidate = false;
                    var fatal = false;
                    var played = false;

                    for (var attempt = 1; attempt <= maxAttempts; attempt++)
                    {
                        var retry = false;
                        using (var wr = new UnityWebRequest(url, "POST"))
                        {
                            wr.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonPayload));
                            wr.downloadHandler = new DownloadHandlerBuffer();
                            wr.SetRequestHeader("Content-Type", "application/json");
                            wr.SetRequestHeader("Accept", "audio/wav, application/json");
                            wr.timeout = 300; // 冷启动推理可能需要 100s 以上
                            wr.certificateHandler = new BypassCertificate();

                            yield return wr.SendWebRequest();

                            Logger.Info($"[TTS] HTTP response: code={wr.responseCode}, result={wr.result}, version={wr.GetResponseHeader("x-tts-version")} (attempt {attempt}/{maxAttempts})");

                            if (wr.result == UnityWebRequest.Result.Success)
                            {
                                var audioData = wr.downloadHandler.data;
                                var contentType = wr.GetResponseHeader("Content-Type") ?? "unknown";
                                if (audioData == null || audioData.Length == 0)
                                {
                                    Logger.Warn($"[TTS] Server returned empty audio data via {url}");
                                    nextCandidate = true;
                                }
                                else if (contentType.Contains("json") || (audioData.Length < 256 && audioData[0] == (byte)'{'))
                                {
                                    Logger.Warn($"[TTS] Server returned JSON instead of audio: {Encoding.UTF8.GetString(audioData)}");
                                    nextCandidate = true;
                                }
                                else if (!IsCompleteWav(audioData))
                                {
                                    Logger.Warn($"[TTS] Truncated/invalid WAV from {url} ({audioData.Length} bytes)");
                                    nextCandidate = true;
                                }
                                else
                                {
                                    _activeBaseUrl = baseUrl;
                                    var duration = GetWavDuration(audioData);
                                    Logger.Info($"[TTS] Received {audioData.Length} bytes ({contentType}, {duration:F2}s) via {url}, playing...");
                                    yield return StartCoroutine(PlayAudioFromBytes(audioData));
                                    played = true;
                                }
                            }
                            else if (wr.responseCode == 400)
                            {
                                Logger.Warn($"[TTS] Server rejected text (400): {wr.downloadHandler?.text}");
                                fatal = true;
                            }
                            else if (wr.responseCode == 503)
                            {
                                // 引擎冷启动/正在换模型时会出现，稍后重试一次
                                Logger.Warn($"[TTS] Server not ready (503): {wr.downloadHandler?.text}");
                                retry = true;
                            }
                            else if (wr.responseCode == 502)
                            {
                                // 引擎合成失败（如 P106 短文本返回静音）；按配置不切换引擎，跳过这条
                                Logger.Warn($"[TTS] Backend 502 via {url}: {wr.downloadHandler?.text}");
                                fatal = true;
                            }
                            else if (wr.responseCode == 0 || wr.result == UnityWebRequest.Result.ConnectionError)
                            {
                                Logger.Warn($"[TTS] Connection failed via {url}: {wr.error}");
                                retry = true;
                            }
                            else
                            {
                                Logger.Warn($"[TTS] HTTP request FAILED via {url} (code={wr.responseCode}): {wr.error}");
                                nextCandidate = true;
                            }
                        }

                        if (played || fatal || nextCandidate)
                        {
                            break;
                        }
                        if (retry && attempt < maxAttempts)
                        {
                            Logger.Info("[TTS] Retrying in 3s...");
                            yield return new WaitForSeconds(3f);
                            continue;
                        }
                        if (retry)
                        {
                            nextCandidate = true;
                        }
                        break;
                    }

                    if (played || fatal)
                    {
                        yield break;
                    }
                    Logger.Warn($"[TTS] Request via {url} failed, trying fallback base URL if available");
                }
            }

            Logger.Warn("[TTS] All TTS base URLs / engines failed");
        }

        // ── Audio Playback ────────────────────────────────────────

        private IEnumerator PlayAudioFromBytes(byte[] audioData)
        {
            // 每次请求用独立临时文件，避免测试朗读与队列朗读互相覆盖
            var tempAudioPath = Path.Combine(
                Application.temporaryCachePath,
                $"tts_speech_{Guid.NewGuid():N}.wav");

            try
            {
                File.WriteAllBytes(tempAudioPath, audioData);
            }
            catch (Exception e)
            {
                Logger.Error($"[TTS] Failed to write temp audio file: {e.Message}");
                yield break;
            }

            var filePath = tempAudioPath.Replace('\\', '/');
            using (var wr = UnityWebRequestMultimedia.GetAudioClip("file:///" + filePath, AudioType.WAV))
            {
                yield return wr.SendWebRequest();

                if (wr.result == UnityWebRequest.Result.Success)
                {
                    var clip = DownloadHandlerAudioClip.GetContent(wr);
                    if (clip != null)
                    {
                        _gainFilter.Gain = ChatSpeechConfig.Instance.SpeechVolume;
                        _audioSource.clip = clip;
                        _audioSource.Play();

                        Logger.Info($"[TTS] Playing audio ({clip.length:F1}s) at gain {_gainFilter.Gain:F2}");

                        while (_audioSource.isPlaying)
                        {
                            yield return null;
                        }

                        _audioSource.clip = null;
                        Destroy(clip);
                    }
                }
                else
                {
                    Logger.Warn($"[TTS] Failed to load audio clip: {wr.error}");
                    _audioSource.clip = null;
                }
            }

            try
            {
                if (File.Exists(tempAudioPath))
                {
                    File.Delete(tempAudioPath);
                }
            }
            catch { /* ignore cleanup errors */ }
        }

        // ── Text Processing ───────────────────────────────────────

        private static bool IsCompleteWav(byte[] d)
        {
            if (d == null || d.Length < 44) return false;
            if (d[0] != 'R' || d[1] != 'I' || d[2] != 'F' || d[3] != 'F') return false;
            if (d[8] != 'W' || d[9] != 'A' || d[10] != 'V' || d[11] != 'E') return false;

            var off = 12;
            while (off + 8 <= d.Length)
            {
                var id = Encoding.ASCII.GetString(d, off, 4);
                var len = BitConverter.ToInt32(d, off + 4);
                if (len < 0)
                {
                    if (id == "data" && len == unchecked((int)0xFFFFFFFF)) return true;
                    return false;
                }
                if (id == "data")
                {
                    return (long)off + 8 + len <= d.Length;
                }
                var next = (long)off + 8 + len + (len & 1);
                if (next <= off || next > d.Length) return false;
                off = (int)next;
            }
            return false;
        }

        private static float GetWavDuration(byte[] d)
        {
            if (d == null || d.Length < 44) return 0f;
            var off = 12;
            var sampleRate = 0;
            var channels = 0;
            var bits = 0;
            long dataLen = 0;

            while (off + 8 <= d.Length)
            {
                var id = Encoding.ASCII.GetString(d, off, 4);
                var len = BitConverter.ToInt32(d, off + 4);
                if (id == "fmt " && len >= 16 && off + 24 <= d.Length)
                {
                    channels = BitConverter.ToInt16(d, off + 10);
                    sampleRate = BitConverter.ToInt32(d, off + 12);
                    bits = BitConverter.ToInt16(d, off + 22);
                }
                else if (id == "data")
                {
                    dataLen = Math.Min((long)len, d.Length - (off + 8));
                    break;
                }

                var next = (long)off + 8 + len + (len & 1);
                if (next <= off || next > d.Length) break;
                off = (int)next;
            }

            var bytesPerSec = sampleRate * channels * (bits / 8);
            return bytesPerSec > 0 ? (float)dataLen / bytesPerSec : 0f;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "...";
        }

        internal static string StripRichText(string message)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;
            var result = RichTextRegex.Replace(message, string.Empty);
            result = Regex.Replace(result, @"\s+", " ");
            return result.Trim();
        }

        /// <summary>Bypass SSL certificate validation for self-signed / LAN servers.</summary>
        private class BypassCertificate : CertificateHandler
        {
            protected override bool ValidateCertificate(byte[] certificateData) => true;
        }
    }

    /// <summary>
    /// AudioGainFilter: Unity 的 AudioSource.volume 上限是 1.0，
    /// 这里在音频线程对采样做增益，实现 0~200% 音量（超出部分硬限幅）。
    /// </summary>
    public class AudioGainFilter : MonoBehaviour
    {
        public volatile float Gain = 1f;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            var gain = this.Gain;
            if (gain <= 0.0001f)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }
            if (Mathf.Abs(gain - 1f) < 0.0001f)
            {
                return;
            }
            for (var i = 0; i < data.Length; i++)
            {
                var v = data[i] * gain;
                if (v > 1f) v = 1f;
                else if (v < -1f) v = -1f;
                data[i] = v;
            }
        }
    }
}
