using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace CS2_Director_Tool.App.Models;

/// <summary>
/// 对阵双方名单响应（`/api/matches/games/{gameId}/roster` 的 data 字段）。
/// </summary>
public class GameRosterData
{
    [JsonProperty("gameId")]
    public string GameId { get; set; } = string.Empty;

    [JsonProperty("teamA")]
    public RosterTeam? TeamA { get; set; }

    [JsonProperty("teamB")]
    public RosterTeam? TeamB { get; set; }
}

/// <summary>
/// 一侧战队信息，包含队标与队员名单。
/// </summary>
public partial class RosterTeam : ObservableObject
{
    private Bitmap? _logoImage;
    private byte[]? _logoBytes;

    [JsonProperty("teamId")]
    public string TeamId { get; set; } = string.Empty;

    /// <summary>完美平台战队 ID（对外路由 / 页面跳转用）。</summary>
    [JsonProperty("perfectId")]
    public string? PerfectId { get; set; }

    [JsonProperty("teamName")]
    public string TeamName { get; set; } = string.Empty;

    /// <summary>战队 LOGO 相对路径（如 /uploads/xxx.png）。</summary>
    [JsonProperty("logoUrl")]
    public string? LogoUrl { get; set; }

    [JsonProperty("members")]
    public ObservableCollection<RosterMember> Members { get; set; } = new();

    /// <summary>下载的 LOGO 原始字节（保存到本地用）。</summary>
    public byte[]? LogoBytes
    {
        get => _logoBytes;
        set => SetProperty(ref _logoBytes, value);
    }

    /// <summary>用于列表展示的战队 LOGO 图片。</summary>
    public Bitmap? LogoImage
    {
        get => _logoImage;
        set
        {
            if (SetProperty(ref _logoImage, value))
                OnPropertyChanged(nameof(ShowLogoPlaceholder));
        }
    }

    /// <summary>是否无 LOGO 可显示（使用占位）。</summary>
    public bool ShowLogoPlaceholder => LogoImage is null;

    /// <summary>占位首字符。</summary>
    public string LogoPlaceholderChar => string.IsNullOrWhiteSpace(TeamName)
        ? "?"
        : TeamName.Trim()[..1].ToUpperInvariant();
}

/// <summary>
/// 战队内的一名队员。
/// </summary>
public partial class RosterMember : ObservableObject
{
    private Bitmap? _avatarImage;
    private byte[]? _avatarBytes;

    [JsonProperty("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>对外用户身份码（未绑定为 null）。</summary>
    [JsonProperty("uid")]
    public int? Uid { get; set; }

    /// <summary>注册用户名。</summary>
    [JsonProperty("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>对外展示昵称（来自 player_profiles.Name）。</summary>
    [JsonProperty("nickname")]
    public string? Nickname { get; set; }

    /// <summary>玩家头像相对路径。</summary>
    [JsonProperty("avatarUrl")]
    public string? AvatarUrl { get; set; }

    /// <summary>玩家绑定的 Steam64 ID。</summary>
    [JsonProperty("steamId")]
    public string? SteamId { get; set; }

    /// <summary>身份：0=首发，1=替补，2=教练。</summary>
    [JsonProperty("role")]
    public int Role { get; set; }

    [JsonProperty("isCaptain")]
    public bool IsCaptain { get; set; }

    /// <summary>下载的头像原始字节（保存到本地用）。</summary>
    public byte[]? AvatarBytes
    {
        get => _avatarBytes;
        set => SetProperty(ref _avatarBytes, value);
    }

    /// <summary>用于列表展示的队员头像图片。</summary>
    public Bitmap? AvatarImage
    {
        get => _avatarImage;
        set
        {
            if (SetProperty(ref _avatarImage, value))
                OnPropertyChanged(nameof(ShowAvatarPlaceholder));
        }
    }

    /// <summary>是否无头像可显示（使用占位）。</summary>
    public bool ShowAvatarPlaceholder => AvatarImage is null;

    /// <summary>列表展示的队员名：优先昵称，缺失回退注册用户名。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Nickname) ? Username : Nickname;

    /// <summary>Steam64 ID 展示文本，未绑定时给出提示。</summary>
    public string SteamDisplay => string.IsNullOrWhiteSpace(SteamId) ? "未绑定 Steam" : SteamId;

    /// <summary>身份标签。</summary>
    public string RoleLabel => Role switch
    {
        0 => "首发",
        1 => "替补",
        2 => "教练",
        _ => "未知"
    };

    /// <summary>占位首字符。</summary>
    public string PlaceholderChar => string.IsNullOrWhiteSpace(DisplayName)
        ? "?"
        : DisplayName.Trim()[..1].ToUpperInvariant();
}