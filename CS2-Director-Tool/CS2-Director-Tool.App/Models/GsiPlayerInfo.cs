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

    /// <summary>玩家的所属队伍（"T" / "CT"，未知为空）。</summary>
    public string Team { get; set; } = string.Empty;

    /// <summary>网站登记的名称；未登记时为空字符串。</summary>
    public string RegisteredName { get; set; } = string.Empty;

    /// <summary>该玩家是否在网站登记了名称。</summary>
    public bool IsRegistered { get; set; }

    /// <summary>队伍显示名。</summary>
    public string TeamLabel => Team switch
    {
        "T" => "恐怖分子",
        "CT" => "反恐精英",
        _ => "未知"
    };

    /// <summary>列表展示的玩家名：优先登记名，未登记回退为游戏内名并标注。</summary>
    public string DisplayName => IsRegistered ? RegisteredName : $"{Name}（未登记）";
}