using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CS2_Director_Tool.App.Models;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 提供 FFmpeg 视频处理能力。
/// </summary>
public interface IFfmpegService
{
    /// <summary>获取 ffmpeg 路径是否有效。</summary>
    bool IsValid { get; }

    /// <summary>校验 ffmpeg 可执行文件路径。</summary>
    bool ValidatePath(string ffmpegPath);

    /// <summary>围绕指定时间戳剪辑视频片段并进行拼接。</summary>
    Task ClipAndConcatAsync(string inputFile, string outputFile, IReadOnlyList<ClipSegment> clips,
        string ffmpegPath, CancellationToken cancellationToken = default);

    /// <summary>剪辑进度更新时发生（0~100）。</summary>
    event EventHandler<double> OnClippingProgress;

    /// <summary>剪辑完成时发生，携带生成的视频文件路径及其实际时长。</summary>
    event EventHandler<ClippingCompletedEventArgs> OnClippingComplete;
}
