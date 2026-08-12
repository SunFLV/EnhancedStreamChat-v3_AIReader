using ChatCore.Interfaces;
using EnhancedStreamChat.Utilities;
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
    /// Sends chat messages to a remote GPT-SoVITS TTS server and plays the synthesized audio.
    /// </summary>
    public class ChatSpeechManager : PersistentSingleton<ChatSpeechManager>
    {
        // ── Queue Management ──────────────────────────────────────
        private readonly ConcurrentQueue<IChatMessage> _speechQueue = new ConcurrentQueue<IChatMessage>();
        private bool _isProcessing;
        private Coroutine _processCoroutine;
        private bool _testSpeechActive;

        // ── Audio Playback ────────────────────────────────────────
        private AudioSource _audioSource;
        private GameObject _audioGameObject;

        // ── HTTP ──────────────────────────────────────────────────
        private string _tempAudioPath;

        // ── Regex for stripping rich text tags ────────────────────
        private static readonly Regex RichTextRegex = new Regex(
            @"<[^>]+>",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );

        // ── JSON helpers for voice list deserialization ─────────────
        [Serializable]
        private class VoiceListResponse
        {
            public VoiceEntry[] voices;
        }

        [Serializable]
        private class TTSRequestBody
        {
            public string text;
            public string voice;
            public float speed;
        }

        [Serializable]
        private class VoiceEntry
        {
            public string name;
            public string description;
        }

        // ── Unity Lifecycle ───────────────────────────────────────

        private void Awake()
        {
            Logger.Info("[TTS] ChatSpeechManager Awake - initializing audio system");
            // Create a dedicated persistent GameObject for audio playback
            _audioGameObject = new GameObject("ChatSpeechAudio");
            DontDestroyOnLoad(_audioGameObject);
            _audioGameObject.transform.SetParent(this.transform);

            _audioSource = _audioGameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.spatialBlend = 0f; // 2D audio (not spatialized)

            _tempAudioPath = Path.Combine(Application.temporaryCachePath, "tts_speech.wav");

            // Fetch available voices later (after scene is fully loaded, to avoid SSL timing issues)
            StartCoroutine(DelayedStartup());
        }

        protected override void OnDestroy()
        {
            StopAllCoroutines();
            if (_audioSource != null)
            {
                _audioSource.Stop();
            }
            if (_audioGameObject != null)
            {
                Destroy(_audioGameObject);
            }

            // Clean temp file
            try
            {
                if (File.Exists(_tempAudioPath))
                {
                    File.Delete(_tempAudioPath);
                }
            }
            catch { /* ignore cleanup errors */ }

            base.OnDestroy();
        }

        // ── Public API ────────────────────────────────────────────

        /// <summary>
        /// Enqueue a chat message for TTS reading.
        /// Called from ChatDisplay.OnTextMessageReceived.
        /// </summary>
        public void EnqueueMessage(IChatMessage msg)
        {
            if (msg == null) return;
            if (!ChatSpeechConfig.instance.SpeechEnabled) return;

            // Apply filter
            if (!ShouldSpeak(msg)) return;

            // Enforce max queue size — drop oldest messages when full
            while (_speechQueue.Count >= ChatSpeechConfig.instance.SpeechMaxQueueSize)
            {
                _speechQueue.TryDequeue(out _);
            }

            _speechQueue.Enqueue(msg);
            Logger.Debug($"TTS: Enqueued message. Queue size: {_speechQueue.Count}");

            // Start processing if idle
            if (!_isProcessing && _processCoroutine == null)
            {
                _processCoroutine = StartCoroutine(ProcessQueueCoroutine());
            }
        }

        /// <summary>
        /// Skip the currently playing speech and process the next queued message.
        /// </summary>
        public void Skip()
        {
            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.Stop();
                Logger.Debug("TTS: Skipped current speech");
            }
        }

        /// <summary>
        /// Clear all pending speech messages from the queue.
        /// </summary>
        public void ClearQueue()
        {
            while (_speechQueue.TryDequeue(out _)) { }
            Logger.Debug("TTS: Queue cleared");
        }

        /// <summary>
        /// Send a test message to verify TTS is working.
        /// Returns true if the message was enqueued.
        /// </summary>
        /// <summary>
        /// Re-fetch voice list from the server. Call when settings panel opens.
        /// </summary>
        public void RefreshVoices()
        {
            Logger.Info("[TTS] RefreshVoices() called — fetching voices...");
            StartCoroutine(FetchVoicesCoroutine());
        }

        public void TestSpeech(string text = "欢迎来到直播间")
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            Logger.Info($"[TTS] Test speech button clicked: \"{text}\"");
            Logger.Info($"[TTS] Config - Enabled: {ChatSpeechConfig.instance.SpeechEnabled}");
            Logger.Info($"[TTS] Config - ServerUrl: {ChatSpeechConfig.instance.SpeechServerUrl}");
            Logger.Info($"[TTS] Config - Voice: {ChatSpeechConfig.instance.SpeechVoice}");
            Logger.Info($"[TTS] GameObject active: {gameObject.activeInHierarchy}, enabled: {isActiveAndEnabled}");

            // Don't interrupt ongoing test speech
            if (_testSpeechActive)
            {
                Logger.Info("[TTS] Test speech already in progress, ignoring");
                return;
            }
            _testSpeechActive = true;
            StartCoroutine(TestSpeechCoroutine(text));
        }

        // ── Filter Logic ──────────────────────────────────────────

        private bool ShouldSpeak(IChatMessage msg)
        {
            // Never speak system messages
            if (msg.IsSystemMessage) return false;

            var mode = ChatSpeechConfig.instance.SpeechFilterMode;
            switch (mode)
            {
                case 0: // All messages
                    return true;

                case 1: // Highlighted / Mentioned only
                    return msg.IsHighlighted || msg.IsPing;

                case 2: // Gifts / SuperChats only
                    return IsGiftOrSuperChatMessage(msg);

                default:
                    return false;
            }
        }

        private bool IsGiftOrSuperChatMessage(IChatMessage msg)
        {
            // Check metadata for platform-specific gift/superchat keys
            if (msg.Metadata != null)
            {
                // Bilibili gift types
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

                // Twitch: highlighted messages (bits, channel points, etc.)
                // msg.IsHighlighted already handled in case 1; here we check for
                // specific Twitch gift metadata
                if (msg.Metadata.TryGetValue("bits", out var bits))
                {
                    return !string.IsNullOrEmpty(bits) && bits != "0";
                }
            }

            // Fallback: check message content for gift-related patterns
            // (e.g. "sent a gift", "投喂", etc.)
            var message = msg.Message;
            if (!string.IsNullOrEmpty(message))
            {
                if (message.Contains("投喂") ||
                    message.Contains("赠送") ||
                    message.Contains("SuperChat") ||
                    message.Contains("醒目留言") ||
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
            Logger.Debug("TTS: Queue processing started");

            while (_speechQueue.TryDequeue(out var msg))
            {
                // Wait if a test speech is in progress
                while (_testSpeechActive)
                {
                    yield return null;
                }

                // Stop if TTS was disabled mid-queue
                if (!ChatSpeechConfig.instance.SpeechEnabled)
                {
                    Logger.Debug("TTS: Disabled — stopping queue processing");
                    break;
                }

                // Extract clean text — include sender name
                var rawText = $"{msg.Sender.DisplayName}说{msg.Message}";
                var cleanText = StripRichText(rawText);

                // Skip empty or whitespace-only messages
                if (string.IsNullOrWhiteSpace(cleanText))
                {
                    Logger.Debug($"TTS: Skipping empty/whitespace message");
                    continue;
                }

                // Truncate long messages to avoid overwhelming TTS
                const int maxLength = 200;
                if (cleanText.Length > maxLength)
                {
                    var originalLength = cleanText.Length;
                    cleanText = cleanText.Substring(0, maxLength);
                    Logger.Debug($"TTS: Truncated message from {originalLength} to {maxLength} chars");
                }

                Logger.Debug($"TTS: Processing: \"{cleanText}\"");

                // Request TTS from server and play audio
                yield return StartCoroutine(RequestAndPlayTTS(cleanText));

                // Small gap between consecutive speeches
                yield return new WaitForSeconds(0.3f);
            }

            _isProcessing = false;
            _processCoroutine = null;
            Logger.Debug("TTS: Queue processing finished");
        }

        private IEnumerator TestSpeechCoroutine(string text)
        {
            Logger.Info($"[TTS] Coroutine started, text: \"{text}\"");
            var cleanText = StripRichText(text);
            Logger.Info($"[TTS] Cleaned text: \"{cleanText}\"");
            if (cleanText.Length > 200)
            {
                cleanText = cleanText.Substring(0, 200);
            }
            yield return StartCoroutine(RequestAndPlayTTS(cleanText));
            _testSpeechActive = false;
            Logger.Debug("TTS: Test speech finished");
        }

        // ── Voice List Fetching ──────────────────────────────────────

        /// <summary>
        /// Fetches the list of available TTS voices from the server
        /// and updates the settings dropdown.
        /// </summary>
        private IEnumerator DelayedStartup()
        {
            yield return new WaitForSeconds(8f); // Wait for scene to fully load
            yield return StartCoroutine(FetchVoicesCoroutine());

            // Auto-test disabled — use the "测试朗读" button instead
        }

        private IEnumerator FetchVoicesCoroutine()
        {
            // Build the voices endpoint URL from the TTS server URL
            var ttsUrl = NormalizeServerUrl(ChatSpeechConfig.instance.SpeechServerUrl);
            Uri baseUri;
            try
            {
                baseUri = new Uri(ttsUrl);
            }
            catch (UriFormatException)
            {
                Logger.Warn($"TTS: Invalid server URL, cannot fetch voices: {ttsUrl}");
                yield break;
            }

            var voicesUrl = new Uri(baseUri, "/api/voices").ToString();

            using (var wr = UnityWebRequest.Get(voicesUrl))
            {
                wr.timeout = 5;
                wr.certificateHandler = new BypassCertificate();
                yield return wr.SendWebRequest();

                if (wr.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var rawJson = wr.downloadHandler.text;
                        Logger.Info($"[TTS] Voices response length: {rawJson.Length}");

                        // Manual parsing — avoid JsonUtility issues with non-ASCII descriptions
                        var voiceNames = new List<object> { "default" };
                        var namePrefix = "\"name\":\"";
                        var idx = 0;
                        while ((idx = rawJson.IndexOf(namePrefix, idx, StringComparison.Ordinal)) >= 0)
                        {
                            idx += namePrefix.Length;
                            var end = rawJson.IndexOf("\"", idx, StringComparison.Ordinal);
                            if (end > idx)
                            {
                                var name = rawJson.Substring(idx, end - idx);
                                if (!string.IsNullOrEmpty(name) && !voiceNames.Contains(name))
                                    voiceNames.Add(name);
                            }
                        }

                        Logger.Info($"[TTS] Loaded {voiceNames.Count} voice(s): {string.Join(", ", voiceNames)}");
                        // Save to config file so it persists across restarts
                        ChatSpeechConfig.instance.SpeechVoices = string.Join(",", voiceNames);
                        ChatSpeechConfig.instance.Save();
                    }
                    catch (Exception e)
                    {
                        Logger.Warn($"[TTS] Failed to parse voice list: {e.Message}");
                    }
                }
                else
                {
                    Logger.Warn(
                        $"TTS: Could not fetch voice list ({wr.responseCode}). " +
                        "Using default voice only."
                    );
                }
            }
        }

        // ── HTTP Communication ────────────────────────────────────

        private IEnumerator RequestAndPlayTTS(string text)
        {
            var serverUrl = NormalizeServerUrl(ChatSpeechConfig.instance.SpeechServerUrl);
            var voice = ChatSpeechConfig.instance.SpeechVoice;
            var speed = ChatSpeechConfig.instance.SpeechSpeed;

            // Build JSON payload — use JsonUtility for reliability
            var ttsRequest = new TTSRequestBody
            {
                text = text,
                voice = voice,
                speed = speed
            };
            var jsonPayload = JsonUtility.ToJson(ttsRequest);

            Logger.Info($"[TTS] Requesting: POST {serverUrl}");
            Logger.Info($"[TTS] Payload: {jsonPayload}");
            var hexBytes = BitConverter.ToString(Encoding.UTF8.GetBytes(text));
            Logger.Info($"[TTS] Text bytes (hex): {hexBytes}");

            using (var wr = new UnityWebRequest(serverUrl, "POST"))
            {
                var bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
                wr.uploadHandler = new UploadHandlerRaw(bodyRaw);
                wr.downloadHandler = new DownloadHandlerBuffer();
                wr.SetRequestHeader("Content-Type", "application/json");
                wr.timeout = 60; // TTS inference can take 20-40s
                // Bypass SSL cert check for self-signed / LAN servers
                wr.certificateHandler = new BypassCertificate();

                Logger.Info($"[TTS] Sending HTTP request to {serverUrl}...");

                yield return wr.SendWebRequest();

                Logger.Info($"[TTS] HTTP response: code={wr.responseCode}, result={wr.result}, error=\"{wr.error}\"");

                if (wr.result == UnityWebRequest.Result.Success)
                {
                    var audioData = wr.downloadHandler.data;
                    var contentType = wr.GetResponseHeader("Content-Type") ?? "unknown";
                    Logger.Info($"[TTS] Response: {audioData?.Length ?? 0} bytes, content-type: {contentType}");

                    if (audioData != null && audioData.Length > 0)
                    {
                        // Check if server returned JSON error instead of WAV
                        if (contentType.Contains("json") || (audioData.Length < 100 && audioData[0] == '{'))
                        {
                            var jsonResponse = Encoding.UTF8.GetString(audioData);
                            Logger.Warn($"[TTS] Server returned JSON instead of audio: {jsonResponse}");
                        }
                        else
                        {
                            Logger.Info($"[TTS] Received {audioData.Length} bytes of audio, playing...");
                            yield return StartCoroutine(PlayAudioFromBytes(audioData));
                        }
                    }
                    else
                    {
                        Logger.Warn("[TTS] Server returned empty audio data");
                    }
                }
                else if (wr.responseCode == 400)
                {
                    Logger.Warn($"[TTS] Server rejected text (400): {wr.downloadHandler.text}");
                }
                else if (wr.responseCode == 503)
                {
                    Logger.Warn("[TTS] Server not ready (503) — skipping message");
                }
                else
                {
                    Logger.Warn($"[TTS] HTTP request FAILED (code={wr.responseCode}): {wr.error}");
                }
            }
        }

        // ── Audio Playback ────────────────────────────────────────

        private IEnumerator PlayAudioFromBytes(byte[] audioData)
        {
            // Write audio bytes to temp file
            Logger.Info($"[TTS] Writing {audioData.Length} bytes to {_tempAudioPath}");
            try
            {
                File.WriteAllBytes(_tempAudioPath, audioData);
            }
            catch (Exception e)
            {
                Logger.Error($"[TTS] Failed to write temp audio file: {e.Message}");
                yield break;
            }

            // Load AudioClip from temp file
            // Use file:/// URI (proper absolute-path format on all platforms)
            var filePath = _tempAudioPath.Replace('\\', '/');
            using (var wr = UnityWebRequestMultimedia.GetAudioClip(
                "file:///" + filePath, AudioType.WAV))
            {
                wr.certificateHandler = new BypassCertificate();
                yield return wr.SendWebRequest();

                Logger.Info($"[TTS] Loading audio from temp file...");

                if (wr.result == UnityWebRequest.Result.Success)
                {
                    var clip = DownloadHandlerAudioClip.GetContent(wr);
                    Logger.Info($"[TTS] AudioClip loaded: length={clip?.length ?? 0}s, channels={clip?.channels ?? 0}");
                    if (clip != null)
                    {
                        _audioSource.volume = ChatSpeechConfig.instance.SpeechVolume;
                        _audioSource.clip = clip;
                        _audioSource.Play();

                        Logger.Info($"[TTS] Playing audio ({clip.length:F1}s) at volume {_audioSource.volume}");

                        // Wait for playback to finish (or until stopped)
                        while (_audioSource.isPlaying)
                        {
                            yield return null;
                        }

                        // Clean up the clip
                        _audioSource.clip = null;
                        Destroy(clip);
                    }
                }
                else
                {
                    Logger.Warn($"TTS: Failed to load audio clip: {wr.error}");
                    // Try to clean up
                    _audioSource.clip = null;
                }
            }

            // Clean temp file
            try
            {
                if (File.Exists(_tempAudioPath))
                {
                    File.Delete(_tempAudioPath);
                }
            }
            catch { /* ignore cleanup errors */ }
        }

        // ── Text Processing ───────────────────────────────────────

        /// <summary>
        /// Remove rich text / HTML tags from a message for clean TTS input.
        /// Also removes common emote/badge markup patterns.
        /// </summary>
        internal static string StripRichText(string message)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;

            // Remove HTML/rich text tags
            var result = RichTextRegex.Replace(message, string.Empty);

            // Collapse multiple whitespace into single space
            result = Regex.Replace(result, @"\s+", " ");

            return result.Trim();
        }

        /// <summary>
        /// Normalize a TTS server address into a full URL.
        /// Accepts: "host:port", "http://host:port", "host:port/api/tts", "http://host:port/api/tts"
        /// </summary>
        private static string NormalizeServerUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            var normalized = url.Trim();

            // Add scheme if missing (force HTTPS — Unity 2022 blocks HTTP at transport level)
            if (!normalized.StartsWith("http://") && !normalized.StartsWith("https://"))
                normalized = "https://" + normalized;

            // Add /api/tts path if missing
            if (!normalized.EndsWith("/api/tts"))
            {
                if (normalized.EndsWith("/"))
                    normalized += "api/tts";
                else
                {
                    // Check if it already has a path like /api/tts
                    var uri = new Uri(normalized);
                    if (uri.AbsolutePath == "/" || string.IsNullOrEmpty(uri.AbsolutePath))
                        normalized = normalized.TrimEnd('/') + "/api/tts";
                }
            }

            Logger.Debug($"TTS: Normalized URL: {url} -> {normalized}");
            return normalized;
        }

        /// <summary>
        /// Bypass SSL certificate validation for self-signed or LAN servers.
        /// </summary>
        private class BypassCertificate : CertificateHandler
        {
            protected override bool ValidateCertificate(byte[] certificateData)
            {
                return true; // Accept all certificates
            }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            var sb = new StringBuilder(s.Length + 10);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
