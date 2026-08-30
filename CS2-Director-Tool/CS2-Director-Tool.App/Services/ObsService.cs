using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CS2_Director_Tool.App.Models;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication;
using OBSWebsocketDotNet.Types;
using OBSWebsocketDotNet.Types.Events;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 使用 obs-websocket-dotnet 通过 WebSocket 提供 OBS Studio 远程控制能力。
/// </summary>
public class ObsService : IObsService, IDisposable
{
    private readonly OBSWebsocket _obs = new OBSWebsocket();
    private readonly ILogService _log;

    private bool _disposed;

    private const int SceneAppearTimeoutMs = 3000;
    private const int InputAppearTimeoutMs = 3000;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _playbackEndedSignals =
        new ConcurrentDictionary<string, TaskCompletionSource<bool>>(StringComparer.OrdinalIgnoreCase);

    public bool IsConnected => _obs.IsConnected;

    public event EventHandler? OnConnected;
    public event EventHandler? OnDisconnected;

    private void Log(string message) => _log.Log(LogCategory.Obs, message);

    public ObsService(ILogService log)
    {
        _log = log;
        _obs.Connected += OnObsConnected;
        _obs.Disconnected += OnObsDisconnected;
        _obs.MediaInputPlaybackEnded += OnMediaInputPlaybackEnded;
    }

    public Task ConnectAsync(string address, string password)
    {
        var tcs = new TaskCompletionSource<bool>();

        void Handler(object? s, EventArgs e)
        {
            _obs.Connected -= Handler;
            tcs.TrySetResult(true);
        }

        void ErrorHandler(object? s, ObsDisconnectionInfo e)
        {
            _obs.Disconnected -= ErrorHandler;
            tcs.TrySetException(new InvalidOperationException(
                $"Failed to connect to OBS at {address}: {e.DisconnectReason}"));
        }

        _obs.Connected += Handler;
        _obs.Disconnected += ErrorHandler;

        try
        {
            _obs.ConnectAsync(address, password);
        }
        catch (Exception ex)
        {
            _obs.Connected -= Handler;
            _obs.Disconnected -= ErrorHandler;
            tcs.TrySetException(new InvalidOperationException($"Failed to connect to OBS at {address}.", ex));
        }

        return tcs.Task;
    }

    public void Disconnect() => _obs.Disconnect();

