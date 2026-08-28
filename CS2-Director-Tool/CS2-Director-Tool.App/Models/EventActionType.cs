using System.ComponentModel;

namespace CS2_Director_Tool.App.Models;

/// <summary>
/// 事件触发后可执行的动作类型。
/// </summary>
public enum EventActionType
{
    /// <summary>在指定的 OBS 媒体源上从头播放。</summary>
    [Description("播放媒体源")]
    PlayMedia,

    /// <summary>停止指定的 OBS 媒体源。</summary>
    [Description("停止媒体源")]
    StopMedia,

    /// <summary>切换到指定的 OBS 场景。</summary>
    [Description("切换场景")]
    SwitchScene,

    /// <summary>切换到游戏场景并开始 OBS 录制，作为击杀回放会话的起点。</summary>
    [Description("开始录制")]
    StartReplayRecording,

    /// <summary>记录当前回合的一个击杀时间点（仅在录制活跃时生效）。</summary>
    [Description("记录击杀点")]
    RecordKillPoint,

    /// <summary>生成并播放击杀回放：停止录制、按击杀点剪辑拼接、播放、恢复录制。</summary>
    [Description("生成回放")]
    GenerateReplay
}
