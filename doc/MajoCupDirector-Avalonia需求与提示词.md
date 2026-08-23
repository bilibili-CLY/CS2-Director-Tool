# MajoCupDirector — .NET 10 + Avalonia UI 重构：需求文档与开发提示词

> 本文档基于对原仓库 `LaffeyNyaa/MajoCupDirector`（WPF / .NET Framework 4.7.2）源码与
> `doc/MajoCupDirector研究报告.md`、`Docs/INTRODUCTION.md` 的研究整理而成。
> 目标：用 **.NET 10 + Avalonia UI** 重写该项目，**1:1 复刻**现有四大功能，
> 目标平台 **Windows + macOS**，玩家改名 API **抽象为可配置**。

---

## 1. 项目概述

MajoCupDirector 是 CS2「魔女杯」赛事 Demo / 观战转播用的桌面导播工具。它本身不注入或修改 CS2，
而是把以下组件串联起来：

```
CS2 Game State Integration（GSI, HTTP JSON ~100ms 推送）
        │
        ▼
MajoCupDirector 本地状态判断（击杀 / 回合 / 暂停 / 玩家）
        ├── OBS WebSocket：录制、切换场景、控制媒体源
        ├── FFmpeg：截取并拼接击杀录像
        └── 赛事网站 API（可配置）：SteamID → 登记选手名 → HLAE 改名命令
```

四大功能（1:1 复刻范围）：
1. **主页配置**：配置 CS2 / FFmpeg / OBS WebSocket，自动安装 GSI 配置。
2. **局内击杀回放**：录制整回合，根据 GSI 推断击杀，回合结束后用 FFmpeg 生成击杀集锦并通过 OBS 播放。
3. **暂停音乐**：游戏暂停时播放指定 OBS 音乐源，恢复时 2 秒淡出并停止。
4. **玩家改名**：获取当前玩家 Steam64 ID，查询登记名，生成 HLAE `mirv_replace_name` 命令（API 可配置）。

---

## 2. 原项目模块分析

原方案分为两个工程：`MajoCupDirector`（主程序、页面、ViewModel、业务服务）与
`MajoCupDirector.UI`（主题、样式、转换器、转场控件）。核心模块如下：

| 模块 / 文件 | 职责 | 依赖 | Avalonia 迁移要点 |
|---|---|---|---|
| `App.xaml(.cs)` / `MainWindow.xaml(.cs)` | 应用入口、侧边栏 + 内容区导航、手动构造各页面与 ViewModel | — | 改为 Avalonia `App.axaml` / `MainWindow.axaml`；用 `Microsoft.Extensions.DependencyInjection` 解析 ViewModel |
| `ViewModels/MainViewModel.cs` | 侧边栏标签页集合、选中态、按前置条件启用/禁用标签页 | ISettingsService, IObsService, ICs2InstallService | 改为 CT.Mvvm；逻辑基本不变 |
| `ViewModels/HomeViewModel.cs` | CS2/FFmpeg/OBS 配置、GSI 安装、OBS 连接；触发前置条件回调 | 多个 Service + ObsWebSocketUrlBuilder | `System.Windows.Forms.OpenFileDialog` → Avalonia `OpenFileDialog`；密码框 `PasswordBox` → Avalonia `TextBox(PasswordChar)` |
| `ViewModels/KillReplayViewModel.cs` | 击杀回放全流程状态机（录制/击杀记录/剪辑/播放协调） | IGsiService, IObsService, IFfmpegService, ICs2InstallService, ISettingsService | 保留全部状态机逻辑；`Application.Current.Dispatcher` → `Dispatcher.UIThread` |
| `ViewModels/PauseMusicViewModel.cs` | 暂停音乐播放/淡出，与回放场景协调 | IGsiService, IObsService | 同上 |
| `ViewModels/PlayerRenameViewModel.cs` | 取命令、查 API、生成 HLAE 命令、复制到剪贴板 | IGsiService, IPlayerApiService | `Clipboard.SetText` → `Application.Clipboard.SetTextAsync` |
| `ViewModels/BaseViewModel.cs` / `RelayCommand.cs` | INotifyPropertyChanged 基类 / 命令 | — | 直接替换为 CommunityToolkit.Mvvm（`ObservableObject` + `[ObservableProperty]` / `[RelayCommand]`） |
| `Services/GsiService.cs` | HTTP 监听 `localhost:3000`，解析 GSI，产出比赛/回合/击杀/暂停事件 | HttpListener, GsiPayloadParser | `HttpListener` 在 .NET 8 跨平台可用；`DateTime.Now` 墙钟时间逻辑保持 1:1 |
| `Services/GsiPayloadParser.cs` | 三级回退 JSON 解析（完整 / 首个对象 / 恢复 map+round） | Newtonsoft.Json | 保留，继续用 Newtonsoft.Json |
| `Services/ObsService.cs` | obs-websocket-dotnet 封装：录制/场景/媒体源/音量/播放结束等待 | obs-websocket-dotnet 5.0.0.3 | 库跨平台可用，逻辑 1:1 保留 |
| `Services/FfmpegService.cs` | 帧精确截取片段并 concat 拼接；进度/超时/取消 | Process, ffmpeg | `Process` 跨平台可用；保留全部 ffmpeg 参数与取消逻辑 |
| `Services/Cs2InstallService.cs` + `ObsWebSocketUrlBuilder.cs` | 解析 cs2 cfg 目录、安装 GSI 配置；构造 ws URL | — | 纯逻辑，1:1 保留 |
| `Services/MajoCupPlayerApiService.cs` | 按 Steam64 ID 查询登记名（原硬编码 majo-cup API） | HttpClient | **改为可配置 API BaseUrl**（默认沿用 majo-cup.laffeynyaa.com） |
| `Services/SettingsService.cs` + `ISettingsService.cs` | 设置持久化为 JSON | — | 路径用 `Environment.SpecialFolder.ApplicationData`（跨平台）；**密码仍明文保存（1:1，不改 DPAPI）** |
| `Resources/gsi_cfg.cfg` | 嵌入的 GSI 配置文件 | — | 作为 Avalonia 资源或嵌入资源 1:1 保留 |
| `MajoCupDirector.UI/*` | 主题（彼岸花/Lycoris 暗色）、按钮/文本框/复选框样式、Bool→Visibility 等转换器、`TransitionContentControl` 转场 | — | 用 Avalonia `Styles` / `ControlTheme` 重建；转场用 Avalonia 的 `TransitioningContentControl` 或自定义 250ms 交叉淡入淡出 |

