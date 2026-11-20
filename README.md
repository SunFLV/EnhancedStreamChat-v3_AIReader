# EnhancedStreamChat v3 — AI 弹幕朗读版

基于 [lyyQwQ/EnhancedStreamChat-v3](https://github.com/lyyQwQ/EnhancedStreamChat-v3)（上游 `dev/catcore` / `main`）
二次开发，在 **CatCore 架构** 的弹幕板上接入了 GPT-SoVITS TTS 后端。

- 弹幕板：上游 10.1.0（CatCore 架构，包含上游的 BUG 修复与 Bilibili 徽章渲染改进）
- 朗读：AI 弹幕朗读（GPT-SoVITS / AI TTS Proxy v3）
- 目标游戏版本：Beat Saber **1.40.4**（`net48`）

---

## 架构

```
Beat Saber (mod) ──HTTPS──▶ AI TTS Proxy (6.sunflv.top:8000)
       │                           │
  弹幕消息 → TTS 请求      ├─ /api/tts     单句合成 → audio/wav
       │                   ├─ /api/voices  音色 / 模型版本 / 默认值
  音频播放 ◀───────────────┤─ /api/servers 可用后端列表
                               │
                         GPT-SoVITS 引擎
```

弹幕连接由 **CatCore** 提供。CatCore 1.1.0 的 DLL 与 manifest 已随插件一起构建，
会安装到 `Libs/CatCore.dll` 和 `Plugins/CatCore.manifest`，无需额外下载。

## 游戏内设置

设置面板 → **语音** 标签：

| 选项 | 说明 |
|------|------|
| 启用AI弹幕朗读 | 开关 |
| 朗读模式 | 全部 / 仅高亮 / 仅礼物 |
| 朗读声音 | `/api/voices` 返回的音色列表 |
| 模型版本 | `/api/voices` 返回的版本列表；`服务器默认` 表示不指定 |
| 合成后端 | `/api/servers` 返回的后端列表（下拉选择，请求时带 `_srv=<下标>`） |
| 朗读音量 | 0 ~ 2.0（200%，超过 100% 由客户端增益实现） |
| 朗读语速 | 0.5 ~ 2.0 |
| 最大队列数 | 1 ~ 50 |
| 测试朗读 / 刷新列表 | 立即合成一条测试语音 / 重新拉取后端、音色、版本 |

## 配置文件

`UserData\EnhancedStreamChat_Speech.ini`

```ini
[Speech]
SpeechEnabled=true
SpeechServerUrl=https://6.sunflv.top:8000
SpeechServerUrls=6.sunflv.top:8000
SpeechVoice=v4_sunflv
SpeechVoices=default,v4_default,v4_tomori,v4_anon,v4_taki,v4_rana,v4_soyo,v4_phone,v4_sunflv
SpeechVersion=
SpeechVersions=v5turbo
SpeechServerIndex=0
SpeechServerNames=本机 A770|9070XT (streaming6)|P106-90 fp16 (CUDA)
SpeechFilterMode=0
SpeechVolume=1.00
SpeechSpeed=1.00
SpeechMaxQueueSize=10
```

旧版配置（`//` 行内注释、引号包裹、`ai.sunflv.top` 地址）会自动兼容并迁移到新地址。

## TTS 请求接口

`POST https://6.sunflv.top:8000/api/tts?_srv=<后端下标>`

```json
{
  "speaker": "用户名",
  "text": "弹幕内容",
  "voice": "v4_sunflv",
  "version": "",
  "speed": 1.0
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `speaker` | string | 发送者名字（测试/系统消息为空字符串），后端可按此缓存复用 |
| `text` | string | 弹幕正文，最长 200 字符 |
| `voice` | string | 声音预设名，见 `/api/voices` |
| `version` | string | 模型版本名；空字符串 = 使用服务器 `default_version`（当前 `v5turbo`） |
| `speed` | float | 语速 0.5 ~ 2.0 |

查询参数：

- `_srv=<下标>`：选择 `/api/servers` 中的后端（单句合成取选中的那一台）。
  `/api/servers` 本身始终从配置的服务器地址获取，不带 `_srv`。

响应：`audio/wav`，响应头包含 `x-tts-version`（实际使用的模型版本）。

另外两个辅助接口：

- `GET /api/voices?_srv=<下标>` → `voices` / `versions` / `default_voice` / `default_version`
- `GET /api/servers` → `servers[]`（`name` / `url` / `kind` / `engine` / `detail`）

## 编译

```bash
dotnet build -c Debug
```

需要 .NET 8 SDK。`BeatSaberDir` 默认指向：

```
D:\Program Files (x86)\Steam\steamapps\common\BSManager\BSInstances\1.40.4
```

构建会复制插件与 CatCore 到游戏目录；只想编译不部署时加 `-p:DisableCopyToGame=True`。

## 上游变更

本工程以 `lyyQwQ/EnhancedStreamChat-v3` 的 `main`（2026-10-02）为基线，已合并：

- CatCore 架构迁移与 `CatCoreWrapper`
- Bilibili 徽章渲染对齐 CatCore pipeline
- 字体反射回退移除、图片/消息渲染修复
- 1.40.8 / 1.42.3 / 1.44.1 的多版本构建支持（本工程固定为 1.40.4）

TTS 相关代码为本地新增：`Chat/ChatSpeechManager.cs`、`Chat/ChatSpeechConfig.cs`，
以及 `ChatDisplay` / `ChatDisplay.Settings` / `ChatDisplay.bsml` 中的语音标签页。

---

# Enhanced Stream Chat v3（上游说明）

Enhanced Stream Chat is a rich text chat integration mod for Beat Saber. It supports emotes, emojis, badges,
and any other image that may come inline in a chat message through any of the supported services.

# 依赖

```
BSIPA
BeatSaberMarkupLanguage
BS Utils
CatCore（随插件附带）
SiraUtil
CameraUtils
```
