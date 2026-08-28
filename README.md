# CS2 Director Tool

为 CS2（《反恐精英 2》）赛事 Demo / 观战转播设计的跨平台桌面导播工具。
基于 **.NET 10 + Avalonia UI** 构建，支持 **Windows 与 macOS**。

> 项目前身为 [LaffeyNyaa/MajoCupDirector](https://github.com/LaffeyNyaa/MajoCupDirector)（WPF / .NET Framework 4.7.2），
> 在原有功能的基础上重写为跨平台版本，并扩展了「事件动作」与集中式日志能力。

---

## 功能总览

工具本身不注入或修改 CS2，而是把以下组件串联成一条转播自动化链路：

```
CS2 Game State Integration（GSI, HTTP JSON ~100ms 推送）
        │
        ▼
CS2 Director Tool 本地状态判断（比赛 / 回合 / 暂停 / 击杀 / 炸弹 / 玩家…）
        ├── OBS WebSocket：录制、切换场景、控制媒体源
        ├── FFmpeg：截取并拼接击杀录像
        └── 赛事网站 API（可配置）：SteamID → 登记选手名 → HLAE 改名命令
```

内置页面：

| 页面 | 说明 |
|---|---|
| **主页** | CS2 / FFmpeg / OBS WebSocket 配置；自动安装 GSI 配置到 CS2 目录；实时显示 GSI 与 OBS 连接状态。 |
| **事件动作** | 基于 GSI 事件（回合开始/结束、暂停、炸弹、击杀…）定义规则，触发播放媒体 / 停止媒体 / 切换场景 / 开始录制 / 记录击杀点 / 生成回放 等动作。支持预设保存/加载。 |
| **玩家改名** | 从 GSI 快照获取当前玩家 Steam64 ID，调用可配置的赛事 API 查询登记名，生成 HLAE `mirv_replace_name byXuid add` 命令并复制到剪贴板。 |
| **日志** | 集中查看全系统日志；支持按类别（GSI / OBS / FFmpeg / App / EventAction 等）过滤、时间范围过滤、关键字搜索。 |

---

## 技术栈与架构

| 项目 | 说明 |
|---|---|
| 运行时 | .NET 10 |
| UI 框架 | Avalonia 12.1.1（Fluent Theme 基础 + 自定义「彼岸花 / Lycoris」暗色主题） |
| MVVM | CommunityToolkit.Mvvm 8.4（`[ObservableProperty]` / `[RelayCommand]`） |
| 依赖注入 | Microsoft.Extensions.DependencyInjection 8.0 |
| JSON 解析 | Newtonsoft.Json 13.0.4（GSI 解析使用三级回退策略） |
| OBS 控制 | obs-websocket-dotnet 5.0.0.3 |
| 视频处理 | 外部 FFmpeg 可执行文件（用户自行下载并在主页配置路径） |
| CS2 数据来源 | CS2 Game State Integration（HTTP POST 到 `http://localhost:3000/`） |
| 架构风格 | View（Page.axaml）+ ViewModel（\*ViewModel）+ Service（I\*Service），严格接口 + 实现解耦 |

### 关键服务

| 服务 | 职责 |
|---|---|
| `GsiService` | 监听 `http://localhost:3000/`，通过 `GsiPayloadParser` 解析 GSI JSON，产出比赛 / 回合 / 暂停 / 炸弹 / 击杀等事件。 |
| `ObsService` | 连接 OBS WebSocket，封装录制、场景切换、媒体源播放/停止/音量控制、回放媒体源自动创建等。 |
| `FfmpegService` | 按时间戳对 OBS 录制文件做帧精确截取（`-ss` 在 `-i` 前，重编码 `libx264 veryfast`），并使用 concat demuxer 拼接最终回放。 |
| `ReplayWorkflowService` | 击杀回放工作流：回合开始切换游戏场景并录制 → 记录击杀时间点 → 回合结束 FFmpeg 剪辑拼接 → OBS 播放回放 → 恢复录制。 |
| `EventActionService` | 订阅 GSI 事件，匹配用户规则后顺序执行 `PlayMedia / StopMedia / SwitchScene / StartReplayRecording / RecordKillPoint / GenerateReplay`。 |
| `MajoCupPlayerApiService` | 按 Steam64 ID 查询赛事登记名（BaseUrl 可配置，默认沿用魔女杯 API）。 |
| `Cs2InstallService` | 解析 CS2 安装目录下的 `csgo/cfg`，安装 / 检查 `gamestate_integration_majo_cup.cfg`。 |
| `SettingsService` | 持久化所有配置为 JSON（跨平台路径：`%APPDATA%/CS2-Director-Tool/settings.json` 或对应平台目录）。 |
| `LogService` | 集中日志基础设施，支持分类别记录，被各页面与服务复用。 |

---

## 事件动作机制

事件动作页面取代了原版分离的「击杀回放」「暂停音乐」配置页，使用统一的规则引擎驱动：

### 可用 GSI 事件

`比赛开始 / 回合开始 / 回合结束 / 比赛结束 / 热身开始 / 热身结束 / 暂停开始 / 暂停结束 / 炸弹安装 / 炸弹拆除 / 炸弹爆炸 / 玩家死亡 / 玩家击杀`

### 可执行动作

| 动作 | 说明 |
|---|---|
| 播放媒体源 | 对指定 OBS 媒体源执行 `RESTART`，从头播放。 |
| 停止媒体源 | 对指定 OBS 媒体源执行 `STOP`。 |
| 切换场景 | 切换到指定 OBS 场景（场景不存在时自动创建）。 |
| 开始录制 | 切换到游戏场景并启动 OBS 录制，作为击杀回放会话起点。 |
| 记录击杀点 | 在录制会话内记录当前时间戳（仅在击杀事件中有效）。 |
| 生成回放 | 停止录制 → 按击杀点用 FFmpeg 剪辑拼接 → 切到回放场景播放 → 恢复录制。 |

### 典型预设示例（击杀回放 + 暂停音乐）

```
规则 1：事件 = 回合开始 → 动作 = 开始录制
规则 2：事件 = 玩家击杀 → 动作 = 记录击杀点
规则 3：事件 = 回合结束 → 动作 = 生成回放
规则 4：事件 = 暂停开始 → 动作 = 播放媒体源（选择「暂停音乐」源）
规则 5：事件 = 暂停结束 → 动作 = 停止媒体源（选择「暂停音乐」源）
```

可以整组保存为「预设」，在不同赛事 / 转播需求间快速切换。

---

## 玩家改名

1. 在主页配置好 CS2 路径并安装 GSI，启动 CS2 进入观战 / Demo。
2. 打开「玩家改名」页，点击「获取命令」。
3. 程序调用 `GsiService.GetCurrentPlayers()` 取得 Steam64 ID，再向赛事 API 请求登记名。
4. 对每位已登记玩家生成：
   ```
   mirv_replace_name byXuid add "<Steam64ID>" "<登记名>"
   ```
5. 所有命令用分号连接后自动复制到剪贴板。
6. 在 HLAE 启动的 CS2 控制台粘贴执行即可替换 HUD 显示名称。

> API BaseUrl 可通过 `settings.json` 中的 `PlayerApiBaseUrl` 字段自定义（默认 `https://majo-cup.laffeynyaa.com`）。

---

## 构建与运行

### 环境要求

- .NET 10 SDK
- Windows 10/11 **或** macOS 10.15+
- 外部依赖（用户自备）：
  - CS2 已安装
  - [FFmpeg](https://ffmpeg.org/download.html) 可执行文件
  - [OBS Studio](https://obsproject.com/) 并开启 **WebSocket Server**（工具 → WebSocket Server 设置）

### 开发运行

```bash
cd CS2-Director-Tool
dotnet run --project CS2-Director-Tool.App/CS2-Director-Tool.App.csproj
```

### Windows 构建（发布）

根据仓库内 [AGENTS.md](./AGENTS.md)：

```bash
dotnet publish CS2-Director-Tool.App/CS2-Director-Tool.App.csproj -c Release -r win-x64 --self-contained -o ./publish/win-x64
# 打包
cd ./publish/win-x64 && zip -r ../../CS2-Director-Tool-win-x64.zip .
```

### 分支策略

- `develop`：默认开发分支，所有改动合入此处。
- `main`：稳定发布分支，仅在准备发布时合并 `develop`，并构建 + 上传 Release 包。

---

## 使用步骤（快速上手）

1. **启动 CS2 Director Tool**，进入「主页」。
2. 选择 `cs2.exe` 路径，点击「安装 GSI 配置」（若未自动安装）。
3. 选择 FFmpeg 可执行文件路径。
4. 填入 OBS WebSocket 地址（通常为 `localhost`）、端口（默认 `4455`）与密码，点击连接。
5. 进入「事件动作」页，配置转播所需规则（或加载预设）并启用。
6. 启动 CS2（或 HLAE 启动的 CS2）、启动 OBS，开始 / 加入观战即可享受自动化转播。
7. 遇到异常时打开「日志」页，按类别 / 时间 / 关键字排查。

---

## 目录结构

```
CS2-Director-Tool/
├── CS2-Director-Tool.App/
│   ├── App.axaml(.cs)            # DI 容器构建、页面装配、GSI 生命周期
│   ├── Program.cs                # 应用入口
│   ├── Assets/                   # 图标等静态资源
│   ├── Resources/gsi_cfg.cfg     # 嵌入的 CS2 GSI 配置
│   ├── Themes/                   # Lycoris 暗色主题与控件样式
│   ├── Converters/               # XAML 值转换器
│   ├── Models/                   # TabItem、LogEntry、EventAction*、GsiPlayerInfo 等模型
│   ├── Services/                 # 所有业务服务（接口 + 实现）
│   ├── ViewModels/               # Home / EventAction / PlayerRename / Log / Main
│   └── Views/                    # HomePage / EventActionPage / PlayerRenamePage / LogPage + MainWindow
├── doc/                          # 需求文档与原仓库研究报告（历史存档）
└── AGENTS.md                     # 开发流程、构建命令、代码规范
```

---

## 已知限制

- GSI 是状态采样，不是离散游戏事件流；高频多杀 / 同一推送中的歧义事件可能被丢弃。
- 击杀回放的时间基准为墙钟时间（`DateTime.Now`），Demo 变速 / 跳转会影响片段定位准确度。
- OBS WebSocket 密码以明文保存在 `settings.json` 中。
- 回放输出文件位于系统临时目录，暂无自动清理策略，长期运行请定期清理。
- 玩家改名命令基于 HLAE `mirv_replace_name byXuid`，部分 HLAE 版本可能要求 XUID 前缀 `x`，请按安装版本验证。

---

## License

见仓库内对应许可文件。Portions of this project were generated with AI assistance and have been reviewed and modified by the developer.
