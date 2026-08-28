using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CS2_Director_Tool.App.Models;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 击杀回放工作流的实现。维护跨回合的录制状态机，供事件动作系统调用。
/// </summary>
public class ReplayWorkflowService : IReplayWorkflowService
{
    private readonly ISettingsService _settingsService;
    private readonly IObsService _obsService;
    private readonly IFfmpegService _ffmpegService;
    private readonly ICs2InstallService _cs2InstallService;
    private readonly ILogService _log;

    private bool _canBeActive;
    private bool _isInRound;
    private bool _recordingActive;
    private DateTime _recordingStartTime;
    private bool _isReplayPlaying;
    private bool _isRoundEnding;
    private readonly List<double> _killTimestamps = new();
    private readonly object _killLock = new();

    public bool IsReplayPlaying => _isReplayPlaying;
    public bool PrerequisitesMet => _canBeActive;

    /// <summary>初始化 <see cref="ReplayWorkflowService"/> 类的新实例。</summary>
    public ReplayWorkflowService(ISettingsService settingsService, IObsService obsService,
        IFfmpegService ffmpegService, ICs2InstallService cs2InstallService, ILogService log)
    {
        _settingsService = settingsService;
        _obsService = obsService;
        _ffmpegService = ffmpegService;
        _cs2InstallService = cs2InstallService;
        _log = log;

        _obsService.OnConnected += (s, e) =>
        {
            _log.Log(LogCategory.Replay, "OBS 已连接");
            RefreshPrerequisites();
        };
        _obsService.OnDisconnected += (s, e) =>
        {
            _log.Log(LogCategory.Replay, "OBS 已断开，回放状态已复位");
            OnObsDisconnected();
            RefreshPrerequisites();
        };

        _ffmpegService.OnClippingComplete += ClippingCompleteHandler;

        RefreshPrerequisites();
    }

    private async void ClippingCompleteHandler(object? sender, ClippingCompletedEventArgs e)
    {
        if (!_isRoundEnding || _isReplayPlaying)
            return;

        try
        {
            if (e is null || string.IsNullOrEmpty(e.FilePath) || !File.Exists(e.FilePath))
            {
                _log.Log(LogCategory.Replay, "错误: 剪辑文件不存在");
                return;
            }

            _log.Log(LogCategory.Replay, "剪辑完成，开始播放回放");
            await PlayReplayAsync(e.FilePath, e.Duration);
        }
        catch (Exception ex)
        {
            _log.Log(LogCategory.Replay, $"回放失败: {ex.Message}");
        }
        finally
        {
            _isRoundEnding = false;
        }
    }

    /// <inheritdoc/>
    public void RefreshPrerequisites()
    {
        bool met = CheckPrerequisitesMet();
        if (met == _canBeActive)
            return;
        _canBeActive = met;
        _log.Log(LogCategory.Replay, met ? "击杀回放前置条件已满足" : "击杀回放前置条件不满足（需 OBS/FFmpeg/GSI）");
        if (!met)
            OnObsDisconnected();
    }

    private bool CheckPrerequisitesMet()
    {
        return _cs2InstallService != null
            && !string.IsNullOrEmpty(_settingsService.Cs2Path)
            && _cs2InstallService.IsGsiConfigInstalled(_settingsService.Cs2Path)
            && !string.IsNullOrEmpty(_settingsService.FfmpegPath)
            && _obsService.IsConnected;
    }

    private void OnObsDisconnected()
    {
        _isInRound = false;
        _recordingActive = false;
        _isReplayPlaying = false;
        _isRoundEnding = false;
        lock (_killLock) { _killTimestamps.Clear(); }
    }

