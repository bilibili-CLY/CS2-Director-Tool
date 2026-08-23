using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using CS2_Director_Tool.App.Models;
using CS2_Director_Tool.App.Services;

namespace CS2_Director_Tool.App.ViewModels
{
    /// <summary>
    /// 击杀回放页面视图模型，管理场景配置与回放流程。
    /// </summary>
    public partial class KillReplayViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IGsiService _gsiService;
        private readonly IObsService _obsService;
        private readonly IFfmpegService _ffmpegService;
        private readonly ICs2InstallService _cs2InstallService;
        private string _gameSceneName;
        private string _replaySceneName;
        private string _replaySourceName;
        private string _status;
        private bool _isRecording;
        private bool _isProcessing;
        private bool _isObsConnected;
        private bool _ffmpegValid;
        private double _progress;
        private bool _isKillReplayEnabled;
        private bool _canBeActive;

        // 击杀追踪
        private readonly List<double> _killTimestamps = new List<double>();
        private DateTime _roundStartTime;

        /// <summary>
        /// 保护击杀时间戳记录与录制会话状态。
        /// </summary>
        private readonly object _killLock = new object();

        /// <summary>
        /// 指示录制会话是否处于活动状态；只有活动会话内的击杀才会被记录。
        /// </summary>
        private bool _recordingActive;

        private DateTime _recordingStartTime;
        private bool _isInRound;

        // 回放协调
        private bool _isReplayPlaying;
        private bool _isRoundEnding;

        public string GameSceneName
        {
            get => _gameSceneName;
            set
            {
                if (SetProperty(ref _gameSceneName, value))
                    _settingsService.GameSceneName = value;
            }
        }

        public string ReplaySceneName
        {
            get => _replaySceneName;
            set
            {
                if (SetProperty(ref _replaySceneName, value))
                    _settingsService.ReplaySceneName = value;
            }
        }

        /// <summary>
        /// 获取或设置用于回放播放的媒体源名称。
        /// </summary>
        public string ReplaySourceName
        {
            get => _replaySourceName;
            set
            {
                if (SetProperty(ref _replaySourceName, value))
                    _settingsService.ReplaySourceName = value;
            }
        }

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public bool IsRecording
        {
            get => _isRecording;
            set => SetProperty(ref _isRecording, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set => SetProperty(ref _isProcessing, value);
        }

        public bool IsObsConnected
        {
            get => _isObsConnected;
            set => SetProperty(ref _isObsConnected, value);
        }

        public bool FfmpegValid
        {
            get => _ffmpegValid;
            set => SetProperty(ref _ffmpegValid, value);
        }

        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        /// <summary>
        /// 获取或设置是否启用击杀回放功能。
        /// </summary>
        public bool IsKillReplayEnabled
        {
            get => _isKillReplayEnabled;
            set
            {
                if (SetProperty(ref _isKillReplayEnabled, value))
                {
                    _settingsService.KillReplayEnabled = value;
                    if (value && !_canBeActive)
                        AppendLog("已勾选启用击杀回放，但前置条件未满足，未真正启用");
                    else
                        AppendLog(value ? "已启用击杀回放" : "已取消启用击杀回放");
                    if (!value)
                        StopKillReplayAsync();
                }
            }
        }

        /// <summary>
        /// 获取一个值，指示击杀回放功能既被用户启用，
        /// 又被其前提条件允许运行。
        /// </summary>
        private bool IsEffectivelyEnabled => _isKillReplayEnabled && _canBeActive;

        /// <summary>
        /// 获取一个值，指示当前是否有回放正在播放。
        /// </summary>
        public bool IsReplayPlaying => _isReplayPlaying;

        /// <summary>
        /// 回放播放结束且游戏场景重新激活时发生。
        /// </summary>
        public event Action? ReplayPlaybackEnded;

        /// <summary>
        /// 功能被停用时停止当前录制并重置回放状态。
        /// </summary>
        private async void StopKillReplayAsync()
        {
            _isInRound = false;
            _killTimestamps.Clear();
            if (!IsRecording)
                return;
            try
            {
                await _obsService.StopRecordingAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"停止录制失败: {ex.Message}");
            }
            lock (_killLock) { _recordingActive = false; }
            IsRecording = false;
            Status = "击杀回放已停用";
        }

