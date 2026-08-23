# MajoCupDirector 仓库研究报告

## 1. 审阅范围

- 仓库：[LaffeyNyaa/MajoCupDirector](https://github.com/LaffeyNyaa/MajoCupDirector)
- 审阅分支：`main`
- 审阅提交：[`11c0297751910ff1b57eea962993a43f2e62dd71`](https://github.com/LaffeyNyaa/MajoCupDirector/commit/11c0297751910ff1b57eea962993a43f2e62dd71)
- 审阅日期：2026-08-23
- 审阅方式：静态阅读源代码、项目文档、配置和提交历史；未连接真实 CS2、OBS、FFmpeg 环境进行端到端运行验证。

## 2. 结论摘要

MajoCupDirector 是一个为 CS2“魔女杯”赛事 Demo/观战转播设计的 Windows 桌面导播工具。它本身不注入或修改 CS2，主要负责把以下组件串联起来：

```text
CS2 Game State Integration（GSI）
        │ HTTP JSON，约每 100ms 推送
        ▼
MajoCupDirector 本地状态判断
        ├── 击杀、回合、比赛状态
        ├── 游戏暂停状态
        └── 玩家 Steam64 ID / 游戏内名字
        │
        ├── OBS WebSocket：录制、切换场景、控制媒体源
        ├── FFmpeg：截取并拼接击杀录像
        └── 魔女杯网站 API：SteamID → 登记选手名
                                      │
                                      ▼
                              生成 HLAE 改名命令
```

主要功能包括：

1. 配置 CS2、FFmpeg 和 OBS WebSocket，并自动安装 GSI 配置。
2. 录制一整回合，根据 GSI 推断击杀，在回合结束后生成击杀集锦并通过 OBS 播放。
3. 游戏暂停时播放指定的 OBS 音乐源，恢复时淡出并停止。
4. 获取当前玩家 Steam64 ID，查询赛事网站登记名，生成 HLAE `mirv_replace_name` 命令。

整体功能链路已经形成，但更接近为特定赛事制作的定制工具，而不是通用、经过充分测试的生产级导播系统。

## 3. 技术架构

### 3.1 技术栈

| 项目 | 实现 |
|---|---|
| 桌面界面 | WPF |
| 运行平台 | Windows |
| 目标框架 | .NET Framework 4.7.2 |
| 架构风格 | View + ViewModel + Service，接近 MVVM |
| JSON | Newtonsoft.Json 13.0.4 |
| OBS 控制 | obs-websocket-dotnet 5.0.0.3 |
| 视频处理 | 外部 FFmpeg 可执行文件 |
| CS2 数据 | Game State Integration，通过本地 HTTP POST |
| 玩家登记信息 | `majo-cup.laffeynyaa.com` HTTPS API |

项目分为两个工程：

- `MajoCupDirector`：主程序、页面、ViewModel 和所有业务服务。
- `MajoCupDirector.UI`：主题、样式、转换器和转场控件。

### 3.2 主要服务

| 服务 | 作用 |
|---|---|
| `Cs2InstallService` | 根据 `cs2.exe` 路径寻找 `csgo/cfg` 并安装 GSI 配置 |
| `GsiService` | 监听 `http://localhost:3000/`，解析 GSI JSON，生成比赛事件 |
| `GsiPayloadParser` | 解析完整 JSON，并尝试从损坏 JSON 中恢复 `map`、`round` |
| `ObsService` | 连接 OBS，控制录制、场景、媒体源和音量 |
| `FfmpegService` | 帧精确提取片段并拼接成回放视频 |
| `MajoCupPlayerApiService` | 根据 Steam64 ID 查询网站登记名 |
| `SettingsService` | 将软件配置持久化为 JSON |

### 3.3 配置持久化

设置文件位于：

```text
%APPDATA%\MajoCupDirector\settings.json
```

其中包括：

- CS2 路径；
- FFmpeg 路径；
- OBS WebSocket 地址、端口和密码；
- 游戏场景、回放场景、回放媒体源名称；
- 击杀回放和暂停音乐的启用状态；
- 暂停音乐媒体源名称。

需要注意，OBS WebSocket 密码以明文形式保存在 JSON 中。

## 4. 是否能获取游戏内玩家数据

### 4.1 可以获取，但当前只对外暴露 SteamID 和名字

程序内置的 GSI 配置会要求 CS2 向以下地址推送数据：

```text
http://localhost:3000/
```

配置参数为：

```text
buffer   = 0.1
throttle = 0.1
heartbeat = 60.0
```

因此正常变化期间推送间隔约为 100ms。配置来源见 [`gsi_cfg.cfg`](https://github.com/LaffeyNyaa/MajoCupDirector/blob/11c0297751910ff1b57eea962993a43f2e62dd71/MajoCupDirector/Resources/gsi_cfg.cfg#L1-L19)。

当前订阅的数据段及用途如下：

| GSI 数据段 | 能获得的内容 | 当前程序是否使用 |
|---|---|---|
| `map` | 地图比赛阶段、回合编号等 | 用于判断比赛开始和回合边界 |
| `round` | `freezetime`、`live`、`over` 等阶段 | 用于触发回合开始/结束 |
| `player_id` | 当前玩家或观战相关身份 | 间接用于观战判断 |
| `player_position` | 当前玩家位置 | 已订阅，但没有解析和使用 |
| `allplayers_id` | 全体玩家 SteamID 和名称 | 用于玩家列表、改名功能 |
| `allplayers_state` | 生命值等玩家状态 | 只读取生命值以判断死亡 |
| `allplayers_match_stats` | 击杀等比赛统计 | 只读取击杀数以推断击杀者 |
| `observer` | 观战目标 | 用于判断镜头是否在击杀者身上 |
| `phase_countdowns` | 当前倒计时/暂停阶段 | 用于暂停音乐功能 |

`GsiService.GetCurrentPlayers()` 当前只返回：

```text
SteamId
Name
```

并且会过滤为“17 位且以 `7656119` 开头”的 Steam64 ID。相关代码见 [`GsiService.GetCurrentPlayers`](https://github.com/LaffeyNyaa/MajoCupDirector/blob/11c0297751910ff1b57eea962993a43f2e62dd71/MajoCupDirector/Services/GsiService.cs#L471-L521)。

### 4.2 当前获取不到或没有对外提供的数据

现有实现没有提供以下信息：

- 玩家武器和弹药；
- 玩家经济、装备和道具；
- 全体玩家位置；
- 伤害、助攻、爆头、穿烟等详细击杀事件；
- 炸弹和投掷物完整状态；
- 可供其他模块订阅的完整原始 GSI 快照。

其中一部分数据可以通过增加 GSI 配置项获得，例如：

```text
allplayers_weapons
allplayers_position
allgrenades
bomb
```

但仍需扩展 `GsiService` 的解析与数据模型。

### 4.3 使用条件与限制

- 这套功能依赖 CS2 客户端主动推送 GSI，而不是读取游戏内存。
- `allplayers` 的完整度与当前游戏/观战状态有关；普通玩家视角下不应假设一定能获得全体玩家信息。
- 仓库的目标场景是观战机或 Demo 转播，这也是全体玩家和观战目标数据最有意义的环境。
- GSI 是状态采样，不是完全可靠的离散游戏事件流；高频或同一推送中的多个事件可能产生歧义。

如果要把它扩展为通用玩家数据接口，建议新增强类型快照，例如：

```csharp
public sealed class PlayerSnapshot
{
    public string SteamId { get; set; }
    public string Name { get; set; }
    public int? Health { get; set; }
    public int? Armor { get; set; }
    public int? Money { get; set; }
    public int? Kills { get; set; }
    public Vector3? Position { get; set; }
}
```

同时暴露 `OnSnapshotUpdated` 或 `GetCurrentSnapshot()`，而不只提供 `GetCurrentPlayers()`。

## 5. 局内击杀回放实现

### 5.1 本质

该功能不是让 CS2 Demo 倒带，也不是调用游戏内的 Replay 系统，而是：

> 先用 OBS 录制比赛画面，再根据 GSI 检测到的击杀时间，用 FFmpeg 截取视频，最后通过 OBS 媒体源播放。

因此它属于“转播画面录像回放”。

### 5.2 前置条件

- 已设置有效 CS2 路径；
- 已安装 GSI 配置；
- 已设置 FFmpeg 路径；
- OBS WebSocket 已连接；
- 用户已勾选启用击杀回放。

如果条件中途失效，勾选状态会保留，但功能停止工作；条件恢复后自动恢复。

### 5.3 回合和录制流程

1. 软件启动时创建并启动统一的 `GsiService`。
2. 检测 `map.phase` 进入 `live`，认为比赛开始。
3. 检测 `round.phase` 进入 `freezetime` 或 `live`，认为新回合开始。
4. 切换到配置的游戏场景，默认名为 `Game`。
5. 如果 OBS 已在录制，则先停止旧录制。
6. 启动一段新的 OBS 录制，并记录真实录制开始时间。
7. 只有录制已确认启动后的击杀才会进入本回合时间戳列表。

回合控制代码位于 [`KillReplayViewModel.OnRoundStarted`](https://github.com/LaffeyNyaa/MajoCupDirector/blob/11c0297751910ff1b57eea962993a43f2e62dd71/MajoCupDirector/ViewModels/KillReplayViewModel.cs#L327-L416)。

### 5.4 击杀识别

代码没有直接收到“玩家 A 击杀玩家 B”的事件，而是根据状态差分推断：

1. 保存上一包每名玩家的生命值和击杀数。
2. 某玩家生命值从 `> 0` 变为 `<= 0` 时，判断其死亡。
3. 遍历其他玩家，比较当前 `match_stats.kills` 与上一包数值。
4. 如果恰好一个玩家的击杀数增加，则把该玩家认定为击杀者。
5. 如果多个玩家的击杀数在同一包中同时增加，则认为存在歧义，不确定击杀者。
6. 使用死亡发生前一包 GSI 的观战目标进行严格匹配。
7. 只有死亡前镜头就在击杀者身上，才把该击杀纳入回放。
8. 击杀者数据可能延迟出现，因此未确认事件最多暂存 1.5 秒，再决定是否丢弃。

实现见 [`GsiService.ProcessPlayerDeaths`](https://github.com/LaffeyNyaa/MajoCupDirector/blob/11c0297751910ff1b57eea962993a43f2e62dd71/MajoCupDirector/Services/GsiService.cs#L523-L730)。

这种设计的目标是只回放已经在导播镜头里真实出现的击杀，避免回放一个直播观众从未看到的击杀画面。

### 5.5 时间记录

每个有效击杀会记录为：

```text
击杀检测时间 - 本次 OBS 录制开始时间
```

时间基准是 Windows 墙钟时间，而不是 Demo Tick 或游戏时间。因此：

- Demo 变速时，片段长度仍按现实秒计算；
- Demo 快进或快退不会被特殊修正；
- 系统时间跳变可能影响时间戳准确性；
- 录制重启后必须重新设置时间零点。

### 5.6 回合结束剪辑

检测到 `round.phase=over` 或 `map.round` 增加后，流程如下：

1. 对当前击杀时间戳做快照，避免下一回合清空列表。
2. 如果最后一次击杀距停止录制不足 1 秒，补充等待，确保包含击杀后画面。
3. 停止 OBS 录制并等待文件落盘。
4. 按以下窗口创建片段：
   - 本回合第一个有效击杀：击杀前 4 秒到击杀后 1 秒，名义时长 5 秒；
   - 后续有效击杀：击杀前 1 秒到击杀后 1 秒，名义时长 2 秒。
5. 时间相交的片段合并为一个连续区间。
6. 调用 FFmpeg 逐段重编码，再拼接最终 MP4。

代码见 [`KillReplayViewModel.OnRoundEnded`](https://github.com/LaffeyNyaa/MajoCupDirector/blob/11c0297751910ff1b57eea962993a43f2e62dd71/MajoCupDirector/ViewModels/KillReplayViewModel.cs#L418-L537)。

FFmpeg 的单片段命令等价于：

```text
ffmpeg -ss <开始秒数> -i <OBS录制文件> -t <时长> \
  -c:v libx264 -preset veryfast -c:a aac -y <临时TS文件>
```

然后使用 concat demuxer 拼接：

```text
ffmpeg -f concat -safe 0 -i concat_list.txt -c copy -y replay.mp4
```

实现见 [`FfmpegService.ClipAndConcatAsync`](https://github.com/LaffeyNyaa/MajoCupDirector/blob/11c0297751910ff1b57eea962993a43f2e62dd71/MajoCupDirector/Services/FfmpegService.cs#L74-L198)。

### 5.7 OBS 播放回放

剪辑完成后：

1. 停止下一回合可能已经开始的录制，避免把回放再次录入。
2. 切换到回放场景，默认名为 `Replay`。
3. 如果回放场景不存在，自动创建。
4. 如果媒体源不存在，创建 `ffmpeg_source`；存在则修改 `local_file`。
5. 播放生成的 MP4。
6. 等待 OBS 的媒体播放结束事件，或者达到“视频实际时长 + 1 秒”的超时预算。
7. 切换回游戏场景。
8. 重新开始 OBS 录制，并重新设置录制时间零点。

代码见 [`KillReplayViewModel.OnClippingComplete`](https://github.com/LaffeyNyaa/MajoCupDirector/blob/11c0297751910ff1b57eea962993a43f2e62dd71/MajoCupDirector/ViewModels/KillReplayViewModel.cs#L539-L634) 和 [`ObsService`](https://github.com/LaffeyNyaa/MajoCupDirector/blob/11c0297751910ff1b57eea962993a43f2e62dd71/MajoCupDirector/Services/ObsService.cs#L136-L358)。

### 5.8 限制和潜在问题

- 回放只能在回合结束后生成，不是击杀发生后立即播放的即时回放。
- 击杀是根据 GSI 状态差分推断，不如真实游戏事件流可靠。
- 同一 GSI 推送中多个玩家击杀数增加时会丢弃相关击杀。
- 只保留镜头原本就在击杀者身上的击杀。
- Demo 变速、跳转使用墙钟时间，不会换算为游戏 Tick。
- 用户关闭功能时会停止录制，但代码没有保存并取消传给 FFmpeg 的 `CancellationToken`，已经开始的剪辑可能继续运行。
- 第一个击杀若发生在录制开始不足 4 秒时，开始位置会被截为 0，但时长仍固定为 5 秒，可能比“击杀后 1 秒”多包含一些画面。
- 输出回放文件保留在系统临时目录，没有发现定期清理最终 `replay_*.mp4` 的机制。

## 6. 暂停音乐实现

### 6.1 名称含义

项目中的“暂停音乐”不是暂停一个正在播放的音乐源，而是：

> 游戏进入暂停状态时播放音乐，游戏恢复时让音乐淡出并停止。

### 6.2 暂停判断

程序读取：

```text
phase_countdowns.phase
```

以下值被视为暂停：

```text
paused
timeout
timeout_ct
timeout_t
```

状态从非暂停变为暂停时触发 `OnGamePaused`，离开这些状态时触发 `OnGameResumed`。实现见 [`GsiService.ProcessPausePhase`](https://github.com/LaffeyNyaa/MajoCupDirector/blob/11c0297751910ff1b57eea962993a43f2e62dd71/MajoCupDirector/Services/GsiService.cs#L440-L469)。

### 6.3 播放流程

1. 检查用户是否启用功能。
2. 检查填写的 OBS 音乐源名称是否存在。
3. 读取并保存该媒体源的原始音量。
4. 将媒体源音量设置为原始音量。
5. 对媒体源执行 `OBS_WEBSOCKET_MEDIA_INPUT_ACTION_RESTART`。
6. 音乐从头播放，不执行淡入。
7. 约 1 秒后读取媒体源状态、播放位置和总时长，并输出诊断日志。

使用 `RESTART` 而不是 `PLAY`，是为了避免某些 OBS 媒体源在播放位置为 0 时不响应 `PLAY`。

### 6.4 恢复流程

游戏离开暂停状态后：

1. 从当前音量开始淡出。
2. 每 50ms 调整一次音量。
3. 在 2 秒内线性下降到 `-100 dB`。
4. 对媒体源执行 `STOP`。
5. 将音量恢复为进入暂停前保存的原始值。

完整实现见 [`PauseMusicViewModel`](https://github.com/LaffeyNyaa/MajoCupDirector/blob/11c0297751910ff1b57eea962993a43f2e62dd71/MajoCupDirector/ViewModels/PauseMusicViewModel.cs#L130-L344)。

### 6.5 与击杀回放的协调

如果游戏在击杀回放播放期间进入暂停：

- 暂时不播放音乐；
- 设置 `_pendingPlayOnReplayEnd`；
- 等回放结束并切回游戏场景后再播放；
- 如果回放结束前游戏已经恢复，则 `OnGameResumed` 会清除挂起标记，不再播放。

这可以避免音乐源位于游戏场景时，因当前正在展示回放场景而播放失败或让观众听不到。

### 6.6 限制

- 音乐媒体源必须事先存在，程序不会自动创建暂停音乐源。
- 页面被设计成与击杀回放共享前置条件，因此 UI 还要求 FFmpeg 已配置，尽管暂停音乐业务逻辑本身并不使用 FFmpeg。
- 暂停状态完全依赖 `phase_countdowns`；旧 GSI 配置若没有该字段，需要重新安装配置。
- 淡出是对 dB 数值做线性插值，不等同于人耳感知响度上的线性变化。

## 7. 玩家改名实现

### 7.1 数据来源

点击“获取命令”后，程序调用：

```csharp
_gsiService.GetCurrentPlayers()
```

取得当前 GSI 快照中的 Steam64 ID 和游戏内名字。若快照为空，会提示确认 CS2 正在运行且 GSI 已生效。

### 7.2 查询网站登记名

对每名玩家依次请求：

```text
GET https://majo-cup.laffeynyaa.com/api/v1/players/steam/{Steam64ID}
```

处理规则：

- HTTP 404：视为玩家未登记，跳过；
- 其他非成功状态：记录为失败；
- 成功：解析 JSON 顶层的 `name` 字段；
- 单次请求超时：10 秒；
- 请求是逐个串行执行，并非并发。

实现见 [`MajoCupPlayerApiService`](https://github.com/LaffeyNyaa/MajoCupDirector/blob/11c0297751910ff1b57eea962993a43f2e62dd71/MajoCupDirector/Services/MajoCupPlayerApiService.cs#L9-L53)。

### 7.3 生成 HLAE 命令

每个成功查到名字的玩家会生成：

```text
mirv_replace_name byXuid add "Steam64ID" "登记名"
```

程序会转义名字中的反斜杠和双引号，然后用分号连接所有命令：

```text
mirv_replace_name ...;mirv_replace_name ...;mirv_replace_name ...
```

最后将整行命令复制到 Windows 剪贴板。用户需要：

1. 使用 HLAE/AfxHookSource2 启动 CS2；
2. 打开游戏控制台；
3. 粘贴并执行命令。

代码见 [`PlayerRenameViewModel`](https://github.com/LaffeyNyaa/MajoCupDirector/blob/11c0297751910ff1b57eea962993a43f2e62dd71/MajoCupDirector/ViewModels/PlayerRenameViewModel.cs#L61-L126)。

### 7.4 该功能实际修改什么

它不会：

- 修改 Steam 昵称；
- 修改服务器端玩家名称；
- 向 CS2 自动发送控制台命令；
- 直接写入 CS2 内存或配置文件。

它只是生成 HLAE 的显示替换规则。AdvancedFX 文档也说明，该命令只能替换 CS2 HUD 中的部分名称显示，并非所有位置都会变化：[`Source2:mirv_replace_name`](https://github.com/advancedfx/advancedfx/wiki/Source2%3Amirv_replace_name)。

### 7.5 HLAE XUID 兼容性风险

仓库当前生成的是：

```text
mirv_replace_name byXuid add "7656119..." "Name"
```

而当前 AdvancedFX 文档示例使用：

```text
mirv_replace_name byXuid add x7656119... "Name"
```

也就是在 XUID 数字前增加 `x`。不同 HLAE 版本的解析行为可能不同，建议按当前安装版本的游戏内帮助验证：

```text
mirv_replace_name
mirv_replace_name byXuid
mirv_replace_name help players
```

若当前版本要求 `x` 前缀，代码应修改为：

```csharp
commands.Add(
    $"mirv_replace_name byXuid add x{player.SteamId} \"{EscapeName(registeredName)}\""
);
```

## 8. 项目成熟度与风险评估

### 8.1 优点

- 功能边界清晰，GSI、OBS、FFmpeg 和网站 API 被拆分为独立服务。
- GSI 服务由应用统一管理，多个功能共享同一监听器。
- 对 OBS 场景、媒体源不存在的情况有一定自动恢复能力。
- 回放使用重编码截取，避免纯 `-c copy` 因关键帧造成片段边界不准确。
- 对损坏 GSI JSON 有有限恢复逻辑，并抑制重复错误日志。
- 击杀回放只选择观战镜头真正捕捉到的击杀，符合转播语义。
- 暂停音乐与回放场景做了基本协调。

### 8.2 主要风险

| 风险 | 影响 |
|---|---|
| GSI 击杀为状态差分推断 | 高频、多杀或异常推送可能漏判 |
| 缺少自动化测试 | 状态机、OBS 时序和剪辑边界容易回归 |
| 没有真实端到端测试脚本 | 难以稳定复现 CS2 → GSI → OBS → FFmpeg 流程 |
| 使用墙钟时间 | Demo 变速、跳转和系统时钟变化可能造成错位 |
| FFmpeg 缺少上层取消控制 | 禁用功能后剪辑可能仍继续 |
| HLAE 命令格式可能版本不兼容 | 玩家改名命令可能无法直接生效 |
| OBS 密码明文保存 | 本机配置文件泄露时密码也会泄露 |
| 外部网站 API 是单点依赖 | API 离线、变更或超时会影响改名功能 |
| 最终回放文件没有清理策略 | 长期使用可能积累临时 MP4 文件 |

## 9. 建议的改进优先级

### P0：影响功能正确性

1. 按实际 HLAE 版本验证 `byXuid` 是否必须使用 `x` 前缀。
2. 用录制的真实 GSI Payload 建立击杀、双杀、换人观战、暂停、Demo 跳转测试集。
3. 为每个录制会话分配不可变 Session ID，防止上一回合异步任务影响下一回合。
4. 在 ViewModel 中保存 `CancellationTokenSource`，关闭功能时取消 FFmpeg 任务和后续回放。
5. 修正第一击杀位于录制开头时的片段结束时间，确保仍严格结束于击杀后 1 秒。

### P1：提升数据能力和稳定性

1. 为 GSI 建立完整的强类型快照模型。
2. 暴露原始快照事件，而不是只暴露 `SteamId + Name`。
3. 增加 `allplayers_position`、武器、炸弹等可选订阅配置。
4. 使用 `Stopwatch` 或单调时钟记录视频相对时间，减少系统时间调整的影响。
5. 给 OBS 操作增加显式状态机、超时和幂等处理。
6. 对最终回放文件增加保留数量或定期清理策略。

### P2：安全性和体验

1. 使用 Windows DPAPI 加密保存 OBS WebSocket 密码。
2. 玩家网站 API 请求改为有并发上限的并发查询，减少全员查询时间。
3. 在 UI 中展示当前 GSI 玩家数、最后推送时间、观战目标和当前回合状态。
4. 将暂停音乐的前置条件从 FFmpeg 中解耦。
5. 增加发布包、安装说明、OBS 场景模板和故障排查文档。

## 10. 最终判断

MajoCupDirector 已经实现了魔女杯转播所需的完整基本闭环：

- 能从 CS2 GSI 获取当前比赛、回合、暂停和部分玩家数据；
- 能根据生命值、击杀数和观战目标推断适合回放的击杀；
- 能通过 OBS 录制画面并用 FFmpeg 生成回合击杀回放；
- 能在游戏暂停期间控制 OBS 音乐源；
- 能根据 Steam64 ID 查询登记名并生成 HLAE 改名命令。

但它目前仍适合“指定赛事、固定导播环境、有人值守”的使用方式。若要扩展为长期稳定运行或可复用的 CS2 导播平台，应优先完善测试、GSI 数据模型、任务取消和录制状态机，并验证当前 HLAE 版本的命令兼容性。

