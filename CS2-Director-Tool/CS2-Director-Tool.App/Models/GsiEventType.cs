using System.ComponentModel;

namespace CS2_Director_Tool.App.Models;

/// <summary>
/// 可供配置的 CS2 GSI 事件类型（精选常用事件）。
/// </summary>
public enum GsiEventType
{
    /// <summary>比赛开始（map.phase 变为 live）。</summary>
    [Description("比赛开始")]
    MatchStarted,

    /// <summary>回合开始（round.phase 变为 freezetime 或 live）。</summary>
    [Description("回合开始")]
    RoundStarted,

    /// <summary>回合结束（round.phase 变为 over 或地图回合数递增）。</summary>
    [Description("回合结束")]
    RoundEnded,

    /// <summary>比赛结束（map.phase 变为 gameover）。</summary>
    [Description("比赛结束")]
    GameOver,

    /// <summary>热身开始（map.phase 变为 warmup）。</summary>
    [Description("热身开始")]
    WarmupStarted,

    /// <summary>热身结束（map.phase 由 warmup 变为其他）。</summary>
    [Description("热身结束")]
    WarmupOver,

    /// <summary>游戏暂停开始。</summary>
    [Description("暂停开始")]
    PauseStarted,

    /// <summary>游戏暂停结束。</summary>
    [Description("暂停结束")]
    PauseOver,

    /// <summary>炸弹被安装。</summary>
    [Description("炸弹安装")]
    BombPlanted,

    /// <summary>炸弹被拆除。</summary>
    [Description("炸弹拆除")]
    BombDefused,

    /// <summary>炸弹爆炸。</summary>
    [Description("炸弹爆炸")]
    BombExploded,

    /// <summary>玩家死亡（生命值降为 0）。</summary>
    [Description("玩家死亡")]
    PlayerDied,

    /// <summary>玩家击杀。</summary>
    [Description("玩家击杀")]
    Kill
}