### 2.1 关键行为规格（来自 `Docs/INTRODUCTION.md` 与研究报告，1:1 必须复刻）

- **GSI 监听**：`http://localhost:3000/`，buffer/throttle=0.1，heartbeat=60。
- **击杀判定（严格）**：生命值 `>0→<=0` 判死亡；比较 `match_stats.kills` 差分识别击杀者；
  同一推送多人击杀数增加→歧义→不记录；仅当「死亡前上一包观战目标 == 击杀者」才记录；
  击杀者/观战目标延迟到达时最多暂存 **1.5 秒** 再决定丢弃。
- **录制约束**：仅录制已启动且处于录制会话内的击杀才记录；回放播放期间/录制未就绪的击杀不记录。
- **剪辑规则**：首个击杀片段 = 击杀前 4s 到击杀后 1s（共 5s）；其余 = 击杀前 1s 到击杀后 1s（共 2s）；
  时间重合片段合并为连续区间；帧精确重编码（`-ss` 在 `-i` 前）；concat demuxer 拼接。
- **时间基准**：墙钟时间（`KillDetectedAt - RecordingStartTime`），Demo 变速/跳变不特殊处理（1:1 保留局限）。
- **暂停音乐**：`phase_countdowns.phase` ∈ {paused, timeout, timeout_ct, timeout_t} → 暂停；
  触发 `RESTART` 从头播放（无淡入）；恢复时 2 秒内线性淡出至 -100dB 后 STOP，并恢复原始音量；
  与回放播放协调（挂起/取消挂起）。
- **改名命令**：`mirv_replace_name byXuid add "<Steam64ID>" "<登记名>"`，分号连接，复制剪贴板。
- **前置条件**：已安装 GSI + 已设 FFmpeg + 已连 OBS。不满足时勾选保留但不真正启用，恢复后自动恢复。

---

## 3. 跨平台与架构适配要点（通用约束）

1. **禁止 Windows 专属 API**：不使用 `System.Windows.*`、`System.Windows.Forms`、`Microsoft.Win32`、
   DPAPI。所有 UI 用 Avalonia 控件。
2. **文件对话框**：用 `Avalonia.Controls.OpenFileDialog`（`await dialog.ShowAsync(topLevel)`），
   过滤 `cs2.exe` / `ffmpeg.exe`（macOS 上对应可执行文件按名称过滤，不强求 `.exe`）。
3. **剪贴板**：`var clipboard = Application.Current?.Clipboard; await clipboard.SetTextAsync(text);`
4. **线程/调度**：后台线程更新日志用 `Dispatcher.UIThread.Post(() => { ... })` 回到 UI 线程。
5. **GSI 解析**：继续用 `Newtonsoft.Json` 13.0.4（JObject 三级回退逻辑直接迁移）。
6. **OBS 控制**：保留 `obs-websocket-dotnet` 5.0.0.3（.NET Standard，跨平台可用）。
7. **FFmpeg**：保留外部路径方式（设置中配置 ffmpeg 可执行文件路径），不内置打包。
8. **设置持久化**：`Path.Combine(Environment.GetFolderPath(ApplicationData), "MajoCupDirector", "settings.json")`；
   密码明文（1:1 不改）。新增 `PlayerApiBaseUrl` 设置项。
