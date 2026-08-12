using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.Parser;
using BeatSaberMarkupLanguage.ViewControllers;
using EnhancedStreamChat.Utilities;
using HMUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EnhancedStreamChat.Chat
{
    public partial class ChatDisplay : BSMLAutomaticViewController
    {
        private bool SetProperty<T>(ref T oldValue, T newValue, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(oldValue, newValue)) {
                return false;
            }
            oldValue = newValue;
            this.OnPropertyChanged(new PropertyChangedEventArgs(name));
            return true;
        }

        private void OnPropertyChanged(PropertyChangedEventArgs e) => this.NotifyPropertyChanged(e.PropertyName);

        [UIAction("#post-parse")]
        private void PostParse()
        {
            try
            {
                // BSML 1.40.x: editButton/modalColorPicker removed (built-in color picker)
                if (this._backgroundColorSetting != null)
                    this._backgroundColorSetting.CurrentColor = this._chatConfig.BackgroundColor;
                if (this._accentColorSetting != null)
                    this._accentColorSetting.CurrentColor = this._chatConfig.AccentColor;
                if (this._highlightColorSetting != null)
                    this._highlightColorSetting.CurrentColor = this._chatConfig.HighlightColor;
                if (this._pingColorSetting != null)
                    this._pingColorSetting.CurrentColor = this._chatConfig.PingColor;
                if (this._textColorSetting != null)
                    this._textColorSetting.CurrentColor = this._chatConfig.TextColor;
                this.gameObject.layer = (int)this.textLayerVisibility;

                if (this.settingsModalGameObject != null)
                    this.settingsModalGameObject.transform.localPosition = new Vector3(
                        this.settingsModalGameObject.transform.localPosition.x,
                        this.settingsModalGameObject.transform.localPosition.y, -2f);
                if (this.settingsIconGameObject != null)
                    this.settingsIconGameObject.transform.localPosition = new Vector3(
                        this.settingsIconGameObject.transform.localPosition.x,
                        this.settingsIconGameObject.transform.localPosition.y, -2f);

                if (this.settingsIconGameObject != null)
                    this.settingsIconGameObject.layer = 5;
                if (this.settingsModalGameObject != null)
                    this.settingsModalGameObject.layer = 5;

                // Find and wire up the test-speech button directly (bypass BSML UIAction)
                WireTestSpeechButton();
                // Refresh voice list from server
                ChatSpeechManager.instance?.RefreshVoices();
            }
            catch (Exception ex)
            {
                Logger.Error($"PostParse failed: {ex}");
            }
        }

        private void WireTestSpeechButton()
        {
            if (this.settingsModalGameObject == null) return;
            var buttons = this.settingsModalGameObject.GetComponentsInChildren<Button>(true);
            foreach (var btn in buttons)
            {
                if (btn.name.Contains("测试朗读") || (btn.GetComponentInChildren<TextMeshProUGUI>()?.text?.Contains("测试朗读") == true))
                {
                    btn.onClick.AddListener(() =>
                    {
                        Logger.Info("[TTS] Test speech button clicked (direct)");
                        ChatSpeechManager.instance?.TestSpeech("欢迎来到直播间，AI语音朗读已就绪");
                    });
                    Logger.Info("[TTS] Test speech button wired successfully");
                    return;
                }
            }
            // Fallback: try finding in the whole GameObject tree
            foreach (var btn in this.GetComponentsInChildren<Button>(true))
            {
                var label = btn.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null && label.text != null && (label.text.Contains("朗读") || label.text.Contains("Test")))
                {
                    btn.onClick.AddListener(() =>
                    {
                        Logger.Info("[TTS] Test speech button clicked (fallback)");
                        ChatSpeechManager.instance?.TestSpeech("欢迎来到直播间，AI语音朗读已就绪");
                    });
                    Logger.Info($"[TTS] Test speech button wired via fallback: {label.text}");
                    return;
                }
            }
            Logger.Warn("[TTS] Could not find test speech button to wire!");
        }

        [UIParams]
        internal BSMLParserParams parserParams;

        [UIObject("settings-icon")]
        internal GameObject settingsIconGameObject;

        [UIObject("settings-modal")]
        internal GameObject settingsModalGameObject;

        [UIComponent("background-color-setting")]
        private readonly ColorSetting _backgroundColorSetting;

        [UIComponent("accent-color-setting")]
        private readonly ColorSetting _accentColorSetting;

        [UIComponent("highlight-color-setting")]
        private readonly ColorSetting _highlightColorSetting;

        [UIComponent("ping-color-setting")]
        private readonly ColorSetting _pingColorSetting;

        [UIComponent("text-color-setting")]
        private readonly ColorSetting _textColorSetting;

        [UIComponent("text-layer-settings")]
        private readonly DropDownListSetting _textLayerSetting;

        internal BeatSaberUtils.TextLayerVisibility textLayerVisibility
        {
            get => this._isInGame ? BeatSaberUtils.textLayerVisibilityReverser(this._chatConfig.Song_ChatLayer) : BeatSaberUtils.textLayerVisibilityReverser(this._chatConfig.Menu_ChatLayer);
            set
            {
                if (this._isInGame || this.SyncOrientation)
                {
                    this._chatConfig.Song_ChatLayer = (int)value;
                }

                if (!this._isInGame || this.SyncOrientation)
                {
                    this._chatConfig.Menu_ChatLayer = (int)value;
                }
            }
        }

        private static readonly List<object> textLayerVisibilities = Enum.GetValues(typeof(BeatSaberUtils.TextLayerVisibility)).Cast<object>().ToList();

        [UIValue("accent-color")]
        public Color AccentColor
        {
            get => this._chatConfig.AccentColor;
            set
            {
                this.SetProperty(ref this._chatConfig.AccentColor, value);
                this.UpdateMessages();
            }
        }

        [UIValue("highlight-color")]
        public Color HighlightColor
        {
            get => this._chatConfig.HighlightColor;
            set
            {
                this.SetProperty(ref this._chatConfig.HighlightColor, value);
                this.UpdateMessages();
            }
        }

        [UIValue("ping-color")]
        public Color PingColor
        {
            get => this._chatConfig.PingColor;
            set
            {
                this.SetProperty(ref this._chatConfig.PingColor, value);
                this.UpdateMessages();
            }
        }

        [UIValue("background-color")]
        public Color BackgroundColor
        {
            get => this._chatConfig.BackgroundColor;
            set
            {
                this.SetProperty(ref this._chatConfig.BackgroundColor, value);
                this._chatScreen.GetComponentsInChildren<ImageView>().FirstOrDefault(x => x.name == "Background").color = value;
            }
        }

        [UIValue("text-color")]
        public Color TextColor
        {
            get => this._chatConfig.TextColor;
            set
            {
                this.SetProperty(ref this._chatConfig.TextColor, value);
                this.UpdateMessages();
            }
        }

        [UIValue("font-size")]
        public float FontSize
        {
            get => this._chatConfig.FontSize;
            set
            {
                this.SetProperty(ref this._chatConfig.FontSize, value);
                this.UpdateMessages();
            }
        }

        private int _settingsWidth = 110;
        [UIValue("settings-width")]
        public int SettingsWidth
        {
            get => this._settingsWidth;
            set => this.SetProperty(ref this._settingsWidth, value);
        }

        [UIValue("chat-width")]
        public int ChatWidth
        {
            get => this._chatConfig.ChatWidth;
            set
            {
                this.SetProperty(ref this._chatConfig.ChatWidth, value);
                this._chatScreen.ScreenSize = new Vector2(this.ChatWidth, this.ChatHeight);
                this._chatContainer.GetComponent<RectMask2D>().rectTransform.sizeDelta = new Vector2(this.ChatWidth, this.ChatHeight);
                this.UpdateMessages();
            }
        }

        [UIValue("chat-height")]
        public int ChatHeight
        {
            get => this._chatConfig.ChatHeight;
            set
            {
                this.SetProperty(ref this._chatConfig.ChatHeight, value);
                this._chatScreen.ScreenSize = new Vector2(this.ChatWidth, this.ChatHeight);
                this._chatContainer.GetComponent<RectMask2D>().rectTransform.sizeDelta = new Vector2(this.ChatWidth, this.ChatHeight);
                this.UpdateMessages();
            }
        }

        [UIValue("chat-position")]
        public Vector3 ChatPosition
        {
            get => this._isInGame ? this._chatConfig.Song_ChatPosition : this._chatConfig.Menu_ChatPosition;
            set
            {
                this._chatScreen.ScreenPosition = value;
                if (this._isInGame || this.SyncOrientation) {
                    this.SetProperty(ref this._chatConfig.Song_ChatPosition, value);
                }

                if (!this._isInGame || this.SyncOrientation) {
                    this.SetProperty(ref this._chatConfig.Menu_ChatPosition, value);
                }
            }
        }

        [UIValue("chat-rotation")]
        public Vector3 ChatRotation
        {
            get => this._isInGame ? this._chatConfig.Song_ChatRotation : this._chatConfig.Menu_ChatRotation;
            set
            {
                this._chatScreen.ScreenRotation = Quaternion.Euler(value);
                if (this._isInGame || this.SyncOrientation) {
                    this.SetProperty(ref this._chatConfig.Song_ChatRotation, value);
                }

                if (!this._isInGame || this.SyncOrientation) {
                    this.SetProperty(ref this._chatConfig.Menu_ChatRotation, value);
                }
            }
        }

        [UIValue("allow-movement")]
        public bool AllowMovement
        {
            get => this._chatConfig.AllowMovement;
            set
            {
                this.SetProperty(ref this._chatConfig.AllowMovement, value);
                this._chatScreen.ShowHandle = value;
            }
        }

        [UIValue("sync-orientation")]
        public bool SyncOrientation
        {
            get => this._chatConfig.SyncOrientation;
            set
            {
                this.SetProperty(ref this._chatConfig.SyncOrientation, value);
                if (value) {
                    this.ChatPosition = this.ChatPosition;
                    this.ChatRotation = this.ChatRotation;
                }
            }
        }

        [UIValue("reverse-chat-order")]
        public bool ReverseChatOrder
        {
            get => this._chatConfig.ReverseChatOrder;
            set
            {
                this.SetProperty(ref this._chatConfig.ReverseChatOrder, value);
                this.UpdateMessages();
            }
        }

        [UIValue("mod-version")]
        public string ModVersion => Plugin.Version;

        [UIAction("launch-web-app")]
        private void LaunchWebApp() => ChatManager.instance._chatCoreInstance.LaunchWebApp();

        [UIAction("launch-kofi")]
        private void LaunchKofi() => Application.OpenURL("https://ko-fi.com/brian91292");

        [UIAction("launch-github")]
        private void LaunchGitHub() => Application.OpenURL("https://github.com/baoziii/EnhancedStreamChat-v3");

        [UIAction("on-settings-clicked")]
        private void OnSettingsClick() => Logger.Info("Settings clicked!");

        [UIAction("#hide-settings")]
        private void OnHideSettings()
        {
            Logger.Info("Saving settings!");
            this._chatConfig.Save();
            ChatSpeechConfig.instance.Save();
        }

        private void HideSettings() => this.parserParams.EmitEvent("hide-settings");

        private void ShowSettings() => this.parserParams.EmitEvent("show-settings");

        // ── Speech/TTS Settings ───────────────────────────────────

        private static readonly List<object> speechFilterModes = new List<object>
        {
            "全部弹幕",
            "仅高亮/提及",
            "仅礼物/醒目留言"
        };

        [UIValue("speechFilterModes")]
        private List<object> SpeechFilterModesList => speechFilterModes;

        private static List<object> _speechVoices => ParseVoicesList();

        private static List<object> ParseVoicesList()
        {
            var voices = new List<object> { "default" };
            var raw = ChatSpeechConfig.instance.SpeechVoices;
            if (!string.IsNullOrEmpty(raw))
            {
                foreach (var v in raw.Split(','))
                {
                    var trimmed = v.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !voices.Contains(trimmed))
                        voices.Add(trimmed);
                }
            }
            return voices;
        }

        internal static List<object> SpeechVoicesInternal => _speechVoices;

        [UIValue("speechVoices")]
        private List<object> SpeechVoicesList => _speechVoices;

        // Server URL dropdown — populated from config SpeechServerUrls (comma-separated)
        private List<object> _speechServerUrls => ParseServerUrls();

        [UIValue("speechServerUrls")]
        private List<object> SpeechServerUrlChoices => _speechServerUrls;

        private List<object> ParseServerUrls()
        {
            var urls = new List<object>();
            var raw = ChatSpeechConfig.instance.SpeechServerUrls;
            if (!string.IsNullOrEmpty(raw))
            {
                foreach (var url in raw.Split(','))
                {
                    var trimmed = url.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        urls.Add(trimmed);
                }
            }
            if (urls.Count == 0)
                urls.Add("ai.sunflv.top:39281");
            return urls;
        }

        [UIValue("speech-server-url")]
        public string SpeechServerUrl
        {
            get
            {
                // Show short form in dropdown: "host:port" from "http://host:port/api/tts"
                var url = ChatSpeechConfig.instance.SpeechServerUrl;
                if (url.StartsWith("http://")) url = url.Substring(7);
                if (url.StartsWith("https://")) url = url.Substring(8);
                if (url.EndsWith("/api/tts")) url = url.Substring(0, url.Length - 8);
                return url;
            }
            set
            {
                if (!string.IsNullOrEmpty(value))
                    this.SetProperty(ref ChatSpeechConfig.instance.SpeechServerUrl, value);
            }
        }

        [UIValue("speech-enabled")]
        public bool SpeechEnabled
        {
            get => ChatSpeechConfig.instance.SpeechEnabled;
            set => this.SetProperty(ref ChatSpeechConfig.instance.SpeechEnabled, value);
        }

        [UIValue("speech-filter-mode")]
        public string SpeechFilterMode
        {
            get
            {
                var idx = ChatSpeechConfig.instance.SpeechFilterMode;
                if (idx >= 0 && idx < speechFilterModes.Count)
                    return speechFilterModes[idx] as string;
                return speechFilterModes[0] as string;
            }
            set
            {
                var index = speechFilterModes.FindIndex(x => (string)x == value);
                if (index >= 0)
                    this.SetProperty(ref ChatSpeechConfig.instance.SpeechFilterMode, index);
            }
        }

        [UIValue("speech-voice")]
        public string SpeechVoice
        {
            get => ChatSpeechConfig.instance.SpeechVoice;
            set
            {
                if (this.SetProperty(ref ChatSpeechConfig.instance.SpeechVoice, value))
                    ChatSpeechConfig.instance.Save();
            }
        }

        [UIValue("speech-volume")]
        public float SpeechVolume
        {
            get => ChatSpeechConfig.instance.SpeechVolume;
            set => this.SetProperty(ref ChatSpeechConfig.instance.SpeechVolume, value);
        }

        [UIValue("speech-speed")]
        public float SpeechSpeed
        {
            get => ChatSpeechConfig.instance.SpeechSpeed;
            set => this.SetProperty(ref ChatSpeechConfig.instance.SpeechSpeed, value);
        }

        [UIValue("speech-max-queue-size")]
        public int SpeechMaxQueueSize
        {
            get => ChatSpeechConfig.instance.SpeechMaxQueueSize;
            set => this.SetProperty(ref ChatSpeechConfig.instance.SpeechMaxQueueSize, value);
        }

        [UIAction("test-speech")]
        private void TestSpeech()
        {
            try
            {
                Logger.Info("[TTS] Test speech button clicked");
                var mgr = ChatSpeechManager.instance;
                if (mgr == null)
                {
                    Logger.Error("[TTS] ChatSpeechManager.instance is null!");
                    return;
                }
                // Use ASCII-safe text to avoid source encoding issues, then Chinese
            mgr.TestSpeech("6B228FCE6765523076F464AD95F4FF0CAI8BED97F367178BFB529F80FD5DF25C317EEA");
            }
            catch (Exception ex)
            {
                Logger.Error($"[TTS] TestSpeech failed: {ex}");
            }
        }
    }
}
