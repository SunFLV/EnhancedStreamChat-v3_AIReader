using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EnhancedStreamChat.Chat
{
    /// <summary>
    /// Separate config file for AI TTS speech settings.
    /// Stored in: UserData/EnhancedStreamChat_Speech.ini
    /// 不依赖 ChatCore，使用轻量 INI 读写，保持与旧版配置文件兼容。
    /// </summary>
    public class ChatSpeechConfig
    {
        private const string SectionName = "Speech";
        private static ChatSpeechConfig s_instance;

        public static ChatSpeechConfig Instance => s_instance ??= new ChatSpeechConfig();

        /// <summary>兼容旧代码的写法。</summary>
        public static ChatSpeechConfig instance => Instance;

        /// <summary>音色/版本/后端列表刷新完成后触发，设置页据此更新下拉框。</summary>
        public static event Action ListsUpdated;

        public static void NotifyListsUpdated() => ListsUpdated?.Invoke();

        // ── Speech ────────────────────────────────────────────────
        // 2026-11-20 默认值取自 SunFLV 当前正在使用的语音设置
        public bool SpeechEnabled = true;

        /// <summary>TTS 服务器根地址，例如 https://6.sunflv.top:8000 。</summary>
        public string SpeechServerUrl = "https://6.sunflv.top:8000";

        /// <summary>
        /// 备用根地址。6.sunflv.top 只有 IPv6 记录，游戏内 UnityWebRequest 可能拿到空响应；
        /// 旧地址 ai.sunflv.top:39281 有 IPv4 且是同一套后端，主地址失败时自动回退到这里。
        /// </summary>
        public string SpeechServerUrlAlt = "https://ai.sunflv.top:39281";

        /// <summary>备用服务器列表（逗号分隔，仅在无法访问服务器时作为提示/手动填写用）。</summary>
        public string SpeechServerUrls = "6.sunflv.top:8000";

        /// <summary>声音预设名。</summary>
        public string SpeechVoice = "v4_phone";

        /// <summary>可选声音（逗号分隔，服务器返回后自动写入）。</summary>
        public string SpeechVoices = "default,v4_default,v4_tomori,v4_anon,v4_taki,v4_rana,v4_soyo,v4_phone,v4_sunflv";

        /// <summary>模型版本名；空字符串表示使用服务器默认版本。</summary>
        public string SpeechVersion = "v5turbo";

        /// <summary>可选模型版本（逗号分隔，服务器返回后自动写入）。</summary>
        public string SpeechVersions = "v4,v5turbo";

        /// <summary>后端服务器下标（对应 /api/servers 的 servers 数组）。</summary>
        public int SpeechServerIndex = 2;

        /// <summary>后端服务器显示名（“|”分隔，服务器返回后自动写入，顺序与下标一致）。</summary>
        public string SpeechServerNames = "本机 A770|9070XT (streaming6) ✕不可达|P106-90 fp16 (CUDA) ⚡";

        /// <summary>朗读过滤：0=全部，1=仅高亮/提及，2=仅礼物/醒目留言。</summary>
        public int SpeechFilterMode = 0;

        /// <summary>朗读音量（0.0 ~ 2.0，超过 1.0 由客户端增益实现）。</summary>
        public float SpeechVolume = 1.75f;

        /// <summary>朗读语速（0.5 ~ 2.0）。</summary>
        public float SpeechSpeed = 1.0f;

        /// <summary>最大队列长度。</summary>
        public int SpeechMaxQueueSize = 10;

        private readonly string _path;

        private ChatSpeechConfig()
        {
            var dir = Path.Combine(Environment.CurrentDirectory, "UserData");
            _path = Path.Combine(dir, "EnhancedStreamChat_Speech.ini");
            this.Load();
        }

        public string ConfigPath => _path;

        // ── 便捷访问 ──────────────────────────────────────────────

        /// <summary>规范化后的服务器根地址（带 scheme，去掉 /api/tts 后缀）。</summary>
        public string ServerBaseUrl
        {
            get
            {
                var raw = NormalizeBase(this.SpeechServerUrl);
                return raw.Length == 0 ? "https://6.sunflv.top:8000" : raw;
            }
        }

        public List<string> GetVoiceList()
        {
            var list = new List<string>();
            this.AddCsv(list, this.SpeechVoices);
            if (!list.Contains("default"))
            {
                list.Insert(0, "default");
            }
            return list;
        }

        /// <summary>主地址 + 备用地址（去重、规范化）。</summary>
        public List<string> GetBaseUrlCandidates()
        {
            var list = new List<string>();
            var primary = NormalizeBase(this.SpeechServerUrl);
            if (!string.IsNullOrEmpty(primary)) list.Add(primary);
            var alt = NormalizeBase(this.SpeechServerUrlAlt);
            if (!string.IsNullOrEmpty(alt) && !list.Contains(alt)) list.Add(alt);
            if (list.Count == 0) list.Add("https://ai.sunflv.top:39281");
            return list;
        }

        public static string NormalizeBase(string raw)
        {
            raw = (raw ?? string.Empty).Trim();
            if (raw.Length == 0) return string.Empty;
            if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                raw = "https://" + raw;
            }
            raw = raw.TrimEnd('/');
            if (raw.EndsWith("/api/tts", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw.Substring(0, raw.Length - "/api/tts".Length);
            }
            else if (raw.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw.Substring(0, raw.Length - "/api".Length);
            }
            return raw.TrimEnd('/');
        }

        public List<string> GetVersionList()
        {
            var list = new List<string>();
            this.AddCsv(list, this.SpeechVersions);
            return list;
        }

        public List<string> GetServerNameList()
        {
            var list = new List<string>();
            if (!string.IsNullOrEmpty(this.SpeechServerNames))
            {
                foreach (var name in this.SpeechServerNames.Split('|'))
                {
                    list.Add(name);
                }
            }
            return list;
        }

        public string GetServerDisplayName(int index)
        {
            var names = this.GetServerNameList();
            if (index >= 0 && index < names.Count)
            {
                return $"{index}: {names[index]}";
            }
            return $"{index}: ???";
        }

        private void AddCsv(List<string> list, string csv)
        {
            if (string.IsNullOrEmpty(csv)) return;
            foreach (var v in csv.Split(','))
            {
                var t = v.Trim();
                if (t.Length > 0 && !list.Contains(t))
                {
                    list.Add(t);
                }
            }
        }

        // ── INI 读写 ──────────────────────────────────────────────

        public void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                foreach (var rawLine in File.ReadAllLines(_path, Encoding.UTF8))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("[") || line.StartsWith(";") || line.StartsWith("#"))
                    {
                        continue;
                    }
                    var idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    var key = line.Substring(0, idx).Trim();
                    var value = this.CleanValue(line.Substring(idx + 1));
                    this.Apply(key, value);
                }

                if (string.IsNullOrWhiteSpace(this.SpeechServerUrlAlt))
                {
                    this.SpeechServerUrlAlt = "https://ai.sunflv.top:39281";
                }

                Logger.Info($"[TTS] Speech config loaded: {_path} (primary={this.ServerBaseUrl}, alt={this.SpeechServerUrlAlt})");
            }
            catch (Exception e)
            {
                Logger.Error($"[TTS] Failed to load speech config: {e}");
            }
        }

        /// <summary>
        /// ChatCore 的 INI 写法是 `Key="value" //comment`，这里兼容引号和行内注释。
        /// 注意 URL 里的 `https://` 不会被误当作注释（要求 // 前面有空白）。
        /// </summary>
        private string CleanValue(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var value = raw.Trim();

            for (var i = 1; i < value.Length - 1; i++)
            {
                if (value[i] == '/' && value[i + 1] == '/' && char.IsWhiteSpace(value[i - 1]))
                {
                    value = value.Substring(0, i).Trim();
                    break;
                }
            }

            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                value = value.Substring(1, value.Length - 2);
            }
            return value.Trim();
        }

        private void Apply(string key, string value)
        {
            switch (key)
            {
                case nameof(this.SpeechEnabled): this.SpeechEnabled = ParseBool(value, this.SpeechEnabled); break;
                case nameof(this.SpeechServerUrl): this.SpeechServerUrl = value; break;
                case nameof(this.SpeechServerUrlAlt): this.SpeechServerUrlAlt = value; break;
                case nameof(this.SpeechServerUrls): this.SpeechServerUrls = value; break;
                case nameof(this.SpeechVoice): this.SpeechVoice = value; break;
                case nameof(this.SpeechVoices): this.SpeechVoices = value; break;
                case nameof(this.SpeechVersion): this.SpeechVersion = value; break;
                case nameof(this.SpeechVersions): this.SpeechVersions = value; break;
                case nameof(this.SpeechServerIndex): this.SpeechServerIndex = ParseInt(value, this.SpeechServerIndex); break;
                case nameof(this.SpeechServerNames): this.SpeechServerNames = value; break;
                case nameof(this.SpeechFilterMode): this.SpeechFilterMode = ParseInt(value, this.SpeechFilterMode); break;
                case nameof(this.SpeechVolume): this.SpeechVolume = ParseFloat(value, this.SpeechVolume); break;
                case nameof(this.SpeechSpeed): this.SpeechSpeed = ParseFloat(value, this.SpeechSpeed); break;
                case nameof(this.SpeechMaxQueueSize): this.SpeechMaxQueueSize = ParseInt(value, this.SpeechMaxQueueSize); break;
            }
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var sb = new StringBuilder();
                sb.AppendLine($"[{SectionName}]");
                sb.AppendLine($"{nameof(this.SpeechEnabled)}={this.SpeechEnabled.ToString().ToLowerInvariant()}");
                sb.AppendLine($"{nameof(this.SpeechServerUrl)}={this.SpeechServerUrl}");
                sb.AppendLine($"{nameof(this.SpeechServerUrlAlt)}={this.SpeechServerUrlAlt}");
                sb.AppendLine($"{nameof(this.SpeechServerUrls)}={this.SpeechServerUrls}");
                sb.AppendLine($"{nameof(this.SpeechVoice)}={this.SpeechVoice}");
                sb.AppendLine($"{nameof(this.SpeechVoices)}={this.SpeechVoices}");
                sb.AppendLine($"{nameof(this.SpeechVersion)}={this.SpeechVersion}");
                sb.AppendLine($"{nameof(this.SpeechVersions)}={this.SpeechVersions}");
                sb.AppendLine($"{nameof(this.SpeechServerIndex)}={this.SpeechServerIndex.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"{nameof(this.SpeechServerNames)}={this.SpeechServerNames}");
                sb.AppendLine($"{nameof(this.SpeechFilterMode)}={this.SpeechFilterMode.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"{nameof(this.SpeechVolume)}={this.SpeechVolume.ToString("0.00", CultureInfo.InvariantCulture)}");
                sb.AppendLine($"{nameof(this.SpeechSpeed)}={this.SpeechSpeed.ToString("0.00", CultureInfo.InvariantCulture)}");
                sb.AppendLine($"{nameof(this.SpeechMaxQueueSize)}={this.SpeechMaxQueueSize.ToString(CultureInfo.InvariantCulture)}");
                File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Logger.Error($"[TTS] Failed to save speech config: {e}");
            }
        }

        private static bool ParseBool(string value, bool fallback) =>
            bool.TryParse(value, out var b) ? b : (value == "1" ? true : fallback);

        private static int ParseInt(string value, int fallback) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;

        private static float ParseFloat(string value, float fallback) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;
    }
}