9. **GSI 配置资源**：`gsi_cfg.cfg` 作为 Avalonia 嵌入资源或 `Assets` 资源，安装时读取内容写入 cs2 cfg 目录。
10. **主题**：重建「彼岸花（Lycoris）」暗色主题——深色背景 + 红色系强调色（约 `#E60012` / `#C1272D` 区间），
    按钮/复选框具备默认、悬停、按下、禁用四态且带过渡；标签页切换内容区 250ms 交叉淡入淡出。
11. **HttpListener**：.NET 8 在 Windows/macOS 均可用，绑定 `http://localhost:3000/`（必要时在 macOS 处理
    `http.sys`/权限问题；localhost 通常无需额外权限）。

---

## 4. 建议的新工程结构（镜像原双工程）

```
CS2-Director-Tool.sln
├── CS2-Director-Tool/                (Avalonia 可执行工程, .NET 8)
│   ├── App.axaml / App.axaml.cs    (DI 容器构建, 资源合并)
│   ├── MainWindow.axaml(.cs)       (侧边栏 + 内容区 + 导航)
│   ├── Views/                      (Home/KillReplay/PauseMusic/PlayerRename Page)
│   ├── ViewModels/                 (Main/Home/KillReplay/PauseMusic/PlayerRename)
│   ├── Services/                   (Gsi/Obs/Ffmpeg/Cs2Install/PlayerApi/Settings + 接口)
│   ├── Models/                     (TabItem, GsiPlayerInfo, ClipSegment, 事件参数…)
│   └── Resources/gsi_cfg.cfg
└── CS2-Director-Tool.UI/             (Avalonia 类库, 主题/转换器/转场控件)
    ├── Themes/                     (Lycoris 暗色主题 Styles)
    ├── Converters/                 (BoolToVisible 等)
    └── Controls/                   (TransitioningContentControl)
```

---

## 5. 功能需求清单（验收口径）

- **FR-1 主页**：CS2 路径 + 浏览；FFmpeg 路径 + 浏览；OBS 地址/端口/密码 + 连接/断开；安装/重装 GSI；
  显示 GSI 安装状态与 OBS 连接状态；设置持久化（含密码）。
- **FR-2 击杀回放**：启用勾选（持久化）；游戏/回放场景名、回放媒体源名配置；前置条件门控；
  回合录制→击杀记录→回合结束剪辑→回放播放→切回游戏的完整状态机；诊断日志控制台。
- **FR-3 暂停音乐**：启用勾选 + 音乐源名（持久化）；暂停触发 RESTART 播放、恢复触发 2s 淡出；
  与回放场景协调。
- **FR-4 玩家改名**：获取命令按钮；从 GSI 取玩家→按**可配置 API BaseUrl** 查登记名→生成 HLAE 命令→
  复制到剪贴板；汇总成功/未登记/失败。
- **FR-5 全局**：GSI 服务随应用启动/退出统一启停；侧边栏按前置条件启用/禁用标签页；
  缺失标签页时强制回主页；彼岸花暗色主题 + 250ms 转场。

---

## 6. 开发提示词（Prompts）

> 以下提示词可直接用于驱动 AI 编码代理按模块实现。所有提示词共享下方「通用约束」。

### 通用约束（粘贴到每个提示词前/后）

```
技术栈：C# 12 / .NET 10 / Avalonia UI 11.x / CommunityToolkit.Mvvm / Microsoft.Extensions.DependencyInjection。
架构：MVVM；属性用 [ObservableProperty]，命令用 [RelayCommand]；服务以接口+实现形式存在，通过 DI 解析。
严禁：WPF / System.Windows.* / System.Windows.Forms / Windows 专属 API（如 DPAPI）/ 硬编码平台路径。
JSON：GSI 解析继续使用 Newtonsoft.Json 13.0.4（JObject）。
行为：严格 1:1 复刻原项目对应模块的功能与边界（见对应规格），不要新增/删减功能，不要“改进”时序与算法。
跨平台：必须能在 Windows 与 macOS 编译运行；文件对话框用 Avalonia OpenFileDialog，剪贴板用
Application.Clipboard.SetTextAsync，UI 线程调度用 Dispatcher.UIThread。
```

---

### 提示词 P0 — 解决方案脚手架、DI 与主题基础设施

