using ChatCore.Config;
using System.IO;

namespace EnhancedStreamChat.Chat
{
    /// <summary>
    /// Separate config file for AI TTS speech settings.
    /// Stored in: UserData/EnhancedStreamChat_Speech.ini
    /// </summary>
    public class ChatSpeechConfig : StreamCoreConfigConverter<ChatSpeechConfig>
    {
        public static ChatSpeechConfig instance { get; private set; } = new ChatSpeechConfig(
            Path.Combine(System.Environment.CurrentDirectory, "UserData"),
            "EnhancedStreamChat_Speech");

        [ConfigSection("Speech")]
        [ConfigMeta(Comment = "Enable AI TTS danmaku reading")]
        public bool SpeechEnabled = false;

        [ConfigMeta(Comment = "TTS server URL")]
        public string SpeechServerUrl = "https://ai.sunflv.top/api/tts";

        [ConfigMeta(Comment = "Available TTS server URLs (comma separated)")]
        public string SpeechServerUrls = "ai.sunflv.top,localhost:8000";

        [ConfigMeta(Comment = "Voice preset name on the TTS server")]
        public string SpeechVoice = "default";

        [ConfigMeta(Comment = "Available voice names (comma separated, from server or manual)")]
        public string SpeechVoices = "default";

        [ConfigMeta(Comment = "Speech filter: 0=All, 1=Highlighted/Mentioned, 2=Gifts/SuperChats only")]
        public int SpeechFilterMode = 0;

        [ConfigMeta(Comment = "Speech volume (0.0 to 1.0)")]
        public float SpeechVolume = 0.8f;

        [ConfigMeta(Comment = "Speech speed (0.5 to 2.0)")]
        public float SpeechSpeed = 1.0f;

        [ConfigMeta(Comment = "Max TTS speech queue size")]
        public int SpeechMaxQueueSize = 10;

        private ChatSpeechConfig(string configDirectory, string configName)
            : base(configDirectory, configName, "", false)
        {
            Logger.Info("Speech config initialized.");
        }
    }
}
