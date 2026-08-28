using System;
using System.Collections.Generic;
using CS2_Director_Tool.App.Models;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 提供 CS2 游戏状态集成（GSI）监控功能。
/// </summary>
public interface IGsiService
{
    /// <summary>获取 GSI 服务器是否正在运行。</summary>
    bool IsRunning { get; }

    /// <summary>检测到击杀事件时发生。</summary>
    event EventHandler<GsiKillEventArgs> OnKill;

    /// <summary>新回合开始时发生（阶段切换到 freezetime 或 live）。</summary>
    event EventHandler OnRoundStarted;

    /// <summary>比赛正式开始时发生（地图阶段为 "live"）。</summary>
    event EventHandler OnMatchStarted;

    /// <summary>回合结束时发生（阶段切换到 over）。</summary>
    event EventHandler OnRoundEnded;

    /// <summary>游戏进入暂停状态时发生。</summary>
    event EventHandler OnGamePaused;

    /// <summary>游戏退出暂停状态时发生。</summary>
    event EventHandler OnGameResumed;

    /// <summary>比赛结束时发生（地图阶段为 "gameover"）。</summary>
    event EventHandler OnGameOver;

    /// <summary>热身开始时发生（地图阶段进入 "warmup"）。</summary>
    event EventHandler OnWarmupStarted;

    /// <summary>热身结束时发生（地图阶段由 "warmup" 变为其他）。</summary>
    event EventHandler OnWarmupOver;

    /// <summary>炸弹被安装时发生。</summary>
    event EventHandler OnBombPlanted;

    /// <summary>炸弹被拆除时发生。</summary>
    event EventHandler OnBombDefused;

    /// <summary>炸弹爆炸时发生。</summary>
    event EventHandler OnBombExploded;

    /// <summary>检测到玩家死亡（生命值降为 0）时发生。</summary>
    event EventHandler OnPlayerDied;

    /// <summary>返回当前已知玩家的快照（SteamID 与游戏内名称），仅保留有效的 Steam 64 位 ID。</summary>
    IReadOnlyList<GsiPlayerInfo> GetCurrentPlayers();

    /// <summary>启动 GSI HTTP 监听器。</summary>
    void Start();

    /// <summary>停止 GSI HTTP 监听器。</summary>
    void Stop();
}
