using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CS2_Director_Tool.App.Models;
using CS2_Director_Tool.App.Services;

namespace CS2_Director_Tool.App.ViewModels;

/// <summary>
/// 击杀回放页面视图模型，管理 OBS 录制、GSI 击杀事件、FFmpeg 剪辑与 OBS 场景回放。
/// </summary>
public partial class KillReplayViewModel : ViewModelBase
{
    private readonly IGsiService _gsi;
    private readonly IObsService _obs;
    private readonly IFfmpegService _ffmpeg;
    private readonly ISettingsService _settings;

    private string _gameSceneName = string.Empty;
    private string _replaySceneName = string.Empty;
    private string _replaySourceName = "Replay";
    private string? _status;
    private bool _isProcessing;
    private double _progress;
    private bool _isKillReplayEnabled;

    private string? _ffmpegPath;

    private string? _recordingPath;
    private DateTime _clipStartTime;
    private bool _isClipActive;
    private bool _isWaitingForRoundEnd;
    private bool _isFirstKillOfRound = true;
    private string? _previousScene;

    private bool _isReplayPlaying;
    private CancellationTokenSource? _activeReplayCancellation;

    private readonly object _gsiEventLock = new object();
    private readonly List<(GsiKillEventArgs e, DateTime killTime)> _gsiEventBuffer = new();
    private readonly HashSet<string> _clipHistory = new HashSet<string>(StringComparer.Ordinal);

    private readonly List<string> _logBuffer = new();
    private string _logText = string.Empty;

    /// <summary>回放开始播放（用于通知暂停音乐功能暂停音乐）。</summary>
    public bool IsReplayPlaying
    {
        get => _isReplayPlaying;
        private set => SetProperty(ref _isReplayPlaying, value);
    }

    /// <summary>回放播放结束时触发（用于通知暂停音乐功能恢复音乐）。</summary>
    public event EventHandler? ReplayPlaybackEnded;

    public string GameSceneName
    {
        get => _gameSceneName;
        set
        {
            if (SetProperty(ref _gameSceneName, value ?? string.Empty))
                _settings.GameSceneName = _gameSceneName;
        }
    }

    public string ReplaySceneName
    {
        get => _replaySceneName;
        set
        {
            if (SetProperty(ref _replaySceneName, value ?? string.Empty))
                _settings.ReplaySceneName = _replaySceneName;
        }
    }

    public string ReplaySourceName
    {
        get => _replaySourceName;
        set
        {
            if (SetProperty(ref _replaySourceName, value ?? string.Empty))
                _settings.ReplaySourceName = _replaySourceName;
        }
    }

