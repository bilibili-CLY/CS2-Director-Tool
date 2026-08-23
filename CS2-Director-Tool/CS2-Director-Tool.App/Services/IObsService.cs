using System;
using System.Threading;
using System.Threading.Tasks;
using CS2_Director_Tool.App.Models;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 通过 WebSocket 提供 OBS Studio 远程控制能力。
/// </summary>
public interface IObsService
{
    /// <summary>获取服务是否已连接到 OBS。</summary>
    bool IsConnected { get; }

    /// <summary>连接到 OBS 时发生。</summary>
    event EventHandler OnConnected;

    /// <summary>与 OBS 断开连接时发生。</summary>
    event EventHandler OnDisconnected;

    /// <summary>服务输出诊断消息时发生。</summary>
    event EventHandler<string> OnLog;

    /// <summary>连接到 OBS WebSocket 服务器。</summary>
    Task ConnectAsync(string address, string password);

    /// <summary>断开与 OBS 的连接。</summary>
    void Disconnect();

    /// <summary>开始录制。</summary>
    Task StartRecordingAsync();

    /// <summary>判断 OBS 当前是否正在录制。</summary>
    Task<bool> IsRecordingActiveAsync();

    /// <summary>停止录制并返回录制文件路径。</summary>
    Task<string> StopRecordingAsync();

    /// <summary>切换到指定场景，若场景不存在则先创建它。</summary>
    Task SwitchToSceneAsync(string sceneName);

    /// <summary>在指定场景中创建或更新回放媒体源。</summary>
    Task CreateReplaySourceAsync(string sceneName, string sourceName, string filePath);

    /// <summary>播放媒体源。</summary>
    Task PlayMediaAsync(string sceneName, string sourceName);

    /// <summary>使用 OBS 媒体播放事件等待指定的媒体输入播放完毕。</summary>
    Task<bool> WaitForMediaPlaybackEndedAsync(string sourceName, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>检查 OBS 中是否存在具有给定名称的输入源。</summary>
    Task<bool> InputExistsAsync(string sourceName);

    /// <summary>获取输入源当前的音量（分贝）。</summary>
    Task<float> GetInputVolumeDbAsync(string inputName);

    /// <summary>设置输入源的音量（分贝）。</summary>
    Task SetInputVolumeDbAsync(string inputName, float volumeDb);

    /// <summary>从头重新播放媒体源（STOP 后 PLAY）。</summary>
    Task PlayMediaSourceAsync(string sourceName);

    /// <summary>停止媒体源。</summary>
    Task StopMediaSourceAsync(string sourceName);

    /// <summary>获取指定媒体源的当前状态。无法查询状态时返回 null。</summary>
    Task<ObsMediaStatusInfo?> GetMediaStatusAsync(string sourceName);

    /// <summary>获取 OBS 当前激活（Program）场景名称。</summary>
    Task<string?> GetCurrentSceneNameAsync();

    /// <summary>设置输入源的配置（例如关闭 restart_on_activate）。</summary>
    Task SetInputSettingsAsync(string inputName, Newtonsoft.Json.Linq.JObject settings, bool overlay = true);
}
