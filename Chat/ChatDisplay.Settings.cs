using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.Parser;
using BeatSaberMarkupLanguage.ViewControllers;
using EnhancedStreamChat.Configuration;
using EnhancedStreamChat.Utilities;
using HMUI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using static EnhancedStreamChat.Configuration.PluginConfig;

namespace EnhancedStreamChat.Chat
{
    public partial class ChatDisplay : BSMLAutomaticViewController
    {
        private bool SetProperty<T>(ref T oldValue, T newValue, [CallerMemberName] string name = null)
        {
#if DEBUG
            Logger.Info($"Change value:{oldValue}, {newValue}");
#endif
            if (EqualityComparer<T>.Default.Equals(oldValue, newValue)) {
                return false;
            }
            oldValue = newValue;
            MainThreadInvoker.Invoke(() => this.OnPropertyChanged(new PropertyChangedEventArgs(name)));
            return true;
        }

        private void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            this.NotifyPropertyChanged(e.PropertyName);
#if DEBUG
            Logger.Info($"property changed:{e.PropertyName}");
#endif
            if (e.PropertyName == nameof(this.AccentColor)) {
                this._chatConfig.AccentColor = this.AccentColor;
            }
            else if (e.PropertyName == nameof(this.TextColor)) {
                this._chatConfig.TextColor = this.TextColor;
            }
            else if (e.PropertyName == nameof(this.BackgroundColor)) {
                this._chatConfig.BackgroundColor = this.BackgroundColor;
            }
            else if (e.PropertyName == nameof(this.AllowMovement)) {
                this._chatConfig.AllowMovement = this.AllowMovement;
            }
            else if (e.PropertyName == nameof(this.ChatHeight)) {
                this._chatConfig.ChatHeight = this.ChatHeight;
            }
            else if (e.PropertyName == nameof(this.ChatWidth)) {
                this._chatConfig.ChatWidth = this.ChatWidth;
            }
            else if (e.PropertyName == nameof(this.ChatPosition)) {
                if (this._chatConfig.SyncOrientation) {
                    this._chatConfig.Menu_ChatPosition = this.ChatPosition;
                    this._chatConfig.Song_ChatPosition = this.ChatPosition;
                }
                else {
                    if (this._isInGame) {
                        this._chatConfig.Song_ChatPosition = this.ChatPosition;
                    }
                    else {
                        this._chatConfig.Menu_ChatPosition = this.ChatPosition;
                    }
                }
            }
            else if (e.PropertyName == nameof(this.ChatRotation)) {
                if (this._chatConfig.SyncOrientation) {
                    this._chatConfig.Menu_ChatRotation = this.ChatRotation;
                    this._chatConfig.Song_ChatRotation = this.ChatRotation;
                }
                else {
                    if (this._isInGame) {
                        this._chatConfig.Song_ChatRotation = this.ChatRotation;
                    }
                    else {
                        this._chatConfig.Menu_ChatRotation = this.ChatRotation;
                    }
                }
            }
            else if (e.PropertyName == nameof(this.FontSize)) {
                this._chatConfig.FontSize = this.FontSize;
            }
            else if (e.PropertyName == nameof(this.HighlightColor)) {
                this._chatConfig.HighlightColor = this.HighlightColor;
            }
            else if (e.PropertyName == nameof(this.PingColor)) {
                this._chatConfig.PingColor = this.PingColor;
            }
            else if (e.PropertyName == nameof(this.ReverseChatOrder)) {
                this._chatConfig.ReverseChatOrder = this.ReverseChatOrder;
            }
            else if (e.PropertyName == nameof(this.KeepHistoryOnSoftRestart)) {
                this._chatConfig.KeepHistoryOnSoftRestart = this.KeepHistoryOnSoftRestart;
            }
            else if (e.PropertyName == nameof(this.SyncOrientation)) {
                this._chatConfig.SyncOrientation = this.SyncOrientation;
                if (this._chatConfig.SyncOrientation) {
                    if (this._isInGame) {
                        this._chatConfig.Menu_ChatPosition = this._chatConfig.Song_ChatPosition;
                        this._chatConfig.Menu_ChatRotation = this._chatConfig.Song_ChatRotation;
                    }
                    else {
                        this._chatConfig.Song_ChatPosition = this._chatConfig.Menu_ChatPosition;
                        this._chatConfig.Song_ChatRotation = this._chatConfig.Menu_ChatRotation;
                    }
                }
            }
            else if (e.PropertyName == nameof(this.Layer)) {
                var layer = Enum.GetValues(typeof(PluginConfig.LayerType)).OfType<PluginConfig.LayerType>().FirstOrDefault(x => x.ToString() == this.Layer.ToString());
                this._chatConfig.Layer = layer;
                switch (layer) {
                    case LayerType.HMDOnly:
                    case LayerType.UI:
                        this._chatConfig.UILayer = (int)layer;
                        break;
                    case LayerType.Manual:
                    default:
                        break;
                }
                this.SetCurrentLayer(this._chatConfig.UILayer);
            }
        }