    /// <inheritdoc/>
    public async Task<bool> StartRecordingAsync()
    {
        if (!_canBeActive)
        {
            _log.Log(LogCategory.Replay, "回合开始：前置条件不满足，跳过录制");
            return false;
        }

        // freezetime -> live 会触发两次本方法；一个回合只初始化一次，
        // 防止中途重启录制并清空/错位击杀时间戳。
        if (_isInRound)
        {
            _log.Log(LogCategory.Replay, "回合开始事件已处理过，跳过本次触发");
            return false;
        }

        _isInRound = true;
        lock (_killLock) { _killTimestamps.Clear(); }
        _log.Log(LogCategory.Replay, "回合开始事件触发，准备录制");

        try
        {
            if (_isReplayPlaying || _isRoundEnding)
            {
                _log.Log(LogCategory.Replay, "回放播放中或上一回合收尾中，本回合推迟录制");
                return false;
            }

            string gameScene = string.IsNullOrEmpty(_settingsService.GameSceneName) ? "Game" : _settingsService.GameSceneName;
            await _obsService.SwitchToSceneAsync(gameScene);
            _log.Log(LogCategory.Replay, $"已切换到游戏场景 '{gameScene}'");

            await RestartRecordingAsync();
            lock (_killLock)
            {
                _recordingStartTime = DateTime.Now;
                _recordingActive = true;
            }
            _log.Log(LogCategory.Replay, "录制已确认开启");
            return true;
        }
        catch (Exception ex)
        {
            _log.Log(LogCategory.Replay, $"开始录制失败: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public Task RecordKillPointAsync()
    {
        if (!_canBeActive || !_isInRound || !_recordingActive)
            return Task.CompletedTask;

        lock (_killLock)
        {
            if (_recordingStartTime == default)
                return Task.CompletedTask;
            double killTimeInRecording = (DateTime.Now - _recordingStartTime).TotalSeconds;
            _killTimestamps.Add(killTimeInRecording);
            _log.Log(LogCategory.Replay, $"击杀点已记录（录制内 {killTimeInRecording:F1}s）");
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task GenerateReplayAsync()
    {
        // map.round 递增与 round.phase=over 都可能触发本方法；只处理一次。
        if (!_isInRound || !_canBeActive || _isRoundEnding)
            return;

        _isInRound = false;
        _isRoundEnding = true;
        _log.Log(LogCategory.Replay, "回合结束事件触发");

        try
        {
            double[] timestamps;
            lock (_killLock)
            {
                timestamps = _killTimestamps.ToArray();
            }

            if (timestamps.Length == 0)
            {
                _log.Log(LogCategory.Replay, "回合结束，无击杀镜头");
                _isRoundEnding = false;
                return;
            }

            _log.Log(LogCategory.Replay, $"回合结束，处理 {timestamps.Length} 个击杀镜头");
            double wait = 0;
            if (_recordingStartTime != default)
            {
                double lastKillInRecording = timestamps[timestamps.Length - 1];
                double elapsedSinceLastKill = (DateTime.Now - _recordingStartTime).TotalSeconds - lastKillInRecording;
                if (elapsedSinceLastKill < 1.0)
                    wait = 1.0 - elapsedSinceLastKill + 0.3;
            }
            if (wait > 0)
            {
                _log.Log(LogCategory.Replay, $"回合结束，最后击杀后仅 {wait - 0.3:F2}s，等待 {wait:F2}s 确保保留击杀后画面");
                await Task.Delay(TimeSpan.FromSeconds(wait));
            }

            string recordingPath;
            try
            {
                recordingPath = await _obsService.StopRecordingAsync();
            }
            catch (Exception ex)
            {
                _log.Log(LogCategory.Replay, $"停止录制失败: {ex.Message}");
                _isRoundEnding = false;
                return;
            }
            _recordingActive = false;
            _log.Log(LogCategory.Replay, $"已停止录制: {recordingPath}");

            if (string.IsNullOrEmpty(recordingPath) || !File.Exists(recordingPath))
            {
                _log.Log(LogCategory.Replay, "错误: 找不到录制文件");
                _isRoundEnding = false;
                return;
            }

            var clips = new List<ClipSegment>();
            for (int i = 0; i < timestamps.Length; i++)
            {
                double timestamp = timestamps[i];
                bool isFirstKill = i == 0;
                clips.Add(new ClipSegment
                {
                    StartTime = Math.Max(0, timestamp - (isFirstKill ? 4 : 1)),
                    Duration = isFirstKill ? 5 : 2
                });
            }
            clips = MergeClips(clips);

            string outputDir = Path.Combine(Path.GetTempPath(), "MajoCupDirector");
            Directory.CreateDirectory(outputDir);
            string outputFile = Path.Combine(outputDir, $"replay_{DateTime.Now:yyyyMMddHHmmss}.mp4");

            _log.Log(LogCategory.Replay, $"开始剪辑 {clips.Count} 个片段 -> {outputFile}");
            // 剪辑完成后 FfmpegService 会抛出 OnClippingComplete，由
            // ClippingCompleteHandler 负责播放并在此后清除 _isRoundEnding。
            await _ffmpegService.ClipAndConcatAsync(recordingPath, outputFile, clips,
                _settingsService.FfmpegPath);
        }
        catch (Exception ex)
        {
            _log.Log(LogCategory.Replay, $"处理失败: {ex.Message}");
            _isRoundEnding = false;
        }
    }

    private async Task PlayReplayAsync(string videoFile, TimeSpan duration)
    {
        _isReplayPlaying = true;
        try
        {
            if (await _obsService.IsRecordingActiveAsync())
            {
                await _obsService.StopRecordingAsync();
                _recordingActive = false;
                _log.Log(LogCategory.Replay, "回放播放前已停止录制");
            }

            string replayScene = string.IsNullOrEmpty(_settingsService.ReplaySceneName) ? "Replay" : _settingsService.ReplaySceneName;
            await _obsService.SwitchToSceneAsync(replayScene);
            _log.Log(LogCategory.Replay, $"已切换到回放场景 '{replayScene}'");

            string sourceName = string.IsNullOrEmpty(_settingsService.ReplaySourceName) ? "Replay" : _settingsService.ReplaySourceName;
            await _obsService.CreateReplaySourceAsync(replayScene, sourceName, videoFile);
            _log.Log(LogCategory.Replay, $"已在 '{replayScene}' 中设置源 '{sourceName}': {videoFile}");

            await _obsService.PlayMediaAsync(replayScene, sourceName);
            _log.Log(LogCategory.Replay, "开始播放回放...");

            TimeSpan playbackBudget = duration + TimeSpan.FromSeconds(1);
            await _obsService.WaitForMediaPlaybackEndedAsync(sourceName, playbackBudget);
            _log.Log(LogCategory.Replay, "回放播放结束");

            string gameScene = string.IsNullOrEmpty(_settingsService.GameSceneName) ? "Game" : _settingsService.GameSceneName;
            await _obsService.SwitchToSceneAsync(gameScene);
            _log.Log(LogCategory.Replay, $"已切换回游戏场景 '{gameScene}'");
        }
        finally
        {
            _isReplayPlaying = false;
        }

        try
        {
            await _obsService.StartRecordingAsync();
            _recordingActive = true;
            lock (_killLock) { _recordingStartTime = DateTime.Now; }
            _log.Log(LogCategory.Replay, "回放完成，继续录制");
        }
        catch (Exception ex)
        {
            _log.Log(LogCategory.Replay, $"恢复录制失败: {ex.Message}");
        }
    }

    private async Task RestartRecordingAsync()
    {
        if (await _obsService.IsRecordingActiveAsync())
        {
            await _obsService.StopRecordingAsync();
            _log.Log(LogCategory.Replay, "检测到已在录制，已停止旧录制");
            await Task.Delay(300);
        }
        await _obsService.StartRecordingAsync();
    }

    private static List<ClipSegment> MergeClips(List<ClipSegment> clips)
    {
        if (clips.Count <= 1)
            return clips;

        var sorted = clips.OrderBy(c => c.StartTime).ToList();
        var merged = new List<ClipSegment>();
        var current = sorted[0];

        for (int i = 1; i < sorted.Count; i++)
        {
            var next = sorted[i];
            if (next.StartTime <= current.StartTime + current.Duration)
            {
                double end = Math.Max(current.StartTime + current.Duration, next.StartTime + next.Duration);
                current.Duration = end - current.StartTime;
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }
        merged.Add(current);
        return merged;
    }
}
