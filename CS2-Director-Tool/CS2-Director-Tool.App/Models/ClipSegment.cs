namespace CS2_Director_Tool.App.Models;

/// <summary>
/// 表示一个视频剪辑片段。
/// </summary>
public class ClipSegment
{
    /// <summary>开始时间（秒，相对录制零点）。</summary>
    public double StartTime { get; set; }

    /// <summary>时长（秒）。</summary>
    public double Duration { get; set; }
}
