using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using CS2_Director_Tool.App.Services;

namespace CS2_Director_Tool.App.ViewModels
{
    /// <summary>
    /// 暂停音乐页面视图模型，在游戏暂停期间播放背景音乐。
    /// </summary>
    public partial class PauseMusicViewModel : ViewModelBase
    {
        private const float SilenceDb = -100f;
        private static readonly TimeSpan FadeOutDuration = TimeSpan.FromSeconds(2);
        private const int FadeStepMs = 50;

        private readonly ISettingsService _settingsService;
        private readonly IGsiService _gsiService;
        private readonly IObsService _obsService;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private readonly Func<bool> _isReplayPlayingProvider;

        private bool _isEnabled;
        private string _musicSourceName;
        private string _status;
        private bool _isMusicActive;
        private bool _pendingPlayOnReplayEnd;
        private float _originalVolumeDb;
        private float _currentVolumeDb;
        private CancellationTokenSource _operationCts;

        /// <summary>
        /// 获取或设置是否启用暂停音乐功能。
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (SetProperty(ref _isEnabled, value))
                {
                    _settingsService.PauseMusicEnabled = value;
                    AppendLog(value ? "已启用暂停音乐" : "已取消启用暂停音乐");
                    if (!value)
                        StopMusicAsync();
                }
            }
        }

        /// <summary>
        /// 获取或设置暂停音乐使用的 OBS 媒体源名称。
        /// </summary>
        public string MusicSourceName
        {
            get => _musicSourceName;
            set
            {
                if (SetProperty(ref _musicSourceName, value))
                    _settingsService.PauseMusicSourceName = value;
            }
        }

        /// <summary>
        /// 获取页面上显示的当前状态文本。
        /// </summary>
        public string Status
        {
            get => _status;
            private set => SetStatus(value);
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
        /// 初始化 <see cref="PauseMusicViewModel"/> 类的新实例。
        /// </summary>
        public PauseMusicViewModel(ISettingsService settingsService, IGsiService gsiService, IObsService obsService,
            Func<bool> isReplayPlayingProvider = null)
        {
            _settingsService = settingsService;
            _gsiService = gsiService;
            _obsService = obsService;
            _isReplayPlayingProvider = isReplayPlayingProvider;

            _isEnabled = settingsService.PauseMusicEnabled;
            _musicSourceName = settingsService.PauseMusicSourceName;

            ClearLogCommand = new RelayCommand(() =>
            {
                _logBuffer.Clear();
                LogText = string.Empty;
            });

            _gsiService.OnGamePaused += async (s, e) => await OnGamePausedAsync();
            _gsiService.OnGameResumed += async (s, e) => await OnGameResumedAsync();
            _gsiService.OnLog += (s, line) => AppendLog(line);
            _obsService.OnLog += (s, message) => AppendLog(message);
            _obsService.OnDisconnected += (s, e) => OnObsDisconnected();
        }

        /// <summary>
        /// 处理游戏暂停事件，以淡入方式播放音乐。
        /// </summary>
        private async Task OnGamePausedAsync()
        {
            await _operationLock.WaitAsync();
            try
            {
                if (!IsEnabled || _isMusicActive)
                    return;

                if (_isReplayPlayingProvider?.Invoke() == true)
                {
                    _pendingPlayOnReplayEnd = true;
                    SetStatus("游戏暂停，但击杀回放播放中，等待回放结束后播放音乐");
                    AppendLog("游戏暂停，但击杀回放播放中，挂起暂停音乐，待回放结束回到游戏场景后播放");
                    return;
                }

                string sourceName = MusicSourceName;
                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    SetStatus("游戏暂停，但音乐源名为空，跳过播放");
                    AppendLog("游戏暂停，但音乐源名为空，跳过播放");
                    return;
                }

                AppendLog($"检测到游戏暂停，准备播放音乐源 '{sourceName}'");
                CancelPendingOperation();
                _operationCts = new CancellationTokenSource();
                CancellationToken token = _operationCts.Token;

                if (!(await _obsService.InputExistsAsync(sourceName)))
                {
                    SetStatus($"游戏暂停，但音乐源 '{sourceName}' 不存在");
                    AppendLog($"音乐源 '{sourceName}' 不存在，跳过播放");
                    return;
                }
                token.ThrowIfCancellationRequested();

                _originalVolumeDb = await _obsService.GetInputVolumeDbAsync(sourceName);
                _currentVolumeDb = _originalVolumeDb;
                _isMusicActive = true;

                await _obsService.SetInputVolumeDbAsync(sourceName, _originalVolumeDb);
                token.ThrowIfCancellationRequested();
                await _obsService.PlayMediaSourceAsync(sourceName);
                AppendLog($"已触发播放，音量 {_originalVolumeDb:F1}dB");
                SetStatus("游戏暂停，音乐播放中");

                try
                {
                    await Task.Delay(1000, token);
                    var status = await _obsService.GetMediaStatusAsync(sourceName);
                    if (status != null)
                    {
                        AppendLog($"媒体源状态: State={status.State}, Cursor={status.CursorMs}ms / Duration={status.DurationMs}ms");
                        if (!string.Equals(status.State, "OBS_MEDIA_STATE_PLAYING", StringComparison.Ordinal) || (status.DurationMs > 0 && status.CursorMs <= 0))
                            AppendLog("提示: 媒体源未处于播放中或播放位置无进展，请检查媒体文件是否有效");
                    }
                }
                catch (OperationCanceledException)
                {
                    // 在状态检查之前播放已被取消；无需记录日志。
                }
                catch (Exception ex)
                {
                    AppendLog($"查询媒体源状态失败: {ex.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                await TryRestoreVolumeAsync();
            }
            catch (Exception ex)
            {
                SetStatus($"暂停音乐失败: {ex.Message}");
                AppendLog($"暂停音乐失败: {ex.Message}");
                _isMusicActive = false;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>
        /// 处理游戏恢复事件，以淡出方式停止音乐。
        /// </summary>
        private async Task OnGameResumedAsync()
        {
            await _operationLock.WaitAsync();
            try
            {
                _pendingPlayOnReplayEnd = false;
                if (!_isMusicActive)
                    return;

                string sourceName = MusicSourceName;
                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    _isMusicActive = false;
                    return;
                }

                AppendLog("检测到暂停结束，开始 2 秒淡出");
                CancelPendingOperation();
                _operationCts = new CancellationTokenSource();
                CancellationToken token = _operationCts.Token;

                SetStatus("暂停结束，音乐淡出...");
                await FadeVolumeAsync(sourceName, _currentVolumeDb, SilenceDb, FadeOutDuration, token);
                await _obsService.StopMediaSourceAsync(sourceName);
                await _obsService.SetInputVolumeDbAsync(sourceName, _originalVolumeDb);
                _currentVolumeDb = _originalVolumeDb;
                _isMusicActive = false;
                AppendLog("音乐已停止，音量已恢复");
                SetStatus("音乐已停止");
            }
            catch (OperationCanceledException)
            {
                await TryRestoreVolumeAsync();
            }
            catch (Exception ex)
            {
                SetStatus($"停止音乐失败: {ex.Message}");
                AppendLog($"停止音乐失败: {ex.Message}");
                _isMusicActive = false;
            }
            finally
            {
                _operationLock.Release();
            }
        }

        /// <summary>
        /// 功能被停用时停止音乐。
        /// </summary>
        private async void StopMusicAsync()
        {
            await OnGameResumedAsync();
        }

        /// <summary>
        /// 击杀回放结束后恢复被挂起的暂停音乐播放。
        /// </summary>
        public void OnReplayPlaybackEnded()
        {
            if (!_pendingPlayOnReplayEnd || !IsEnabled)
                return;
            _pendingPlayOnReplayEnd = false;
            AppendLog("回放结束，回到游戏场景，开始播放暂停音乐");
            _ = OnGamePausedAsync();
        }

        /// <summary>
        /// OBS 断开连接时取消挂起的操作并重置本地状态。
        /// </summary>
        private void OnObsDisconnected()
        {
            CancelPendingOperation();
            _pendingPlayOnReplayEnd = false;
            _isMusicActive = false;
            SetStatus("OBS 已断开");
            AppendLog("OBS 已断开，暂停音乐已复位");
        }

        /// <summary>
        /// 将音乐音量从一个分贝值线性淡变到另一个分贝值。
        /// </summary>
        private async Task FadeVolumeAsync(string sourceName, float fromDb, float toDb, TimeSpan duration, CancellationToken token)
        {
            int steps = Math.Max(1, (int)(duration.TotalMilliseconds / FadeStepMs));
            for (int i = 1; i <= steps; i++)
            {
                token.ThrowIfCancellationRequested();
                _currentVolumeDb = fromDb + (toDb - fromDb) * i / steps;
                await _obsService.SetInputVolumeDbAsync(sourceName, _currentVolumeDb);
                await Task.Delay(FadeStepMs, token);
            }
            _currentVolumeDb = toDb;
        }

        /// <summary>
        /// 操作中途被取消时恢复原始音量。
        /// </summary>
        private async Task TryRestoreVolumeAsync()
        {
            if (!_isMusicActive)
                return;

            string sourceName = MusicSourceName;
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                _isMusicActive = false;
                return;
            }

            try
            {
                await _obsService.SetInputVolumeDbAsync(sourceName, _originalVolumeDb);
            }
            catch
            {
                // 尽力而为；源可能已不存在或 OBS 已断开。
            }
            _isMusicActive = false;
        }

        /// <summary>
        /// 取消并释放任何挂起的淡变操作。
        /// </summary>
        private void CancelPendingOperation()
        {
            _operationCts?.Cancel();
            _operationCts?.Dispose();
            _operationCts = null;
        }

        /// <summary>
        /// 在 UI 线程上更新状态文本。
        /// </summary>
        private void SetStatus(string value)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
            });
        }
    }
}
