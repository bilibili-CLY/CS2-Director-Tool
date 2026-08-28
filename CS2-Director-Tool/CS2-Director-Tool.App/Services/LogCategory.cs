namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 日志类别常量，同时用于日志页的类别筛选。
/// </summary>
public static class LogCategory
{
    public const string App = "系统";
    public const string Home = "主页";
    public const string EventAction = "事件动作";
    public const string Replay = "回放";
    public const string PlayerRename = "玩家改名";
    public const string Gsi = "GSI";
    public const string Obs = "OBS";
    public const string Ffmpeg = "FFmpeg";

    /// <summary>全部类别，供日志页筛选下拉使用。</summary>
    public static readonly string[] All =
    {
        App, Home, EventAction, Replay, PlayerRename, Gsi, Obs, Ffmpeg
    };
}
