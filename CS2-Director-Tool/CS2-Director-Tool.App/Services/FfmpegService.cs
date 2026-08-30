using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CS2_Director_Tool.App.Models;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 提供 FFmpeg 视频处理能力。
/// </summary>
public class FfmpegService : IFfmpegService
{
    private const int ExtractProcessTimeoutMs = 300_000;
    private const int ConcatProcessTimeoutMs = 600_000;

    private readonly ILogService _log;
    private readonly ISettingsService _settingsService;
    private bool _isValid;

    public bool IsValid => _isValid;

    public event EventHandler<double>? OnClippingProgress;
    public event EventHandler<ClippingCompletedEventArgs>? OnClippingComplete;

    /// <summary>初始化 <see cref="FfmpegService"/> 类的新实例。</summary>
    public FfmpegService(ILogService log, ISettingsService settingsService)
    {
        _log = log;
        _settingsService = settingsService;
    }

    public bool ValidatePath(string ffmpegPath)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            _isValid = false;
            return false;
        }

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };

            process.Start();
            process.WaitForExit(10_000);

            var success = process.ExitCode == 0;
            _isValid = success;
            _log.Log(LogCategory.Ffmpeg, success
                ? $"FFmpeg 路径校验通过: {ffmpegPath}"
                : $"FFmpeg 路径校验失败（退出码 {process.ExitCode}）: {ffmpegPath}");
            return success;
        }
        catch (Exception ex)
        {
            _isValid = false;
            _log.Log(LogCategory.Ffmpeg, $"FFmpeg 路径校验失败: {ex.Message}");
            return false;
        }
    }

    public async Task ClipAndConcatAsync(
        string inputFile,
        string outputFile,
        IReadOnlyList<ClipSegment> clips,
        string ffmpegPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inputFile))
            throw new ArgumentException("Input file path cannot be empty.", nameof(inputFile));
        if (string.IsNullOrWhiteSpace(outputFile))
            throw new ArgumentException("Output file path cannot be empty.", nameof(outputFile));
        if (clips is null || clips.Count == 0)
            throw new ArgumentException("At least one clip segment must be provided.", nameof(clips));
        if (string.IsNullOrWhiteSpace(ffmpegPath))
            throw new ArgumentException("FFmpeg path cannot be empty.", nameof(ffmpegPath));
        if (!File.Exists(inputFile))
            throw new FileNotFoundException("Input video file not found.", inputFile);
        if (!File.Exists(ffmpegPath))
            throw new FileNotFoundException("FFmpeg executable not found.", ffmpegPath);

        var replayOutputPath = string.IsNullOrWhiteSpace(_settingsService.ReplayOutputPath)
            ? Path.Combine(Path.GetTempPath(), "CSDirectorTool")
            : _settingsService.ReplayOutputPath;
        var tempDir = Path.Combine(replayOutputPath, "FfmpegClips", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        _log.Log(LogCategory.Ffmpeg,
            $"开始剪辑: {clips.Count} 个片段, 输入 {Path.GetFileName(inputFile)} -> 输出 {Path.GetFileName(outputFile)}");

        var tempClipFiles = new List<string>();
        string? concatFileList = null;

        try
        {
            double totalDuration = clips.Sum(c => c.Duration);
            var tasks = new List<Task>();

            for (var i = 0; i < clips.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var clip = clips[i];
                var tempClipFile = Path.Combine(tempDir, $"clip_{i:D4}.ts");
                tempClipFiles.Add(tempClipFile);

                var arguments = $"-ss {clip.StartTime.ToString(CultureInfo.InvariantCulture)} -i \"{inputFile}\" -t {clip.Duration.ToString(CultureInfo.InvariantCulture)} -c:v libx264 -preset veryfast -c:a aac -y \"{tempClipFile}\"";
                var clipIndex = i;

                tasks.Add(Task.Run(async () =>
                {
                    await RunFfmpegProcessAsync(
                        ffmpegPath,
                        arguments,
                        "clip extraction",
                        cancellationToken,
                        ExtractProcessTimeoutMs,
                        (line) =>
                        {
                            var progress = ParseFfmpegProgress(line, clip.Duration);
                            if (progress.HasValue)
                            {
                                var overallProgress = (clipIndex * clip.Duration + progress.Value * clip.Duration) / totalDuration * 100.0;
                                Dispatcher.UIThread.Post(() =>
                                    OnClippingProgress?.Invoke(this, Math.Min(overallProgress, 100.0)));
                            }
                        });

                    _log.Log(LogCategory.Ffmpeg,
                        $"片段 {clipIndex + 1}/{clips.Count} 提取完成: {Path.GetFileName(tempClipFile)} (开始 {clip.StartTime:0.###}s, 时长 {clip.Duration:0.###}s)");

                    var clipProgress = (double)(clipIndex + 1) / clips.Count * 100.0;
                    Dispatcher.UIThread.Post(() => OnClippingProgress?.Invoke(this, clipProgress));
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);

            cancellationToken.ThrowIfCancellationRequested();
            concatFileList = Path.Combine(tempDir, "concat_list.txt");
            var concatContent = new StringBuilder();
            foreach (var clipFile in tempClipFiles)
                concatContent.AppendLine($"file '{clipFile}'");
            File.WriteAllText(concatFileList, concatContent.ToString());

            cancellationToken.ThrowIfCancellationRequested();
            var concatArgs = $"-f concat -safe 0 -i \"{concatFileList}\" -c copy -y \"{outputFile}\"";

            await RunFfmpegProcessAsync(
                ffmpegPath,
                concatArgs,
                "concatenation",
                cancellationToken,
                ConcatProcessTimeoutMs,
                null);

            var actualDuration = await GetVideoDurationAsync(ffmpegPath, outputFile, cancellationToken);
            if (actualDuration <= TimeSpan.Zero)
                actualDuration = TimeSpan.FromSeconds(totalDuration);

            _log.Log(LogCategory.Ffmpeg,
                $"剪辑完成: {outputFile} (时长 {actualDuration:hh\\:mm\\:ss})");

            Dispatcher.UIThread.Post(() =>
                OnClippingComplete?.Invoke(this, new ClippingCompletedEventArgs(outputFile, actualDuration)));
        }
        finally
        {
            CleanupTempFiles(tempClipFiles);
            if (concatFileList is not null && File.Exists(concatFileList))
            {
                try { File.Delete(concatFileList); } catch { }
            }

            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch { }
        }
    }

    private async Task RunFfmpegProcessAsync(
        string ffmpegPath,
        string arguments,
        string operationName,
        CancellationToken cancellationToken,
        int timeoutMs,
        Action<string>? onStderrLine)
    {
        await Task.Run(() =>
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                },
                EnableRaisingEvents = true,
            };

            var stderrBuffer = new StringBuilder();
            var processExited = new ManualResetEventSlim(false);

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    stderrBuffer.AppendLine(e.Data);
                    onStderrLine?.Invoke(e.Data);
                }
            };

            process.Exited += (_, _) => processExited.Set();

            try
            {
                process.Start();
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();

                var waitResult = WaitHandle.WaitAny(
                    new[] { processExited.WaitHandle, cancellationToken.WaitHandle },
                    timeoutMs);

                if (cancellationToken.IsCancellationRequested)
                {
                    try { process.Kill(); } catch { }
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (waitResult == WaitHandle.WaitTimeout)
                {
                    try { process.Kill(); } catch { }
                    _log.Log(LogCategory.Ffmpeg, $"FFmpeg {operationName} 超时（{timeoutMs / 1000} 秒）");
                    throw new TimeoutException(
                        $"FFmpeg {operationName} timed out after {timeoutMs / 1000} seconds.");
                }

                process.WaitForExit(5000);

                if (process.ExitCode != 0)
                {
                    var error = stderrBuffer.ToString();
                    _log.Log(LogCategory.Ffmpeg, $"FFmpeg {operationName} 失败（退出码 {process.ExitCode}）: {error}");
                    throw new InvalidOperationException(
                        $"FFmpeg {operationName} failed with exit code {process.ExitCode}.{Environment.NewLine}{error}");
                }
            }
            finally
            {
                if (!process.HasExited)
                {
                    try { process.Kill(); } catch { }
                }
            }
        }, cancellationToken);
    }

    private static double? ParseFfmpegProgress(string line, double segmentDuration)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var match = Regex.Match(line, @"time=(\d{2}):(\d{2}):(\d{2}\.\d+)");
        if (!match.Success)
            return null;

        if (!int.TryParse(match.Groups[1].Value, out var hours))
            return null;
        if (!int.TryParse(match.Groups[2].Value, out var minutes))
            return null;
        if (!double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return null;

        var currentTime = hours * 3600 + minutes * 60 + seconds;
        return segmentDuration <= 0 ? 0 : Math.Min(currentTime / segmentDuration, 1.0);
    }

    private static async Task<TimeSpan> GetVideoDurationAsync(string ffmpegPath, string videoFile,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() =>
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = $"-i \"{videoFile}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                    },
                    EnableRaisingEvents = true,
                };

                var stderrBuffer = new StringBuilder();
                var processExited = new ManualResetEventSlim(false);

                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data is not null)
                        stderrBuffer.AppendLine(e.Data);
                };
                process.Exited += (_, _) => processExited.Set();

                try
                {
                    process.Start();
                    process.BeginErrorReadLine();
                    process.BeginOutputReadLine();
                    processExited.Wait(30_000);
                    process.WaitForExit(5000);
                }
                catch
                {
                    return TimeSpan.Zero;
                }

                var match = Regex.Match(stderrBuffer.ToString(), @"Duration:\s*(\d+):(\d{2}):(\d{2}(?:\.\d+)?)");
                if (!match.Success)
                    return TimeSpan.Zero;

                var hours = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var minutes = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                var seconds = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                return TimeSpan.FromSeconds(hours * 3600 + minutes * 60 + seconds);
            }, cancellationToken);
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    private static void CleanupTempFiles(List<string> tempFiles)
    {
        foreach (var file in tempFiles)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch { }
        }
    }
}