        [UIAction("#post-parse")]
        protected void PostParse()
        {
            this.Load();
            // bg
            this._backgroundColorSetting.EditButton.onClick.AddListener(this.HideSettings);
            this._backgroundColorSetting.ModalColorPicker.CancelEvent += this.ShowSettings;
            this._backgroundColorSetting.CurrentColor = this._chatConfig.BackgroundColor;
            // accent
            this._accentColorSetting.EditButton.onClick.AddListener(this.HideSettings);
            this._accentColorSetting.ModalColorPicker.CancelEvent += this.ShowSettings;
            this._accentColorSetting.CurrentColor = this._chatConfig.AccentColor;
            // highlight
            this._highlightColorSetting.EditButton.onClick.AddListener(this.HideSettings);
            this._highlightColorSetting.ModalColorPicker.CancelEvent += this.ShowSettings;
            this._highlightColorSetting.CurrentColor = this._chatConfig.HighlightColor;
            // ping
            this._pingColorSetting.EditButton.onClick.AddListener(this.HideSettings);
            this._pingColorSetting.ModalColorPicker.CancelEvent += this.ShowSettings;
            this._pingColorSetting.CurrentColor = this._chatConfig.PingColor;
            // text
            this._textColorSetting.EditButton.onClick.AddListener(this.HideSettings);
            this._textColorSetting.ModalColorPicker.CancelEvent += this.ShowSettings;
            this._textColorSetting.CurrentColor = this._chatConfig.TextColor;

            // Move interactables in front of the screen
            this._settingsModalGameObject.transform.localPosition = new Vector3(this._settingsModalGameObject.transform.localPosition.x, this._settingsModalGameObject.transform.localPosition.y, -2f);
            this._settingsIconGameObject.transform.localPosition = new Vector3(this._settingsIconGameObject.transform.localPosition.x, this._settingsIconGameObject.transform.localPosition.y, -2f);

            if (this._chatConfig.Layer == PluginConfig.LayerType.Manual) {
                this.SetCurrentLayer(this._chatConfig.UILayer);
            }
            else {
                this.SetCurrentLayer((int)this._chatConfig.Layer);
            }

            // TTS：初始化下拉框并拉取服务器/音色/模型版本
            ChatSpeechConfig.ListsUpdated -= this.UpdateSpeechChoices;
            ChatSpeechConfig.ListsUpdated += this.UpdateSpeechChoices;
            this.UpdateSpeechChoices();
            ChatSpeechManager.Instance?.RefreshVoices();
        }

        [UIParams]
        internal BSMLParserParams _parserParams;

        [UIObject("settings-icon")]
        internal GameObject _settingsIconGameObject;

        [UIObject("settings-modal")]
        internal GameObject _settingsModalGameObject;

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