```
目标：搭建 MajoCupDirector 的 .NET 8 + Avalonia 解决方案骨架。

要求：
1. 创建 solution + 两个工程：MajoCupDirector（Avalonia 可执行）与 MajoCupDirector.UI（Avalonia 类库，放主题/转换器/控件）。
2. 引用 NuGet：Avalonia 11.x、Avalonia.Desktop、Avalonia.Themes.Fluent（作为基础再覆盖自定义样式）、
   CommunityToolkit.Mvvm、Microsoft.Extensions.DependencyInjection、Newtonsoft.Json 13.0.4、obs-websocket-dotnet 5.0.0.3。
3. 在 MajoCupDirector.UI 中：
   - 定义「彼岸花（Lycoris）」暗色主题：深色背景（约 #1E1E24 / #16161A），强调红色（约 #E60012），
     文本次级灰；提供 PageTitle/Subtitle/SectionTitle/Label/Card/PrimaryButton/DefaultButton/ToggleCheckBox/
     DarkTextBox/DarkPasswordBox 等 ControlTheme；按钮与复选框具备 default/hover/pressed/disabled 四态且带过渡。
   - 实现 BoolToVisibilityConverter、InverseBoolConverter 等转换器（Avalonia 的 IValueConverter）。
   - 实现 TransitioningContentControl：内容切换时 250ms 交叉淡入淡出（CrossFade）。
4. 在 MajoCupDirector 的 App.axaml.cs 中：使用 Microsoft.Extensions.DependencyInjection 构建
   ServiceProvider，注册所有 Service 接口与实现（单例），注册 MainWindow 及其ViewModel；
   在 App.axaml 合并 MajoCupDirector.UI 的主题资源字典。
5. 实现 MainWindow.axaml(.cs)：左侧边栏（标签页列表，数据绑定 MainViewModel.Tabs）+ 右侧
   TransitioningContentControl 内容区；启动时构造各 Page 与对应 ViewModel 并注入到 MainViewModel。
6. 将 gsi_cfg.cfg 作为 MajoCupDirector 的嵌入资源保留。

交付：可编译运行的空壳程序，侧边栏可切换 4 个空页面（主页/击杀回放/暂停音乐/玩家改名），带彼岸花主题与转场。
```

---

### 提示词 P1 — 设置服务与 CS2/GSI 安装模块

```
目标：实现 ISettingsService（JSON 持久化，含新增 PlayerApiBaseUrl）与 ICs2InstallService + ObsWebSocketUrlBuilder。

要求（1:1 复刻原 SettingsService / Cs2InstallService / ObsWebSocketUrlBuilder 行为）：
1. ISettingsService 属性：Cs2Path, FfmpegPath, ObsWebSocketAddress, ObsWebSocketPort, ObsWebSocketPassword,
   GameSceneName, ReplaySceneName, ReplaySourceName(默认"Replay"), PauseMusicEnabled, PauseMusicSourceName,
   KillReplayEnabled, 以及【新增】PlayerApiBaseUrl（默认 "https://majo-cup.laffeynyaa.com"）。
   每次 setter 即持久化（Load/Save）。
2. 持久化路径：Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
   "MajoCupDirector", "settings.json")；读写失败静默回退默认值（1:1 不改）。
   密码仍以明文保存（1:1，不使用 DPAPI）。
3. ICs2InstallService：
   - FindCfgDirectory(cs2ExecutablePath)：依次尝试 exeDir/csgo/cfg、exeDir/game/csgo/cfg、exeDir/../../csgo/cfg，
     存在即返回，否则 null。
   - InstallGsiConfig / IsGsiConfigInstalled：写入/检查 gamestate_integration_majo_cup.cfg。
   - 常量 GsiConfigFileName = "gamestate_integration_majo_cup.cfg"。
4. ObsWebSocketUrlBuilder.Build(host, port)：构造 ws://host:port，对裸 IPv6 加方括号；空值或非法抛 ArgumentException。
5. 全部通过 DI 注册为单例，构造函数注入 ISettingsService。

交付：可被其他模块注入使用的设置与 CS2/GSI 安装服务，行为与原版一致，跨平台可编译。
```

---

### 提示词 P2 — GSI 监听与解析模块

