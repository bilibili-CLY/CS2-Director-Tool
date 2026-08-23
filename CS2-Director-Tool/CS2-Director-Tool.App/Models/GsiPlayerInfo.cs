namespace CS2_Director_Tool.App.Models;

/// <summary>
/// GSI allplayers 部分中观察到的玩家。
/// </summary>
public class GsiPlayerInfo
{
    /// <summary>玩家的 Steam 64 位 ID。</summary>
    public string SteamId { get; set; } = string.Empty;

    /// <summary>玩家的游戏内名称。</summary>
    public string Name { get; set; } = string.Empty;
}