        private Color _accentColor;
        [UIValue("accent-color")]
        public Color AccentColor
        {
            get => this._accentColor;

            set
            {
                _ = this.SetProperty(ref this._accentColor, value);
                this.UpdateMessages();
            }
        }
        private Color _highlightColor;
        [UIValue("highlight-color")]
        public Color HighlightColor
        {
            get => this._highlightColor;
            set
            {
                _ = this.SetProperty(ref this._highlightColor, value);
                this.UpdateMessages();
            }
        }

        private Color _pingColor;
        [UIValue("ping-color")]
        public Color PingColor
        {
            get => this._pingColor;
            set
            {
                _ = this.SetProperty(ref this._pingColor, value);
                this.UpdateMessages();
            }
        }

        private Color _backGroundColor;
        [UIValue("background-color")]
        public Color BackgroundColor
        {
            get => this._backGroundColor;
            set
            {
                _ = this.SetProperty(ref this._backGroundColor, value);
                if (this._chatScreen) {
                    this._chatScreen.GetComponentsInChildren<ImageView>().FirstOrDefault(x => x.name == "Background").color = value;
                }
            }
        }

        private Color _textColor;
        [UIValue("text-color")]
        public Color TextColor
        {
            get => this._textColor;
            set
            {
                _ = this.SetProperty(ref this._textColor, value);
                this.UpdateMessages();
            }
        }

        private float _fontsize;
        [UIValue("font-size")]
        public float FontSize
        {
            get => this._fontsize;
            set
            {
                _ = this.SetProperty(ref this._fontsize, value);
                this.UpdateMessages();
            }
        }

        [UIValue("layer-types")]
        private readonly List<object> _layerTypes = new List<object>() { $"{PluginConfig.LayerType.UI}", $"{PluginConfig.LayerType.HMDOnly}", $"{PluginConfig.LayerType.Manual}" };

        /// <summary>説明 を取得、設定</summary>
        private string _layer = $"{PluginConfig.LayerType.UI}";
        /// <summary>説明 を取得、設定</summary>
        [UIValue("layer-type")]
        public string Layer
        {
            get => this._layer;

            set => this.SetProperty(ref this._layer, value);
        }

        private int _settingsWidth = 110;
        [UIValue("settings-width")]
        public int SettingsWidth
        {
            get => this._settingsWidth;
            set => this.SetProperty(ref this._settingsWidth, value);
        }

        private int _chatWidth;
        [UIValue("chat-width")]
        public int ChatWidth
        {
            get => this._chatWidth;
            set
            {
                _ = this.SetProperty(ref this._chatWidth, value);
                this._chatScreen.ScreenSize = new Vector2(this.ChatWidth, this.ChatHeight);
                this._chatContainer.GetComponent<RectMask2D>().rectTransform.sizeDelta = new Vector2(this.ChatWidth, this.ChatHeight);
                this.UpdateMessages();
            }
        }

        private int _chatHeight;
        [UIValue("chat-height")]
        public int ChatHeight
        {
            get => this._chatHeight;
            set
            {
                _ = this.SetProperty(ref this._chatHeight, value);
                this._chatScreen.ScreenSize = new Vector2(this.ChatWidth, this.ChatHeight);
                this._chatContainer.GetComponent<RectMask2D>().rectTransform.sizeDelta = new Vector2(this.ChatWidth, this.ChatHeight);
                this.UpdateMessages();
            }
        }

        private Vector3 _chatPosition;
        [UIValue("chat-position")]
        public Vector3 ChatPosition
        {
            get => this._chatPosition;
            set
            {
                _ = this.SetProperty(ref this._chatPosition, value);
                this._chatScreen.ScreenPosition = value;
            }
        }

        private Vector3 _chatRotation;
        [UIValue("chat-rotation")]
        public Vector3 ChatRotation
        {
            get => this._chatRotation;
            set
            {
                _ = this.SetProperty(ref this._chatRotation, value);
                this._chatScreen.ScreenRotation = Quaternion.Euler(value);
            }
        }