```
目标：实现 IGsiService（HTTP 监听 localhost:3000）+ GsiPayloadParser（三级回退解析）。

要求（1:1 复刻 GsiService.cs / GsiPayloadParser.cs）：
1. GsiService：
   - 使用 HttpListener 监听 http://localhost:3000/；Start/Stop/Dispose；IsRunning。
   - 收到请求立即回 200 OK，再异步处理 body。
   - 调用 GsiPayloadParser.Parse(body)：
       * IsFullyParsed → ProcessPlayerDeaths / ProcessPlayerNames / ProcessMapPhase / ProcessRoundPhase / ProcessPausePhase；
       * IsRecovered → 仅 ProcessMapPhase / ProcessRoundPhase / ProcessPausePhase；
       * 否则按 5 秒内相同错误特征抑制重复日志。
   - 事件（EventHandler）：OnKill(GsiKillEventArgs), OnRoundStarted, OnMatchStarted, OnRoundEnded,
     OnGamePaused, OnGameResumed, OnLog(string)。
   - ProcessMapPhase：map.phase 切到 "live" → OnMatchStarted；map.round 递增 → OnRoundEnded（去重）。
   - ProcessRoundPhase：首包按 freezetime/live 触发 OnRoundStarted；phase 切到 freezetime/live → OnRoundStarted；
     over → OnRoundEnded。
   - ProcessPausePhase：phase_countdowns.phase ∈ {paused, timeout, timeout_ct, timeout_t} → OnGamePaused/OnGameResumed。
   - ProcessPlayerDeaths（严格击杀判定，1:1）：
       * 维护 per-player 上一份 health、kills（Dictionary<string,int>）与 playerNames 快照。
       * 死亡：health >0 → <=0；FindKiller 比较 match_stats.kills 差分，恰好 1 人增加才算击杀者，否则歧义；
       * IsTargetOnKiller：用「死亡前上一包观战目标」(GetSpectatorTarget: player.spectarget，回退 observer.steamid/target)
         与击杀者匹配（SteamID 或名称）。
       * 暂存 PendingKill 最多 1.5 秒（KillcamMatchWindow），期间击杀者/观战目标到达则确认，否则过期；
         过期仍计算并触发 OnKill（IsObserverOnKiller=false）。
       * GsiKillEventArgs：KillerName, VictimName, IsObserverOnKiller, ObserverTargetName, KillDetectedAt(DateTime.Now)。
   - GetCurrentPlayers()：返回 Steam64 ID（17 位、以 7656119 开头、long 可解析）与名称快照。
   - 日志：LogPayloadSummary 仅在 map/round/activity 变化时报；异常/解析失败按规则抑制。
2. GsiPayloadParser：直接 JObject.Parse；失败则提取第一个平衡 JSON 对象；再失败则独立恢复 map/round 对象；
   提供 ErrorMessage / ContextSnippet（含字符串内花括号与转义处理）。
3. 继续用 Newtonsoft.Json（JObject/JToken）。
4. 跨平台：HttpListener 在 .NET 8 可用；监听地址固定 localhost:3000。

交付：IGsiService + GsiPayloadParser，行为与原版逐项一致；提供 GsiPlayerInfo / GsiKillEventArgs 模型。
```

---

### 提示词 P3 — OBS WebSocket 控制模块

```
目标：实现 IObsService（基于 obs-websocket-dotnet 5.0.0.3 的封装）。

要求（1:1 复刻 ObsService.cs 的所有方法、重试、等待与自动创建逻辑）：
1. ConnectAsync(address, password) / Disconnect() / IsConnected；事件 OnConnected, OnDisconnected, OnLog。
2. StartRecordingAsync：最多重试 3 次（间隔 500ms）调用 _obs.StartRecord()。
3. StopRecordingAsync：StopRecord() → 轮询 IsRecording（最多 5s）→ 取录制目录拼接文件名 → 等待文件落盘（最多 5s）。
4. SwitchToSceneAsync(sceneName)：场景不存在则 CreateScene 后切换；容忍 ErrorCode 600（前端列表滞后）重建。
5. CreateReplaySourceAsync(sceneName, sourceName, filePath)：场景不存在则创建并 WaitForSceneToExist；
   媒体源设置 local_file/is_local_file=true/restart_on_activate=false；已存在则 SetInputSettings + 触发
   OBS_WEBSOCKET_MEDIA_INPUT_ACTION_RESTART，否则 CreateInput(ffmpeg_source)；WaitForInputToExist。
6. PlayMediaAsync / PlayMediaSourceAsync(RESTART) / StopMediaSourceAsync(STOP) / InputExistsAsync /
   GetInputVolumeDbAsync / SetInputVolumeDbAsync(volumeDb, true) / GetMediaStatusAsync。
7. WaitForMediaPlaybackEndedAsync(sourceName, timeout?, ct)：先等待 minimumPlayTime（与预期时长相关的 0.5~1.5s）
   避免旧文件 ended 事件误触发；再注册 per-source TaskCompletionSource，等待 MediaInputPlaybackEnded 事件或
   超时（默认 = 媒体时长 + 30s，无时长则 30s）；超时仍视为成功（不无限挂起）。
8. 事件路由：OnMediaInputPlaybackEnded → 按 sourceName 完成对应 signal。
9. Dispose：解订阅事件，已连接则 Disconnect。

交付：IObsService + ObsService，obs-websocket-dotnet 调用方式与原版一致，跨平台可用。
```

---

### 提示词 P4 — FFmpeg 剪辑模块

