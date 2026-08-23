namespace CS2_Director_Tool.App.Models;

/// <summary>
/// 描述 OBS 媒体源的播放状态。
/// </summary>
public class ObsMediaStatusInfo
{
    /// <summary>媒体源状态（如 OBS_MEDIA_STATE_PLAYING）。</summary>
    public string? State { get; set; }

    /// <summary>媒体总时长（毫秒）；未播放时为 null。</summary>
    public long? DurationMs { get; set; }

    /// <summary>播放进度位置（毫秒）；未播放时为 null。</summary>
    public long? CursorMs { get; set; }
}