        private bool _allowMovement;
        [UIValue("allow-movement")]
        public bool AllowMovement
        {
            get => this._allowMovement;
            set
            {
                _ = this.SetProperty(ref this._allowMovement, value);
                this._chatScreen.ShowHandle = value;
            }
        }

        private bool _syncOrientation;
        [UIValue("sync-orientation")]
        public bool SyncOrientation
        {
            get => this._syncOrientation;
            set => this.SetProperty(ref this._syncOrientation, value);
        }

        private bool _reverseChatOrder;
        [UIValue("reverse-chat-order")]
        public bool ReverseChatOrder
        {
            get => this._reverseChatOrder;
            set
            {
                _ = this.SetProperty(ref this._reverseChatOrder, value);
                this._updateMessagePositions = true;
                //this.UpdateMessages();
            }
        }

        private bool _keepHistoryOnSoftRestart;
        [UIValue("keep-history-on-soft-restart")]
        public bool KeepHistoryOnSoftRestart
        {
            get => this._keepHistoryOnSoftRestart;
            set => this.SetProperty(ref this._keepHistoryOnSoftRestart, value);
        }

        /// <summary>説明 を取得、設定</summary>
        private bool _reconnectEnable = true;
        [UIValue("re-connect-enable")]
        public bool ReconnectEnable
        {
            get => this._reconnectEnable;

            set => this.SetProperty(ref this._reconnectEnable, value);
        }

        [UIValue("mod-version")]
        public string ModVersion => Plugin.Version;

        [UIAction("launch-web-app")]
        protected void LaunchWebApp()
        {
            this._catCoreManager.LaunchWebPortal();
        }

        [UIAction("launch-kofi")]
        protected void LaunchKofi()
        {
            Application.OpenURL("https://ko-fi.com/brian91292");
        }

        [UIAction("launch-github")]
        protected void LaunchGitHub()
        {
            Application.OpenURL("https://github.com/denpadokei/EnhancedStreamChat-v3");
        }

        [UIAction("on-settings-clicked")]
        protected void OnSettingsClick()
        {
            Logger.Info("Settings clicked!");
        }

        [UIAction("#hide-settings")]
        protected void OnHideSettings()
        {
            Logger.Info("Saving settings!");
        }

        [UIAction("re-connect")]
        protected async void ReIrcConnect()
        {
            if (!this.ReconnectEnable) {
                return;
            }
            await this._connectSemaphore.WaitAsync();
            try {
                this.ReconnectEnable = false;
                await Task.Delay(s_reconnectDelay);
                this.BeginConnectionAttempt(ConnectionAttemptKind.ManualReconnect);
                await this._catCoreManager.IrcStart();
            }
            catch (System.Exception e) {
                this.ShowConnectionFailureNotice(ConnectionAttemptKind.ManualReconnect);
                Logger.Error(e);
            }
            finally {
                this.ReconnectEnable = true;
                _ = this._connectSemaphore.Release();
            }
        }

        protected async void PubSubReconnect(object instance)
        {
            await this._connectSemaphore.WaitAsync();
            try {
                await Task.Delay(s_reconnectDelay);
                await this._catCoreManager.PubSubStart(instance);
            }
            catch (System.Exception e) {
                Logger.Error(e);
            }
            finally {
                _ = this._connectSemaphore.Release();
            }
        }

        private void HideSettings()
        {
            this._parserParams.EmitEvent("hide-settings");
        }

