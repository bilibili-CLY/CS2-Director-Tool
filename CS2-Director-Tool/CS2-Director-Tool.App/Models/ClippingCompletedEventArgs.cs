using System;

namespace CS2_Director_Tool.App.Models;

/// <summary>
/// 为 <see cref="IFfmpegService.OnClippingComplete"/> 事件提供数据。
/// </summary>
public class ClippingCompletedEventArgs : EventArgs
{
    /// <summary>初始化 <see cref="ClippingCompletedEventArgs"/> 类的新实例。</summary>
    /// <param name="filePath">生成的视频文件路径。</param>
    /// <param name="duration">生成的视频文件实际时长。</param>
    public ClippingCompletedEventArgs(string filePath, TimeSpan duration)
    {
        FilePath = filePath;
        Duration = duration;
    }

    /// <summary>获取生成的视频文件路径。</summary>
    public string FilePath { get; }

    /// <summary>获取生成的视频文件实际时长。</summary>
    public TimeSpan Duration { get; }
}