        private readonly List<string> _logBuffer = new List<string>();
        private string _logText = string.Empty;

        /// <summary>
        /// 获取页面控制台区域显示的诊断日志文本。
        /// </summary>
        public string LogText
        {
            get => _logText;
            private set => SetProperty(ref _logText, value);
        }

        /// <summary>
        /// 获取清空日志控制台的命令。
        /// </summary>
        public IRelayCommand ClearLogCommand { get; }

        /// <summary>
        /// 向日志控制台追加一条带时间戳的日志行。可从任意线程调用；
        /// 文本只在 UI 线程上修改。
        /// </summary>
        /// <param name="message">日志消息。</param>
        private void AppendLog(string message)
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
            Dispatcher.UIThread.Post(() =>
            {
                _logBuffer.Add(line);
                while (_logBuffer.Count > 1000)
                    _logBuffer.RemoveAt(0);

                LogText = string.Join(Environment.NewLine, _logBuffer);
            });
        }

        /// <summary>
        /// 初始化 <see cref="KillReplayViewModel"/> 类的新实例。
        /// </summary>
        public KillReplayViewModel(ISettingsService settingsService, IGsiService gsiService,
            IObsService obsService, IFfmpegService ffmpegService, ICs2InstallService cs2InstallService)
        {
            _settingsService = settingsService;
            _gsiService = gsiService;
            _obsService = obsService;
            _ffmpegService = ffmpegService;
            _cs2InstallService = cs2InstallService;

            GameSceneName = settingsService.GameSceneName;
            ReplaySceneName = settingsService.ReplaySceneName;
            ReplaySourceName = settingsService.ReplaySourceName;
            FfmpegValid = ffmpegService.ValidatePath(settingsService.FfmpegPath);
            _isKillReplayEnabled = settingsService.KillReplayEnabled;

            ClearLogCommand = new RelayCommand(() =>
            {
                _logBuffer.Clear();
                LogText = string.Empty;
            });

            // 订阅 GSI 事件
            _gsiService.OnKill += OnKill;
            _gsiService.OnRoundStarted += async (s, e) => await OnRoundStarted();
            _gsiService.OnMatchStarted += (s, e) => OnMatchStarted();
            _gsiService.OnRoundEnded += async (s, e) => await OnRoundEnded();
            _gsiService.OnLog += (s, line) => AppendLog(line);

            // 订阅 OBS 事件
            _obsService.OnConnected += (s, e) =>
            {
                IsObsConnected = true;
                AppendLog("OBS 已连接");
                RefreshPrerequisites();
            };
            _obsService.OnDisconnected += (s, e) =>
            {
                IsObsConnected = false;
                AppendLog("OBS 已断开");
                RefreshPrerequisites();
            };
            _obsService.OnLog += (s, message) => AppendLog(message);

            // 订阅 FFmpeg 事件
            _ffmpegService.OnClippingProgress += (s, progress) => Progress = progress;
            _ffmpegService.OnClippingComplete += async (s, e) => await OnClippingComplete(e);

            RefreshPrerequisites();
        }

        /// <summary>
        /// 刷新前提条件状态，并在保留用户复选框偏好的前提下，
        /// 相应地启动或暂停击杀回放流程。
        /// </summary>
        public void RefreshPrerequisites()
        {
            bool prerequisitesMet = CheckPrerequisitesMet();
            if (prerequisitesMet == _canBeActive)
                return;

            _canBeActive = prerequisitesMet;
            if (prerequisitesMet)
                AppendLog(_isKillReplayEnabled ? "前置条件已满足，击杀回放已恢复" : "前置条件已满足");
            else
            {
                AppendLog("前置条件不满足，击杀回放已暂停（勾选状态保留，条件满足后自动恢复）");
                StopKillReplayAsync();
            }
        }

        private bool CheckPrerequisitesMet()
        {
            return _cs2InstallService != null
                && !string.IsNullOrEmpty(_settingsService.Cs2Path)
                && _cs2InstallService.IsGsiConfigInstalled(_settingsService.Cs2Path)
                && !string.IsNullOrEmpty(_settingsService.FfmpegPath)
                && _obsService.IsConnected;
        }

        private void OnKill(object? sender, GsiKillEventArgs e)
        {
            if (!IsEffectivelyEnabled || !_isInRound || !e.IsObserverOnKiller)
                return;

            double secondsSinceRoundStart;
            double killTimeInRecording;
            lock (_killLock)
            {
                if (!_recordingActive || _recordingStartTime == default || e.KillDetectedAt < _recordingStartTime)
                {
                    AppendLog($"击杀 {e.KillerName}->{e.VictimName} 被跳过：录制未开始或不在当前录制会话内");
                    return;
                }

                secondsSinceRoundStart = (e.KillDetectedAt - _roundStartTime).TotalSeconds;
                killTimeInRecording = (e.KillDetectedAt - _recordingStartTime).TotalSeconds;
                _killTimestamps.Add(killTimeInRecording);
            }

            string observerName = string.IsNullOrEmpty(e.ObserverTargetName) ? "未知" : e.ObserverTargetName;
            Status = $"击杀: {e.KillerName} 击杀 {e.VictimName} (时间: {secondsSinceRoundStart:F1}s)";
            AppendLog($"击杀: {e.KillerName} 击杀 {e.VictimName}（回合内 {secondsSinceRoundStart:F1}s，视角在: {observerName}）");
        }

        private void OnMatchStarted()
        {
            if (!IsEffectivelyEnabled)
                return;

            _isInRound = false;
            _killTimestamps.Clear();
            Status = "已进入对局，等待回合开始...";
            AppendLog("对局开始（map.phase=live）");
        }

        private async Task OnRoundStarted()
        {
            if (!IsEffectivelyEnabled)
                return;

            // freezetime -> live 的切换会触发两次本方法；一个回合只需要
            // 初始化一次。
            if (_isInRound)
                return;

            _isInRound = true;
            lock (_killLock) { _killTimestamps.Clear(); }
            _roundStartTime = DateTime.Now;
            AppendLog("回合开始事件触发");

            try
            {
                // 当回放仍在播放或上一回合仍在收尾时，保持当前场景和录制不变，
                // 这样播放永远不会被打断，旧录制也永远不会被截断；
                // 场景切换与录制的启动会被推迟到回放流程/回合收尾完成后进行。
                if (_isReplayPlaying || _isRoundEnding)
                {
                    AppendLog("回放播放中或上一回合收尾中，本回合不切换场景、不开始录制，待处理完成后继续");
                }
                else
                {
                    string gameScene = string.IsNullOrEmpty(GameSceneName) ? "Game" : GameSceneName;
                    await _obsService.SwitchToSceneAsync(gameScene);
                    AppendLog($"已切换到游戏场景 '{gameScene}'");

                    await RestartRecordingAsync();
                    lock (_killLock)
                    {
                        _recordingStartTime = DateTime.Now;
                        _recordingActive = true;
                    }
                    AppendLog("录制已确认开启");
                }
            }
            catch (Exception ex)
            {
                Status = $"开始录制失败: {ex.Message}";
                AppendLog($"开始录制失败: {ex.Message}");
                return;
            }

            Status = "新回合开始，等待击杀...";
        }

        /// <summary>
        /// 启动 OBS 录制（若尚未在录制）。
        /// </summary>
        private async Task EnsureRecordingAsync()
        {
            if (!(await _obsService.IsRecordingActiveAsync()))
            {
                await _obsService.StartRecordingAsync();
                IsRecording = true;
            }
        }

        /// <summary>
        /// 重启 OBS 录制：先停止任何正在进行的录制，再开始一段全新的录制，
        /// 以确保从干净的状态开始捕获新回合。
        /// </summary>
        private async Task RestartRecordingAsync()
        {
            if (await _obsService.IsRecordingActiveAsync())
            {
                await _obsService.StopRecordingAsync();
                IsRecording = false;
                AppendLog("检测到已在录制，已停止旧录制");
                await Task.Delay(300);
            }
            await _obsService.StartRecordingAsync();
            IsRecording = true;
        }

        private async Task OnRoundEnded()
        {
            if (!_isInRound || !IsEffectivelyEnabled)
                return;

            _isInRound = false;
            _isRoundEnding = true;
            AppendLog("回合结束事件触发");

            try
            {
                // 在任何 await 之前对时间戳做快照 —— 处理本回合期间，
                // 下一回合可能已经开始并清空列表。
                double[] timestamps;
                lock (_killLock)
                {
                    timestamps = _killTimestamps.ToArray();
                }

                if (timestamps.Length == 0)
                {
                    Status = "回合结束，无击杀镜头";
                    AppendLog("回合结束，无击杀镜头");
                    return;
                }

                Status = $"回合结束，处理 {timestamps.Length} 个击杀镜头...";
                AppendLog($"回合结束，处理 {timestamps.Length} 个击杀镜头");
                IsProcessing = true;

                // 如果最后一次击杀发生在回合结束前 1 秒内，先等待片刻再停止录制，
                // 使文件仍保留剪辑所期望的击杀后约 1 秒的画面；否则片段会超出
                // 文件末尾而被截断。
                if (_recordingStartTime != default)
                {
                    double lastKillInRecording = timestamps[timestamps.Length - 1];
                    double elapsedSinceLastKill = (DateTime.Now - _recordingStartTime).TotalSeconds - lastKillInRecording;
                    if (elapsedSinceLastKill < 1.0)
                    {
                        double wait = 1.0 - elapsedSinceLastKill + 0.3; // 补足到 1 秒画面再加 0.3 秒缓冲
                        AppendLog($"回合结束，最后击杀后仅 {elapsedSinceLastKill:F2}s，等待 {wait:F2}s 确保保留击杀后画面");
                        await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, wait)));
                    }
                }

                try
                {
                    // 停止录制。如果没有正在进行的录制（例如本回合录制启动失败），
                    // 则清晰地报告而不是崩溃。
                    string recordingPath;
                    try
                    {
                        recordingPath = await _obsService.StopRecordingAsync();
                    }
                    catch (Exception ex)
                    {
                        Status = $"停止录制失败: {ex.Message}";
                        AppendLog($"停止录制失败: {ex.Message}");
                        IsProcessing = false;
                        return;
                    }
                    IsRecording = false;
                    lock (_killLock) { _recordingActive = false; }
                    AppendLog($"已停止录制: {recordingPath}");

                    if (string.IsNullOrEmpty(recordingPath) || !File.Exists(recordingPath))
                    {
                        Status = "错误: 找不到录制文件";
                        AppendLog("错误: 找不到录制文件");
                        IsProcessing = false;
                        return;
                    }

                    // 构建剪辑片段（每次击杀前 1 秒到后 1 秒 = 2 秒时长；
                    // 回合内第一次击杀前导 4 秒，共 5 秒）。
                    // 时间戳相对于录制时间轴，其零点是 OBS 实际开始捕获的时刻
                    // （记录在 OnRoundStarted / OnClippingComplete 中），因此
                    // 从 timestamp - offset 开始的片段正好落在击杀前 offset 处。
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

                    // 合并重叠的片段
                    clips = MergeClips(clips);

                    // 剪辑并拼接
                    string outputDir = Path.Combine(Path.GetTempPath(), "MajoCupDirector");
                    Directory.CreateDirectory(outputDir);
                    string outputFile = Path.Combine(outputDir, $"replay_{DateTime.Now:yyyyMMddHHmmss}.mp4");

                    AppendLog($"开始剪辑 {clips.Count} 个片段 -> {outputFile}");
                    await _ffmpegService.ClipAndConcatAsync(recordingPath, outputFile, clips,
                        _settingsService.FfmpegPath);

                    AppendLog("剪辑完成");
                }
                catch (Exception ex)
                {
                    Status = $"处理失败: {ex.Message}";
                    AppendLog($"处理失败: {ex.Message}");
                    IsProcessing = false;
                }
            }
            finally
            {
                // 回合已完全收尾；下一回合现在可以开始自己的场景切换与录制了。
                _isRoundEnding = false;
            }
        }

        private async Task OnClippingComplete(ClippingCompletedEventArgs e)
        {
            try
            {
                if (e == null || string.IsNullOrEmpty(e.FilePath) || !File.Exists(e.FilePath))
                {
                    Status = "错误: 剪辑文件不存在";
                    AppendLog("错误: 剪辑文件不存在");
                    IsProcessing = false;
                    return;
                }

                string videoFile = e.FilePath;
                _isReplayPlaying = true;

                try
                {
                    // 回放绝不能被录进画面：停止在片段还在处理期间、新回合开始
                    // 时可能已经启动的录制。只有在回放播放完毕并恢复游戏场景后
                    // 才会恢复录制。
                    if (await _obsService.IsRecordingActiveAsync())
                    {
                        await _obsService.StopRecordingAsync();
                        IsRecording = false;
                        lock (_killLock) { _recordingActive = false; }
                        AppendLog("回放播放前已停止录制");
                    }

                    // 切换到回放场景
                    string replayScene = string.IsNullOrEmpty(ReplaySceneName) ? "Replay" : ReplaySceneName;
                    await _obsService.SwitchToSceneAsync(replayScene);
                    AppendLog($"已切换到回放场景 '{replayScene}'");

                    // 创建或复用 Replay 源
                    string sourceName = string.IsNullOrEmpty(ReplaySourceName) ? "Replay" : ReplaySourceName;
                    await _obsService.CreateReplaySourceAsync(replayScene, sourceName, videoFile);
                    AppendLog($"已在 '{replayScene}' 中设置源 '{sourceName}': {videoFile}");

                    // 播放媒体
                    await _obsService.PlayMediaAsync(replayScene, sourceName);
                    AppendLog("开始播放回放...");

                    Status = "正在播放回放...";

                    // 以实际视频时长加少量缓冲作为等待上限，这样切回游戏场景
                    // 永远不依赖不可靠的事件或过长的默认超时。即使下一回合已经
                    // 开始，回放场景也会保持活动直到播放结束。
                    TimeSpan playbackBudget = e.Duration + TimeSpan.FromSeconds(1);
                    await _obsService.WaitForMediaPlaybackEndedAsync(sourceName, playbackBudget);
                    AppendLog("回放播放结束");

                    // 切回游戏场景
                    string gameScene = string.IsNullOrEmpty(GameSceneName) ? "Game" : GameSceneName;
                    await _obsService.SwitchToSceneAsync(gameScene);
                    AppendLog($"已切换回游戏场景 '{gameScene}'");
                }
                finally
                {
                    _isReplayPlaying = false;
                    ReplayPlaybackEnded?.Invoke();
                }

                // 回放已结束且游戏场景已重新激活；
                // 现在才为当前回合开始录制。
                try
                {
                    await EnsureRecordingAsync();
                }
                catch (Exception ex)
                {
                    Status = $"恢复录制失败: {ex.Message}";
                    AppendLog($"恢复录制失败: {ex.Message}");
                    IsProcessing = false;
                    return;
                }

                // 录制已（重新）启动，录制时间轴已归零；
                // 将击杀位置重新锚定到新的零点。
                lock (_killLock)
                {
                    _recordingStartTime = DateTime.Now;
                    _recordingActive = true;
                }
                IsProcessing = false;
                Status = "回放完成，继续录制";
                AppendLog("回放完成，继续录制");
            }
            catch (Exception ex)
            {
                Status = $"回放失败: {ex.Message}";
                AppendLog($"回放失败: {ex.Message}");
                IsProcessing = false;
            }
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
                    // 合并重叠的片段
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
}