```
目标：实现 IFfmpegService（帧精确截取 + concat 拼接）。

要求（1:1 复刻 FfmpegService.cs）：
1. ValidatePath(ffmpegPath)：运行 `ffmpeg -version`，ExitCode==0 有效。
2. ClipAndConcatAsync(inputFile, outputFile, clips(StartTime/Duration 秒), ffmpegPath, ct)：
   - 参数校验（非空、文件存在）；临时目录 Path.Combine(Path.GetTempPath(), "MajoCupDirector", "FfmpegClips", Guid)。
   - 逐片段：`-ss <start> -i "<input>" -t <duration> -c:v libx264 -preset veryfast -c:a aac -y "<clip.ts>"`
     （-ss 在 -i 前，帧精确）；用 OnClippingProgress 上报整体进度（基于各片段时长加权）。
   - 写 concat_list.txt（`file '...'`），执行 `-f concat -safe 0 -i "<list>" -c copy -y "<output>"`。
   - 用 ffprobe 式探测实际时长（-i 输出 Duration 正则），触发 OnClippingComplete(outputFile, actualDuration)。
   - 无论成功失败 finally 清理临时片段与目录。
   - ct 取消时 Kill 进程并抛 OperationCanceledException。
3. RunFfmpegProcessAsync：Process 启动，重定向 stderr/stdout(UTF8)，解析 `time=HH:MM:SS.MS` 进度；
   WaitHandle 等待退出或取消或超时（Extract 300s / Concat 600s），超时 Kill 并抛 TimeoutException；
   ExitCode!=0 抛 InvalidOperationException（含 stderr）。
4. 事件：OnClippingProgress(double 0~100), OnClippingComplete(ClippingCompletedEventArgs)。
5. 跨平台：Process 用 ffmpegPath 直接启动（macOS 上为 ffmpeg 可执行文件，无需 .exe 后缀）。

交付：IFfmpegService + FfmpegService，ffmpeg 参数、进度、取消、清理与原版一致。
```

---

### 提示词 P5 — 玩家改名 API 模块（可配置）

```
目标：实现 IPlayerApiService（按可配置 BaseUrl 查询登记名）。

要求（1:1 复刻 MajoCupPlayerApiService 行为，仅将 API 地址改为可配置）：
1. 构造函数接收 baseUrl（来自 ISettingsService.PlayerApiBaseUrl，默认 "https://majo-cup.laffeynyaa.com"）。
2. GetRegisteredNameAsync(steamId)：GET {baseUrl}/api/v1/players/steam/{steamId}；
   - 404 → 返回 null（未登记）；
   - 其他非成功 → 抛异常（由调用方计入失败）；
   - 成功 → 解析顶层 "name" 字段，trim 后为空返回 null，否则返回。
   - 单次超时 10s（HttpClient.Timeout）。
3. 串行查询（1:1 不改并发）。实现 IDisposable 释放 HttpClient。
4. 通过 DI 注入 ISettingsService 以取得 BaseUrl。

交付：IPlayerApiService + MajoCupPlayerApiService，默认行为与原版一致，BaseUrl 可经由设置更改。
```

---

### 提示词 P6 — 主页 ViewModel 与 View（配置/GSI 安装/OBS 连接）

```
目标：实现 HomeViewModel（CT.Mvvm）与 HomePage.axaml，复刻主页配置交互。

要求（1:1 复刻 HomeViewModel.cs + HomePage.xaml）：
1. 属性（[ObservableProperty]）：Cs2Path, FfmpegPath, ObsAddress, ObsPort, ObsPassword,
   GsiStatus, ObsStatus, IsObsConnected, IsGsiInstalled；派生 ConnectButtonText / GsiInstallButtonText。
2. 命令（[RelayCommand]）：BrowseCs2, BrowseFfmpeg, InstallGsi, ConnectObs, ToggleObs。
   - Browse：Avalonia OpenFileDialog（CS2 过滤可执行文件；FFmpeg 同理；macOS 不强求 .exe）。
   - InstallGsi：读取嵌入资源 gsi_cfg.cfg 文本 → ICs2InstallService.InstallGsiConfig(Cs2Path, content) → 刷新状态。
   - ConnectObs：ObsWebSocketUrlBuilder.Build(ObsAddress, ObsPort) → IObsService.ConnectAsync(url, ObsPassword)；
     失败更新 ObsStatus。ToggleObs：已连则 Disconnect 否则 Connect。
3. 加载时从 ISettingsService 回填各字段；Cs2Path / FfmpegPath / OBS 字段变更即时写回设置。
4. 维护 IsGsiInstalled / IsObsConnected，并在变化时调用注入的 Action _onPrerequisitesChanged（用于 MainViewModel 刷新标签页启用态）。
5. 订阅 IObsService.OnConnected/OnDisconnected 更新 IsObsConnected / ObsStatus。
6. View：左侧卡片式布局（CS2 路径+浏览+安装按钮；FFmpeg 路径+浏览；OBS 地址/端口/密码+连接/断开），
   使用 MajoCupDirector.UI 的彼岸花主题与 Card/Button/TextBox/PasswordBox 样式。
   密码框用 Avalonia TextBox(PasswordChar='*')，LostFocus 时写回 ObsPassword。

交付：主页可完成 CS2/FFmpeg/OBS 配置、GSI 安装、OBS 连接，并正确触发前置条件回调。
```

