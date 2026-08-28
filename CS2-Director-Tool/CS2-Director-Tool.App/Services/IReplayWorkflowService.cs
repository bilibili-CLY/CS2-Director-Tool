using System;
using System.Threading.Tasks;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 击杀回放工作流：负责录制会话、击杀时间点收集、FFmpeg 剪辑拼接与回放协调。
/// 由事件动作系统在匹配的规则被触发时调用。
/// </summary>
public interface IReplayWorkflowService
{
    /// <summary>获取当前是否正在播放回放。</summary>
    bool IsReplayPlaying { get; }

    /// <summary>获取前回放功能所需的前置条件（OBS、FFmpeg、GSI）是否满足。</summary>
    bool PrerequisitesMet { get; }

    /// <summary>响应 OBS/环境连接状态变化时刷新前置条件。</summary>
    void RefreshPrerequisites();

    /// <summary>切换到游戏场景并开始一段回放录制（回合开始）。</summary>
    Task<bool> StartRecordingAsync();

    /// <summary>记录当前回合的一个击杀时间点（录制活跃时生效）。</summary>
    Task RecordKillPointAsync();

    /// <summary>回合结束：停止录制、按击杀点剪辑拼接并播放回放，随后恢复录制。</summary>
    Task GenerateReplayAsync();
}
