# 2026-11-20 上游合并与 TTS 接口更新

## 基线

- 上游：`lyyQwQ/EnhancedStreamChat-v3` 的 `main`（提交 `2c831709`，2026-10-02）
- 本地：原 `EnhancedStreamChat-v3_AIReader-master`（ChatCore 架构，1.39.1 分支改造 + TTS）
- 合并方式：**以上游 CatCore 架构为基线整体替换弹幕板**，再把 AI 朗读移植过来

## 已合并的上游改动

`dev/catcore` → `main` 的全部 15 个变更文件，包括：

- `CatCoreWrapper/CatCoreManager.cs`
- `Chat/ChatDisplay.cs` / `ChatDisplay.Settings.cs` / `ChatDisplay.bsml`
- `Chat/ChatImageProvider.cs` / `ChatMessageBuilder.cs` / `ESCFontManager.cs`
- `Graphics/EnhancedFontInfo.cs` / `EnhancedTextMeshProUGUI*.cs`
- `Interfaces/ICatCoreManager.cs`、`Utilities/BeatSaberUtils.cs`
- Bilibili 徽章渲染对齐 CatCore pipeline
- 移除字体反射回退

新增的 CatCore 架构文件：`Configuration/PluginConfig.cs`、`Converters/`、`Models/`、
`Interfaces/IESC*.cs`、`Installers/ESCAppInstaller.cs`、`Libs/CatCore/`。

删除的旧 ChatCore 文件：`Chat/ChatConfig.cs`、`Chat/ChatManager.cs`、
`Utilities/PersistentSingleton.cs` 等。

## 目标版本

- 游戏：Beat Saber **1.40.4**
- `TargetFramework`：`net48`（上游写法）
- `manifest.json`：`gameVersion 1.40.4`，依赖 CatCore `^1.1.0`
- CatCore 1.1.0 + CatCore.Svg 1.0.0 随插件一起安装：
  - `Libs/CatCore.dll`
  - `Libs/CatCore.Svg.dll`（从 `lyyQwQ/CatCore` 源码构建，ILRepack 合并 Svg/ExCSS/Fizzler）
  - `Plugins/CatCore.manifest`

## TTS 移植

新增/重写：

- `Chat/ChatSpeechConfig.cs`：不再依赖 ChatCore 的 INI 转换器；兼容旧配置的引号与 `//` 行内注释；
  自动把 `ai.sunflv.top` 迁移到 `https://6.sunflv.top:8000`
- `Chat/ChatSpeechManager.cs`：基于 `IESCChatMessage`；后台线程只入队，协程在主线程启动
- `ChatDisplay.cs`：在 `OnTextMessageReceived(IESCChatMessage, DateTime)` 入队
- `ChatDisplay.Settings.cs` / `ChatDisplay.bsml`：新增「语音」标签页
- `Installers/ESCAppInstaller.cs`：Zenject 绑定 `ChatSpeechManager`

## 后端 API（AI TTS Proxy v3）

`POST /api/tts?_srv=<下标>`

```json
{
  "speaker": "用户名",
  "text": "弹幕内容",
  "voice": "v4_sunflv",
  "version": "",
  "speed": 1.0
}
```

新增参数：

- `version`：模型版本名；空字符串 = 服务器 `default_version`（当前 `v5turbo`）
- `_srv`：请求哪个后端合成；取值来自 `GET /api/servers` 的 `servers` 数组下标

设置页新增：

- 「模型版本」下拉：来自 `GET /api/voices?_srv=<下标>` 的 `versions`，默认「服务器默认」
- 「合成后端」下拉：来自 `GET /api/servers`
- 音量上限从 1.0 提升到 2.0（`AudioGainFilter` 在音频线程做增益 + 硬限幅）

客户端超时 300s；遇到 503（引擎冷启动/换模型）自动重试一次。

## 构建与部署

```bash
dotnet build -c Debug                 # 编译并复制到游戏目录
dotnet build -c Debug -p:DisableCopyToGame=True   # 只编译
```

部署前备份位于：

- `../game_1404_backup_20261120/`（旧插件 DLL/PDB、两个 INI）
- `../EnhancedStreamChat_AIReader_pre-merge_20261120.tar.gz`（合并前源码）

## 注意事项

1. 弹幕配置从 ChatCore 网页迁移到 **CatCore 网页**，首次启动会自动打开浏览器；B 站登录/频道需要重新配置。
2. 旧的 `UserData/EnhancedStreamChat.ini` 已备份，但 BSIPA 新配置会使用同一路径（键名不同）。
3. 旧 TTS 配置会自动兼容和迁移。
4. 如果 B 站徽章显示异常，检查 `Libs/CatCore.Svg.dll` 是否已部署。