---

### 提示词 P7 — 击杀回放 ViewModel 与 View（核心状态机）

```
目标：实现 KillReplayViewModel（CT.Mvvm）与 KillReplayPage.axaml，1:1 复刻击杀回放全流程。

要求（严格复刻 KillReplayViewModel.cs 的状态机与日志逻辑）：
1. 依赖注入：ISettingsService, IGsiService, IObsService, IFfmpegService, ICs2InstallService。
2. 属性：GameSceneName, ReplaySceneName, ReplaySourceName, Status, IsRecording, IsProcessing,
   IsObsConnected, FfmpegValid, Progress, IsKillReplayEnabled；IsReplayPlaying（内部）。
   日志：LogText（上限 1000 行环形缓冲），ClearLogCommand。
3. 前置条件：CheckPrerequisitesMet = GSI 已安装 + Ffmpeg 已设 + OBS 已连；变化时若不满足则 StopKillReplayAsync
   （勾选状态保留）。IsEffectivelyEnabled = IsKillReplayEnabled && canBeActive。
4. 订阅 GSI 事件：OnKill / OnRoundStarted / OnMatchStarted / OnRoundEnded / OnLog；
   OBS：OnConnected/OnDisconnected/OnLog；FFmpeg：OnClippingProgress/OnClippingComplete。
5. 录制/击杀记录（墙钟时间，1:1）：
   - OnRoundStarted（freezetime/live）：若 _isReplayPlaying 或 _isRoundEnding 则推迟切换/录制；
     否则切到游戏场景→RestartRecording→记录 _recordingStartTime/ _recordingActive=true。
   - OnKill：仅 IsEffectivelyEnabled && _isInRound && e.IsObserverOnKiller && 录制会话内；
     记录 killTimeInRecording = KillDetectedAt - _recordingStartTime，加入 _killTimestamps。
   - OnRoundEnded：快照时间戳→若空则结束；等最后击杀后不足 1s 则补足；StopRecording→等待落盘；
     构建片段（首个击杀前4s后1s=5s，其余前1s后1s=2s）→MergeClips（重合合并）→
     ClipAndConcatAsync→OnClippingComplete。
   - OnClippingComplete：停止可能已开始的录制→切回放场景→CreateReplaySourceAsync→PlayMediaAsync→
     WaitForMediaPlaybackEndedAsync(时长+1s)→切回游戏场景→EnsureRecordingAsync 并重置 _recordingStartTime。
6. 停用（IsKillReplayEnabled=false）：StopKillReplayAsync 停止录制、清空时间戳、重置状态。
7. 所有日志走 AppendLog（带时间戳，Dispatcher.UIThread 回到 UI 线程更新 LogText）。
8. View：启用勾选框 + 状态/进度条（IsProcessing 时显示）；游戏/回放场景名、回放媒体源名输入框；
   日志控制台（只读 TextBox，清空按钮），使用彼岸花主题。

交付：击杀回放完整闭环（录制→击杀记录→剪辑→回放→切回），行为与原版逐项一致。
```

---

### 提示词 P8 — 暂停音乐 ViewModel 与 View

```
目标：实现 PauseMusicViewModel（CT.Mvvm）与 PauseMusicPage.axaml，1:1 复刻暂停音乐。

要求（复刻 PauseMusicViewModel.cs）：
1. 依赖：ISettingsService, IGsiService, IObsService，以及 Func<bool> isReplayPlayingProvider（由主页/协调层注入，
   用于判断回放是否正在播放）。
2. 常量：SilenceDb=-100f，FadeOutDuration=2s，FadeStepMs=50。
3. 属性：IsEnabled, MusicSourceName, Status, IsMusicActive（内部），LogText/ClearLogCommand。
4. OnGamePausedAsync：未启用或已激活则返；若正在回放 → 设 _pendingPlayOnReplayEnd=true 并挂起；
   音乐源名为空/不存在则跳过；否则保存 _originalVolumeDb，RESTART 播放（无淡入），1s 后记录媒体状态诊断。
5. OnGameResumedAsync：_pendingPlayOnReplayEnd=false；若激活则 FadeVolumeAsync 2s 线性淡出至 -100dB →
   STOP → 恢复 _originalVolumeDb。FadeVolumeAsync 每 50ms 调 SetInputVolumeDbAsync。
6. StopMusicAsync（停用）/ OnReplayPlaybackEnded（回放结束恢复挂起播放）/ OnObsDisconnected（复位）。
7. 用 SemaphoreSlim(1,1) + CancellationTokenSource 保证操作互斥与可取消；OperationCanceled 时 TryRestoreVolumeAsync。
8. View：启用勾选框 + 音乐源名输入框 + 状态 + 日志控制台（清空按钮），彼岸花主题。

交付：暂停音乐播放/淡出及其与回放场景的协调，1:1 复刻。
```