        private void ShowSettings()
        {
            this._parserParams.EmitEvent("show-settings");
        }
        private void Load()
        {
            this.AccentColor = this._chatConfig.AccentColor;
            this.TextColor = this._chatConfig.TextColor;
            this.BackgroundColor = this._chatConfig.BackgroundColor;
            this.AllowMovement = this._chatConfig.AllowMovement;
            this.ChatHeight = this._chatConfig.ChatHeight;
            this.ChatWidth = this._chatConfig.ChatWidth;
            this.FontSize = this._chatConfig.FontSize;
            this.HighlightColor = this._chatConfig.HighlightColor;
            this.PingColor = this._chatConfig.PingColor;
            this.ReverseChatOrder = this._chatConfig.ReverseChatOrder;
            this.KeepHistoryOnSoftRestart = this._chatConfig.KeepHistoryOnSoftRestart;
            this.SyncOrientation = this._chatConfig.SyncOrientation;
            if (this._isInGame) {
                this.ChatPosition = this._chatConfig.Song_ChatPosition;
                this.ChatRotation = this._chatConfig.Song_ChatRotation;
            }
            else {
                this.ChatPosition = this._chatConfig.Menu_ChatPosition;
                this.ChatRotation = this._chatConfig.Menu_ChatRotation;
            }
            this.Layer = this._chatConfig.Layer.ToString();
        }

        // ── Speech / TTS Settings ─────────────────────────────────

        private static readonly List<object> s_speechFilterModes = new List<object>
        {
            "全部弹幕",
            "仅高亮/提及",
            "仅礼物/醒目留言"
        };

        private const string SpeechVersionDefaultLabel = "服务器默认";

        [UIValue("speech-filter-modes")]
        private List<object> SpeechFilterModesList => s_speechFilterModes;

        [UIValue("speech-enabled")]
        public bool SpeechEnabled
        {
            get => ChatSpeechConfig.Instance.SpeechEnabled;
            set
            {
                if (this.SetProperty(ref ChatSpeechConfig.Instance.SpeechEnabled, value))
                {
                    ChatSpeechConfig.Instance.Save();
                }
            }
        }

        [UIValue("speech-filter-mode")]
        public string SpeechFilterMode
        {
            get
            {
                var idx = ChatSpeechConfig.Instance.SpeechFilterMode;
                return idx >= 0 && idx < s_speechFilterModes.Count
                    ? (string)s_speechFilterModes[idx]
                    : (string)s_speechFilterModes[0];
            }
            set
            {
                var index = s_speechFilterModes.FindIndex(x => (string)x == value);
                if (index >= 0 && this.SetProperty(ref ChatSpeechConfig.Instance.SpeechFilterMode, index))
                {
                    ChatSpeechConfig.Instance.Save();
                }
            }
        }

        [UIValue("speech-voices")]
        private List<object> SpeechVoicesList => this.BuildVoiceChoices();

        [UIValue("speech-voice")]
        public string SpeechVoice
        {
            get => ChatSpeechConfig.Instance.SpeechVoice;
            set
            {
                if (this.SetProperty(ref ChatSpeechConfig.Instance.SpeechVoice, value))
                {
                    ChatSpeechConfig.Instance.Save();
                }
            }
        }

        [UIValue("speech-versions")]
        private List<object> SpeechVersionsList => this.BuildVersionChoices();

        [UIValue("speech-version")]
        public string SpeechVersion
        {
            get
            {
                var v = ChatSpeechConfig.Instance.SpeechVersion;
                return string.IsNullOrEmpty(v) ? SpeechVersionDefaultLabel : v;
            }
            set
            {
                var v = value == SpeechVersionDefaultLabel ? string.Empty : value;
                if (ChatSpeechConfig.Instance.SpeechVersion != v)
                {
                    ChatSpeechConfig.Instance.SpeechVersion = v;
                    ChatSpeechConfig.Instance.Save();
                    this.NotifyPropertyChanged(nameof(this.SpeechVersion));
                }
            }
        }

        [UIValue("speech-servers")]
        private List<object> SpeechServersList => this.BuildServerChoices();

        [UIValue("speech-server")]
        public string SpeechServer
        {
            get => ChatSpeechConfig.Instance.GetServerDisplayName(ChatSpeechConfig.Instance.SpeechServerIndex);
            set
            {
                var idx = ParseServerIndex(value);
                if (idx >= 0 && this.SetProperty(ref ChatSpeechConfig.Instance.SpeechServerIndex, idx))
                {
                    ChatSpeechConfig.Instance.Save();
                    ChatSpeechManager.Instance?.RefreshVoices();
                }
            }
        }

