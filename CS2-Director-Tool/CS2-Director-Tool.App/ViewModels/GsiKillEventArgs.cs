using System;

namespace CS2_Director_Tool.App.Models;

/// <summary>
/// 击杀事件的事件参数。
/// </summary>
public class GsiKillEventArgs : EventArgs
{
    /// <summary>完成击杀的玩家名称。</summary>
    public string KillerName { get; set; } = string.Empty;

    /// <summary>被击杀的玩家名称。</summary>
    public string VictimName { get; set; } = string.Empty;

    /// <summary>观察者是否位于击杀者视角。</summary>
    public bool IsObserverOnKiller { get; set; }

    /// <summary>观察者当前观战的玩家名称。</summary>
    public string? ObserverTargetName { get; set; }

    /// <summary>检测到击杀的时刻（墙钟时间），用于保证剪辑时机的准确。</summary>
    public DateTime KillDetectedAt { get; set; }
}