---

### 提示词 P9 — 玩家改名 ViewModel 与 View

```
目标：实现 PlayerRenameViewModel（CT.Mvvm）与 PlayerRenamePage.axaml，1:1 复刻改名命令生成。

要求（复刻 PlayerRenameViewModel.cs，使用可配置 IPlayerApiService）：
1. 依赖：IGsiService, IPlayerApiService。
2. 属性：Status, IsBusy；命令 GetCommandsCommand([RelayCommand], CanExecute=!IsBusy)。
3. GetCommandsAsync：
   - IGsiService.GetCurrentPlayers()；为空 → 提示确认 CS2 运行且 GSI 生效。
   - 逐个（串行）调用 IPlayerApiService.GetRegisteredNameAsync(steamId)：
       成功 → 生成 `mirv_replace_name byXuid add "<steamId>" "<EscapeName(name)>"`；
       未登记(null) → notFoundCount++；异常 → failures 列表。
   - 成功数>0 → Application.Clipboard.SetTextAsync(分号连接 commands)；
     状态提示「已成功获取并复制到剪贴板，请使用 HLAE 运行游戏，提交到游戏内控制台」+ 成功/未登记/失败汇总。
   - 全部失败 → 提示未获取到任何已登记玩家名及原因。
4. EscapeName：反斜杠→\\，双引号→\"。
5. View：单个「获取命令」按钮 + 状态文本，彼岸花主题。

交付：改名命令生成与剪贴板复制，1:1 复刻；API 地址经由设置可配置。
```

---

### 提示词 P10 — MainViewModel 与导航/前置条件联动

```
目标：实现 MainViewModel（CT.Mvvm）与 MainWindow 导航，1:1 复刻侧边栏标签页启用/禁用逻辑。

要求（复刻 MainViewModel.cs + 原 App.xaml.cs 的页面装配）：
1. MainViewModel 依赖：ISettingsService, IObsService, ICs2InstallService。
2. Tabs（ObservableCollection<TabItem>）：home / killReplay / pauseMusic / playerRename，
   每项有 Id, Title, IsEnabled, IsSelected。
3. SelectTabCommand；SelectedTab 变更时同步各 Tab.IsSelected。
4. CheckPrerequisites()：gsiInstalled = Cs2Path 已设且 GsiConfig 已安装；ffmpegSet；obsConnected=_isObsConnected。
   据此设置各标签 IsEnabled（home 恒 true；killReplay/pauseMusic 需三者；playerRename 仅需 gsiInstalled）；
   若当前标签被禁用则强制切回 home。
5. SetObsConnected(bool) 供 HomeViewModel 连接状态变化时调用并触发 CheckPrerequisites。
6. 在 App 启动处：构造 HomeViewModel 时传入 onPrerequisitesChanged → mainViewModel.CheckPrerequisites()；
   将各 Page 的 ViewModel 装配进 Tabs（与 MainWindow 内容区绑定）。
7. 击杀回放与暂停音乐之间的回放播放协调：KillReplayViewModel 暴露 IsReplayPlaying / ReplayPlaybackEnded，
   PauseMusicViewModel 通过 isReplayPlayingProvider 获取，并在回放结束时调用 OnReplayPlaybackEnded。

交付：侧边栏导航 + 前置条件门控 + 回放/暂停音乐协调，行为与原版一致。
```

---

## 7. 验收与风险备注（1:1 复刻范围内保留的已知局限）

- 回放只能在回合结束后生成（非即时回放）。
- 击杀为 GSI 状态差分推断，高频/多杀/异常推送可能漏判（原局限保留）。
- 墙钟时间基准，Demo 变速/跳变不换算（原局限保留）。
- 停用功能时已开始剪辑可能继续（原局限保留，1:1 不引入取消令牌）。
- 首击杀位于录制开头时开始位置截 0 但时长固定 5s（原局限保留）。
- 回放文件留存系统临时目录，无自动清理（原局限保留）。
- OBS 密码明文保存（跨平台 1:1，未引入 DPAPI）。
- HLAE `byXuid` 是否需 `x` 前缀取决于 HLAE 版本，按当前版本游戏内帮助验证（原备注保留）。
