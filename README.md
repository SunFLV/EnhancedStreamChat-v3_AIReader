# EnhancedStreamChat v3 — AI 弹幕朗读版

基于 [EnhancedStreamChat-v3](https://github.com/baoziii/EnhancedStreamChat-v3) 二次开发，新增 **AI 语音朗读弹幕** 功能。

---

## 新功能：AI 弹幕朗读

使用 GPT-SoVITS 将直播间弹幕实时合成为语音播放。

### 架构

```
Beat Saber (mod) ──HTTPS──▶ A770 服务器 (GPT-SoVITS)
       │                           │
  弹幕消息 → TTS 请求      生成 WAV 音频
       │                           │
  音频播放 ◀────────────── 返回音频
```

### 客户端配置

游戏内设置面板 → **语音** 标签：

| 选项 | 说明 | 默认值 |
|------|------|--------|
| 启用AI弹幕朗读 | 开关 | 关 |
| 朗读模式 | 全部 / 仅高亮 / 仅礼物 | 全部 |
| 朗读声音 | TTS 服务器 voice 选择 | default |
| TTS服务器 | 服务器地址下拉选择 | ai.sunflv.top |
| 朗读音量 | 0~1 | 0.8 |
| 朗读语速 | 0.5~2.0 | 1.0 |
| 最大队列数 | 1~50 | 10 |

### 配置文件

独立配置文件：`UserData\EnhancedStreamChat_Speech.ini`

```ini
[Speech]
SpeechEnabled=true
SpeechServerUrl=https://ai.sunflv.top/api/tts
SpeechServerUrls=ai.sunflv.top,localhost:8000
SpeechVoice=default
SpeechVoices=default,tomori,anon
SpeechFilterMode=0
SpeechVolume=0.8
SpeechSpeed=1.0
SpeechMaxQueueSize=10
```

`SpeechServerUrls` 中逗号分隔多个地址，游戏中下拉框可选择。`SpeechVoices` 同样逗号分隔，服务器会在启动后自动写入该字段。

### TTS 请求接口

客户端通过 HTTPS POST 向服务器发送合成请求：

```json
{
  "speaker": "用户名",
  "text": "弹幕内容",
  "voice": "default",
  "speed": 1.0
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `speaker` | string | 发送者名字（测试/系统消息为空字符串），后端可按此缓存复用 |
| `text` | string | 弹幕正文 |
| `voice` | string | 声音预设名 |
| `speed` | float | 语速 0.5~2.0 |

`speaker` 与 `text` 分离设计，便于后端缓存同一说话人的"名字说"音频，减少重复推理。

### 编译

```bash
dotnet build -c Debug
```

需要 .NET 8 SDK + Beat Saber 1.40.4 已安装。输出在 `bin\Debug\Final\Plugins\`。

### 已知限制

- 动画表情 (GIF/APNG) 在 1.40.x 上暂不可用
- Unity 2022 强制 HTTPS，服务器需配 SSL 证书
- BSML 下拉框不支持动态刷新，voice 列表通过配置文件管理

---

# 增强直播聊天 EnhancedStreamChat v3

增强直播聊天 Enhanced Stream Chat 是节奏光剑 Beat Saber 中一个富文本聊天集成 mod。支持表情，emojis，勋章，和其他聊天信息中包含的受支持第三方图片服务。

# 关于该 MOD

增强直播聊天 Enhanced Stream Chat 对日语与中文进行特殊优化，使得文字大小更加稳定。

造成该问题的原因是字体缓存问题，因此可以在 Unity 中提前创建字体资产来避免这个问题。

如果不在 `UserData/FontAsset` 中放置字体资产就会调用系统字体并可能造成字体大小不一的问题。

# 基础配置 Basic Configuration (for users)

1. 按照[该说明](https://github.com/baoziii/ChatCore-v2#%E5%9F%BA%E6%9C%AC%E8%AE%BE%E7%BD%AE-%E8%8A%82%E5%A5%8F%E5%85%89%E5%89%91mod%E7%94%A8%E6%88%B7)安装聊天核心 Chat Core
2. 在[这里](https://github.com/baoziii/EnhancedStreamChat-v3/releases)下载 EnhancedStreamChat v3
3. 解压压缩包到 Beat Saber 游戏根目录
4. 启动游戏后，ChatCore 设置页会自动在浏览器打开，配置 Twitch/Bilibili 设置

# 如何创建字体资产

1. 在 Unity 新建一个 3D 项目
2. 将字体文件 (`*.ttf`, `*.otf`) 以及 [Chinese7000+JP.txt](https://github.com/baoziii/EnhancedStreamChat-v3/blob/denpadokei-dev/Chinese7000+JP.txt) 拖入 Assets 文件夹
3. 创建 SDF: `Window` → `TextMeshPro` → `Font Asset Creator`
   - Source Font File: 你的字体文件
   - Sampling Point Size: Custom Size, 60
   - Padding: 6
   - Packing Method: Fast
   - Atlas Resolution: 8192 × 8192
   - Character Set: Characters from File → 选择 `Chinese7000+JP.txt`
   - Render Mode: SDFAA
   - 点击 Generate Font Atlas → Save
4. 安装 Asset Bundle Browser (Package Manager)
5. 打开 Asset Bundle Browser → Configure → 拖入生成的 SDF 文件 → Build
6. 将 `AssetBundles/StandaloneWindows/` 中的 `.assets` 文件放到 `Beat Saber\UserData\FontAssets\Main\`