    public async Task StartRecordingAsync()
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await Task.Run(() => _obs.StartRecord());
                return;
            }
            catch (ErrorResponseException) when (attempt < maxAttempts)
            {
                await Task.Delay(500);
            }
        }
    }

    public Task<bool> IsRecordingActiveAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                return _obs.GetRecordStatus().IsRecording;
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<string> StopRecordingAsync()
    {
        var fileName = await Task.Run(() => _obs.StopRecord());
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (!(await IsRecordingActiveAsync()))
                break;
            await Task.Delay(100);
        }

        var directory = await Task.Run(() => _obs.GetRecordDirectory());
        var fullPath = Path.Combine(directory, fileName);

        // OBS 在录制停止后可能仍在向磁盘写入并完成文件；在把它交给调用方之前，先等待文件可读。
        var fileDeadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < fileDeadline)
        {
            if (File.Exists(fullPath))
                return fullPath;
            await Task.Delay(250);
        }

        return fullPath;
    }

    public Task SwitchToSceneAsync(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
            throw new ArgumentNullException(nameof(sceneName));

        return Task.Run(() =>
        {
            var scene = FindScene(sceneName);
            if (scene is null)
            {
                CreateSceneIfMissing(sceneName);
                Log($"场景 '{sceneName}' 不存在，已自动创建");
                _obs.SetCurrentProgramScene(sceneName);
                return;
            }

            try
            {
                _obs.SetCurrentProgramScene(scene.Name);
            }
            catch (ErrorResponseException ex) when (ex.ErrorCode == 600)
            {
                CreateSceneIfMissing(sceneName);
                Log($"场景 '{sceneName}' 不存在，已自动创建");
                _obs.SetCurrentProgramScene(sceneName);
            }
        });
    }

    private void CreateSceneIfMissing(string sceneName)
    {
        try
        {
            _obs.CreateScene(sceneName);
        }
        catch (ErrorResponseException ex) when (ex.ErrorCode == 601)
        {
            // 同名场景已存在 —— 无需创建。
        }
    }

    public Task CreateReplaySourceAsync(string sceneName, string sourceName, string filePath)
    {
        if (string.IsNullOrEmpty(sceneName))
            throw new ArgumentNullException(nameof(sceneName));
        if (string.IsNullOrEmpty(sourceName))
            throw new ArgumentNullException(nameof(sourceName));
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        return Task.Run(() =>
        {
            if (FindScene(sceneName) is null)
            {
                CreateSceneIfMissing(sceneName);
                if (!WaitForSceneToExist(sceneName))
                {
                    Log($"错误: 回放场景 '{sceneName}' 创建失败，当前场景列表: {GetSceneNames()}");
                    throw new InvalidOperationException($"Scene '{sceneName}' could not be created.");
                }
            }

            var actualSceneName = FindScene(sceneName)?.Name ?? sceneName;

            var settings = new JObject
            {
                ["local_file"] = filePath,
                ["is_local_file"] = true,
                // 场景激活时源不得自动重新开始播放；否则切换到回放场景时会播放上一个文件并产生虚假的播放事件。
                ["restart_on_activate"] = false
            };

            if (InputExists(sourceName))
            {
                _obs.SetInputSettings(sourceName, settings, true);
                _obs.TriggerMediaInputAction(sourceName, "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_RESTART");
            }
            else
            {
                try
                {
                    _obs.CreateInput(actualSceneName, sourceName, "ffmpeg_source", settings, true);
                }
                catch (ErrorResponseException ex) when (ex.ErrorCode == 601)
                {
                    Log($"媒体源 '{sourceName}' 已存在（错误码 601），回退为更新设置并重启源");
                    _obs.SetInputSettings(sourceName, settings, true);
                    _obs.TriggerMediaInputAction(sourceName, "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_RESTART");
                }
            }

            if (!WaitForInputToExist(sourceName))
            {
                Log($"错误: 媒体源 '{sourceName}' 创建失败（场景 '{actualSceneName}'），当前输入列表: {GetInputNames()}");
                throw new InvalidOperationException($"Media source '{sourceName}' could not be created in scene '{actualSceneName}'.");
            }
        });
    }

    public Task PlayMediaAsync(string sceneName, string sourceName)
    {
        if (string.IsNullOrEmpty(sourceName))
            throw new ArgumentNullException(nameof(sourceName));

        return Task.Run(() =>
            _obs.TriggerMediaInputAction(sourceName, "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_PLAY"));
    }

    public async Task<bool> WaitForMediaPlaybackEndedAsync(string sourceName, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
            throw new ArgumentNullException(nameof(sourceName));

        var effectiveTimeout = timeout ?? await GetDefaultPlaybackTimeoutAsync(sourceName);

        // 忽略属于早前文件的“ended”事件（例如 RESTART 操作导致旧文件停止播放）。
        var minimumPlayTime = TimeSpan.FromSeconds(
            Math.Min(1.5, Math.Max(0.5, effectiveTimeout.TotalSeconds * 0.5)));

        await Task.Delay(minimumPlayTime, cancellationToken);

        var endedSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _playbackEndedSignals[sourceName] = endedSignal;

        var remaining = effectiveTimeout - minimumPlayTime;
        if (remaining > TimeSpan.Zero)
        {
            var ended = await WaitUntilCompletedAsync(endedSignal.Task, remaining, cancellationToken);
            if (!ended)
                Log($"警告: 回放 '{sourceName}' 在预期时长内未收到结束事件，按已播放完处理");
        }

        return true;
    }

    public Task<bool> InputExistsAsync(string sourceName) =>
        Task.Run(() => InputExists(sourceName));

    public Task<float> GetInputVolumeDbAsync(string inputName) =>
        Task.Run(() => _obs.GetInputVolume(inputName).VolumeDb);

    public Task SetInputVolumeDbAsync(string inputName, float volumeDb) =>
        Task.Run(() => _obs.SetInputVolume(inputName, volumeDb, true));

    public Task PlayMediaSourceAsync(string sourceName) =>
        Task.Run(() => _obs.TriggerMediaInputAction(sourceName, "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_RESTART"));

    public Task StopMediaSourceAsync(string sourceName) =>
        Task.Run(() => _obs.TriggerMediaInputAction(sourceName, "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_STOP"));

    public Task<ObsMediaStatusInfo?> GetMediaStatusAsync(string sourceName)
    {
        return Task.Run(() =>
        {
            try
            {
                var status = _obs.GetMediaInputStatus(sourceName);
                if (status is null)
                    return null;

                return new ObsMediaStatusInfo
                {
                    State = status.State,
                    DurationMs = status.Duration.HasValue ? (long?)status.Duration.Value : null,
                    CursorMs = (long?)status.Cursor,
                };
            }
            catch (ErrorResponseException ex)
            {
                Log($"警告: 无法查询媒体源 '{sourceName}' 状态: 错误码 {ex.ErrorCode}, {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Log($"警告: 无法查询媒体源 '{sourceName}' 状态: {ex.Message}");
                return null;
            }
        });
    }

    public Task<string?> GetCurrentSceneNameAsync() =>
        Task.Run(() =>
        {
            try
            {
                return _obs.GetCurrentProgramScene();
            }
            catch
            {
                return (string?)null;
            }
        });

    public Task SetInputSettingsAsync(string inputName, Newtonsoft.Json.Linq.JObject settings, bool overlay = true) =>
        Task.Run(() => _obs.SetInputSettings(inputName, settings, overlay));

    private static Task<bool> WaitUntilCompletedAsync(Task signalTask, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var delay = Task.Delay(timeout, cancellationToken);
        var completed = Task.WhenAny(signalTask, delay);
        return completed == signalTask ? Task.FromResult(true) : Task.FromResult(false);
    }

    private void OnMediaInputPlaybackEnded(object? sender, MediaInputPlaybackEndedEventArgs e)
    {
        if (_playbackEndedSignals.TryGetValue(e.InputName, out var signal))
            signal.TrySetResult(true);
    }

    private bool InputExists(string sourceName)
    {
        try
        {
            return _obs.GetInputList().Any(i =>
                string.Equals(i.InputName, sourceName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private async Task<TimeSpan> GetDefaultPlaybackTimeoutAsync(string sourceName)
    {
        try
        {
            var status = await GetMediaStatusAsync(sourceName);
            if (status?.DurationMs is { } duration && duration > 0)
                return TimeSpan.FromMilliseconds(duration);
        }
        catch
        {
            // 忽略，回退到默认超时。
        }

        return TimeSpan.FromSeconds(5);
    }

    private bool WaitForSceneToExist(string sceneName)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(SceneAppearTimeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (FindScene(sceneName) is not null)
                return true;
            Thread.Sleep(200);
        }

        return FindScene(sceneName) is not null;
    }

    private bool WaitForInputToExist(string sourceName)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(InputAppearTimeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (InputExists(sourceName))
                return true;
            Thread.Sleep(200);
        }

        return InputExists(sourceName);
    }

    private string GetSceneNames()
    {
        try
        {
            return string.Join(", ", _obs.ListScenes().Select(s => s.Name));
        }
        catch
        {
            return "(查询失败)";
        }
    }

    private string GetInputNames()
    {
        try
        {
            return string.Join(", ", _obs.GetInputList().Select(i => i.InputName));
        }
        catch
        {
            return "(查询失败)";
        }
    }

    private SceneBasicInfo? FindScene(string sceneName)
    {
        return _obs.ListScenes().FirstOrDefault(s =>
            string.Equals(s.Name, sceneName, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _obs.Connected -= OnObsConnected;
            _obs.Disconnected -= OnObsDisconnected;
            _obs.MediaInputPlaybackEnded -= OnMediaInputPlaybackEnded;

            if (_obs.IsConnected)
                _obs.Disconnect();
        }

        _disposed = true;
    }

    private void OnObsConnected(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => OnConnected?.Invoke(this, e));

    private void OnObsDisconnected(object? sender, ObsDisconnectionInfo e) =>
        Dispatcher.UIThread.Post(() => OnDisconnected?.Invoke(this, EventArgs.Empty));
}