        [UIValue("speech-volume")]
        public float SpeechVolume
        {
            get => ChatSpeechConfig.Instance.SpeechVolume;
            set
            {
                if (this.SetProperty(ref ChatSpeechConfig.Instance.SpeechVolume, value))
                {
                    ChatSpeechConfig.Instance.Save();
                }
            }
        }

        [UIValue("speech-speed")]
        public float SpeechSpeed
        {
            get => ChatSpeechConfig.Instance.SpeechSpeed;
            set
            {
                if (this.SetProperty(ref ChatSpeechConfig.Instance.SpeechSpeed, value))
                {
                    ChatSpeechConfig.Instance.Save();
                }
            }
        }

        [UIValue("speech-max-queue-size")]
        public int SpeechMaxQueueSize
        {
            get => ChatSpeechConfig.Instance.SpeechMaxQueueSize;
            set
            {
                if (this.SetProperty(ref ChatSpeechConfig.Instance.SpeechMaxQueueSize, value))
                {
                    ChatSpeechConfig.Instance.Save();
                }
            }
        }

        [UIAction("test-speech")]
        private void TestSpeech()
        {
            Logger.Info("[TTS] Test speech button clicked");
            ChatSpeechManager.Instance?.TestSpeech("欢迎来到直播间，AI语音朗读已就绪");
        }

        [UIAction("refresh-speech")]
        private void RefreshSpeech()
        {
            ChatSpeechManager.Instance?.RefreshVoices();
        }

        [UIComponent("speech-voice-setting")]
        private readonly DropDownListSetting _speechVoiceSetting;

        [UIComponent("speech-version-setting")]
        private readonly DropDownListSetting _speechVersionSetting;

        [UIComponent("speech-server-setting")]
        private readonly DropDownListSetting _speechServerSetting;

        private List<object> BuildVoiceChoices()
        {
            var result = new List<object>();
            foreach (var v in ChatSpeechConfig.Instance.GetVoiceList())
            {
                result.Add(v);
            }
            return result;
        }

        private List<object> BuildVersionChoices()
        {
            var result = new List<object> { SpeechVersionDefaultLabel };
            foreach (var v in ChatSpeechConfig.Instance.GetVersionList())
            {
                result.Add(v);
            }
            return result;
        }

        private List<object> BuildServerChoices()
        {
            var result = new List<object>();
            var names = ChatSpeechConfig.Instance.GetServerNameList();
            if (names.Count == 0)
            {
                result.Add(ChatSpeechConfig.Instance.GetServerDisplayName(0));
                return result;
            }
            for (var i = 0; i < names.Count; i++)
            {
                result.Add($"{i}: {names[i]}");
            }
            return result;
        }

        private void UpdateSpeechChoices()
        {
            try
            {
                if (this._speechVoiceSetting != null)
                {
                    this._speechVoiceSetting.Values = this.BuildVoiceChoices();
                    this._speechVoiceSetting.UpdateChoices();
                    this._speechVoiceSetting.Value = this.SpeechVoice;
                }
                if (this._speechVersionSetting != null)
                {
                    this._speechVersionSetting.Values = this.BuildVersionChoices();
                    this._speechVersionSetting.UpdateChoices();
                    this._speechVersionSetting.Value = this.SpeechVersion;
                }
                if (this._speechServerSetting != null)
                {
                    this._speechServerSetting.Values = this.BuildServerChoices();
                    this._speechServerSetting.UpdateChoices();
                    this._speechServerSetting.Value = this.SpeechServer;
                }
            }
            catch (Exception e)
            {
                Logger.Warn($"[TTS] Failed to update speech dropdowns: {e.Message}");
            }
        }

        private static int ParseServerIndex(string display)
        {
            if (string.IsNullOrEmpty(display)) return -1;
            var idx = display.IndexOf(':');
            if (idx > 0 && int.TryParse(display.Substring(0, idx), out var value))
            {
                return value;
            }
            return -1;
        }
    }
}