    public string? Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set => SetProperty(ref _isProcessing, value);
    }

    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    public bool IsKillReplayEnabled
    {
        get => _isKillReplayEnabled;
        set
        {
            if (SetProperty(ref _isKillReplayEnabled, value))
            {
                _settings.KillReplayEnabled = value;
                OnIsKillReplayEnabledChanged();
            }
        }
    }

    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

    public ICommand ClearLogCommand { get; }

    public KillReplayViewModel(IGsiService gsi, IObsService obs, IFfmpegService ffmpeg, ISettingsService settings)
    {
        _gsi = gsi;
        _obs = obs;
        _ffmpeg = ffmpeg;
        _settings = settings;

        GameSceneName = _settings.GameSceneName;
        ReplaySceneName = _settings.ReplaySceneName;
        ReplaySourceName = _settings.ReplaySourceName;
        IsKillReplayEnabled = _settings.KillReplayEnabled;
        _ffmpegPath = _settings.FfmpegPath;

        ClearLogCommand = new RelayCommand(() =>
        {
            _logBuffer.Clear();
            LogText = string.Empty;
        });

        _gsi.OnKill += OnGsiKill;
        _gsi.OnRoundStarted += OnRoundStart;
        _gsi.OnRoundEnded += OnRoundEnd;
        _gsi.OnMatchStarted += OnMatchStart;
        _gsi.OnLog += (_, msg) => AppendLog(msg);

        _ffmpeg.OnClippingProgress += (_, p) => Progress = Math.Max(0, Math.Min(100, p));
    }

    private void OnIsKillReplayEnabledChanged()
    {
        if (_isKillReplayEnabled)
            StartReplayFeature();
        else
            StopReplayFeature();
    }

    /// <summary>由 MainViewModel 在前置条件变化（如 OBS 断开）时调用。</summary>
    public void OnPrerequisitesChanged(bool met)
    {
        if (!met && _isKillReplayEnabled)
        {
            IsKillReplayEnabled = false;
            AppendLog("前置条件不再满足，已停止击杀回放功能");
        }
    }

    private void StartReplayFeature()
    {
        if (string.IsNullOrEmpty(_ffmpegPath))
        {
            Status = "错误: 未配置 ffmpeg 路径";
            IsKillReplayEnabled = false;
            return;
        }

        if (_isProcessing)
            return;

        AppendLog("击杀回放功能已启用，正在开始录制...");
        RunObs(async () =>
        {
            await _obs.StartRecordingAsync();
            _clipStartTime = DateTime.Now;
            _recordingPath = await _obs.GetCurrentRecordingPathAsync();
            _isClipActive = true;
            Status = "击杀回放功能已启用，正在录制...";
            AppendLog($"已开始录制: {_recordingPath ?? "(路径未知)"}");
        });
    }

    private void StopReplayFeature()
    {
        if (_isProcessing)
            return;

        CancelActiveReplay();
        if (_isClipActive)
        {
            RunObs(async () =>
            {
                await _obs.StopRecordingAsync();
                _isClipActive = false;
                AppendLog("停止录制（已禁用击杀回放）");
            });
        }

        if (IsReplayPlaying)
            RestoreGameScene();

        Status = "击杀回放功能已禁用";
        IsKillReplayEnabled = false;
    }

    private void OnGsiKill(object? sender, GsiKillEventArgs e)
    {
        if (!_isKillReplayEnabled)
            return;
        if (!e.IsObserverOnKiller)
            return;
        if (ContainsClip(e.KillerName, e.VictimName))
            return;

        var killTime = DateTime.Now;
        lock (_gsiEventLock)
        {
            _gsiEventBuffer.Add((e, killTime));
        }

        if (_isProcessing)
            return;

        ProcessNextClip();
    }

    private bool ContainsClip(string killerName, string victimName)
    {
        var key = $"{killerName}->{victimName}";
        if (_clipHistory.Contains(key))
            return true;
        _clipHistory.Add(key);
        return false;
    }

    private void ProcessNextClip()
    {
        if (_isProcessing)
            return;
        if (_gsiEventBuffer.Count == 0)
            return;
        if (!_isClipActive)
        {
            Status = "录制尚未开始，无法生成回放";
            return;
        }

        _isProcessing = true;
        Progress = 0;

        (GsiKillEventArgs e, DateTime killTime) item;
        lock (_gsiEventLock)
        {
            item = _gsiEventBuffer[0];
            _gsiEventBuffer.RemoveAt(0);
        }

        CalculateClipAndReplay(item.killTime, out var clipStart, out var clipDuration, out var replayStart);

        RunObs(async () =>
        {
            var recordingPath = await _obs.GetCurrentRecordingPathAsync();
            if (string.IsNullOrEmpty(recordingPath) || !File.Exists(recordingPath))
            {
                AppendLog("错误: 无法获取当前录制文件路径，跳过本次回放");
                _isProcessing = false;
                return;
            }

            var outputDir = Path.GetDirectoryName(recordingPath) ?? ".";
            var outputFile = Path.Combine(outputDir, $"kill_replay_{DateTime.Now:yyyyMMdd_HHmmssfff}.mp4");
            var clips = new List<ClipSegment>
            {
                new ClipSegment { StartTime = clipStart, Duration = clipDuration }
            };

            _activeReplayCancellation = new CancellationTokenSource();
            var token = _activeReplayCancellation.Token;

            try
            {
                await _ffmpeg.ClipAndConcatAsync(recordingPath, outputFile, clips, _ffmpegPath!, token);
            }
            catch (Exception ex)
            {
                AppendLog($"剪辑失败: {ex.Message}");
                _isProcessing = false;
                return;
            }

            AppendLog($"剪辑完成: {outputFile}");
            OnClipCompleted(outputFile, replayStart, clipDuration, item.e);
        });
    }

    private void CalculateClipAndReplay(DateTime killTime, out double clipStart, out double clipDuration,
        out double replayStart)
    {
        var killOffset = (killTime - _clipStartTime).TotalSeconds;
        if (killOffset < 0)
            killOffset = 0;

        if (_isFirstKillOfRound)
        {
            // 首次击杀：击杀前 4 秒 + 击杀后 1 秒，共 5 秒。
            clipStart = Math.Max(0, killOffset - 4);
            clipDuration = (killOffset - clipStart) + 1;
            replayStart = clipStart + clipDuration - 1;
            _isFirstKillOfRound = false;
        }
        else
        {
            // 其余击杀：前后各 1 秒，共 2 秒。
            clipStart = Math.Max(0, killOffset - 1);
            clipDuration = 2;
            replayStart = clipStart + 1;
        }
    }

    private void OnClipCompleted(string filePath, double replayStart, double clipDuration, GsiKillEventArgs e)
    {
        try
        {
            AppendLog($"击杀回放: {e.KillerName} 击杀 {e.VictimName}（高光起点 {replayStart:F2}s）");
            RunObs(async () =>
            {
                var prev = await _obs.GetCurrentSceneNameAsync();
                _previousScene = prev ?? GameSceneName;

                await _obs.CreateReplaySourceAsync(ReplaySceneName, ReplaySourceName, filePath);
                await _obs.SwitchToSceneAsync(ReplaySceneName);
                await _obs.SeekMediaInputAsync(ReplaySourceName, (long)(replayStart * 1000));
                await _obs.PlayMediaAsync(ReplaySceneName, ReplaySourceName);

                IsReplayPlaying = true;

                await _obs.WaitForMediaPlaybackEndedAsync(ReplaySourceName, TimeSpan.FromSeconds(clipDuration));
                RestoreGameScene();
            });
        }
        catch (Exception ex)
        {
            AppendLog($"回放失败: {ex.Message}");
            _isProcessing = false;
            IsReplayPlaying = false;
        }
    }

    private void RestoreGameScene()
    {
        try
        {
            if (!string.IsNullOrEmpty(_previousScene))
                RunObs(() => _obs.SwitchToSceneAsync(_previousScene!));
        }
        catch (Exception ex)
        {
            AppendLog($"恢复场景失败: {ex.Message}");
        }
        finally
        {
            IsReplayPlaying = false;
            ReplayPlaybackEnded?.Invoke(this, EventArgs.Empty);
            _isProcessing = false;
            Progress = 0;
        }
    }

    private void CancelActiveReplay()
    {
        _activeReplayCancellation?.Cancel();
        lock (_gsiEventLock)
        {
            _gsiEventBuffer.Clear();
        }
        _isFirstKillOfRound = true;
    }

    private void OnRoundStart(object? sender, EventArgs e)
    {
        _isFirstKillOfRound = true;
        _isWaitingForRoundEnd = false;
        _clipHistory.Clear();
        if (_isKillReplayEnabled && !_isProcessing)
            Status = "等待击杀...";
    }

    private void OnRoundEnd(object? sender, EventArgs e)
    {
        _isWaitingForRoundEnd = true;
        CancelActiveReplay();
        if (IsReplayPlaying)
            RestoreGameScene();
    }

    private void OnMatchStart(object? sender, EventArgs e)
    {
        _isFirstKillOfRound = true;
        _clipHistory.Clear();
        if (_isKillReplayEnabled && !_isProcessing)
            Status = "等待击杀...";
    }

    private void RunObs(Func<Task> action)
    {
        Task.Run(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                AppendLog($"OBS 操作失败: {ex.Message}");
            }
        });
    }

    private void AppendLog(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        Dispatcher.UIThread.Post(() =>
        {
            _logBuffer.Add(line);
            while (_logBuffer.Count > 1000)
                _logBuffer.RemoveAt(0);

            LogText = string.Join(Environment.NewLine, _logBuffer);
        });
    }
}
