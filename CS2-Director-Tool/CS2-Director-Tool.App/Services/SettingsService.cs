using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 基于 JSON 的 <see cref="ISettingsService"/> 实现，将设置持久化到用户应用程序数据目录中的文件。
/// 密码以明文保存（1:1 复刻原项目行为，跨平台不引入平台专属加密）。
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;

    private string _cs2Path = string.Empty;
    private string _ffmpegPath = string.Empty;
    private string _obsWebSocketAddress = string.Empty;
    private string _obsWebSocketPort = string.Empty;
    private string _obsWebSocketPassword = string.Empty;
    private string _gameSceneName = string.Empty;
    private string _replaySceneName = string.Empty;
    private string _replaySourceName = "Replay";
    private bool _killReplayEnabled;
    private bool _eventActionEnabled;
    private List<Models.EventActionRule> _eventActionRules = new();
    private List<Models.EventActionPreset> _eventActionPresets = new();
    private string _playerApiBaseUrl = "https://majo-cup.laffeynyaa.com";
    private string _replayOutputPath = string.Empty;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.Combine(appData, "MajoCupDirector");
        _settingsFilePath = Path.Combine(directory, "settings.json");

        _replayOutputPath = Path.Combine(Path.GetTempPath(), "CSDirectorTool");

        Load();
    }

    public string Cs2Path
    {
        get => _cs2Path;
        set { _cs2Path = value ?? string.Empty; Save(); }
    }

    public string FfmpegPath
    {
        get => _ffmpegPath;
        set { _ffmpegPath = value ?? string.Empty; Save(); }
    }

    public string ObsWebSocketAddress
    {
        get => _obsWebSocketAddress;
        set { _obsWebSocketAddress = value ?? string.Empty; Save(); }
    }

    public string ObsWebSocketPort
    {
        get => _obsWebSocketPort;
        set { _obsWebSocketPort = value ?? string.Empty; Save(); }
    }

    public string ObsWebSocketPassword
    {
        get => _obsWebSocketPassword;
        set { _obsWebSocketPassword = value ?? string.Empty; Save(); }
    }

    public string GameSceneName
    {
        get => _gameSceneName;
        set { _gameSceneName = value ?? string.Empty; Save(); }
    }

    public string ReplaySceneName
    {
        get => _replaySceneName;
        set { _replaySceneName = value ?? string.Empty; Save(); }
    }

    public string ReplaySourceName
    {
        get => _replaySourceName;
        set { _replaySourceName = value ?? string.Empty; Save(); }
    }

    public bool KillReplayEnabled
    {
        get => _killReplayEnabled;
        set { _killReplayEnabled = value; Save(); }
    }

    public List<Models.EventActionRule> EventActionRules
    {
        get => _eventActionRules;
        set { _eventActionRules = value ?? new(); Save(); }
    }

    public bool EventActionEnabled
    {
        get => _eventActionEnabled;
        set { _eventActionEnabled = value; Save(); }
    }

    public List<Models.EventActionPreset> EventActionPresets
    {
        get => _eventActionPresets;
        set { _eventActionPresets = value ?? new(); Save(); }
    }

    public string PlayerApiBaseUrl
    {
        get => _playerApiBaseUrl;
        set { _playerApiBaseUrl = value ?? string.Empty; Save(); }
    }

    public string ReplayOutputPath
    {
        get => _replayOutputPath;
        set { _replayOutputPath = value ?? string.Empty; Save(); }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
                return;

            var json = File.ReadAllText(_settingsFilePath);
            var data = JsonConvert.DeserializeObject<SettingsData>(json);

            if (data is null)
                return;

            _cs2Path = data.Cs2Path ?? string.Empty;
            _ffmpegPath = data.FfmpegPath ?? string.Empty;
            _obsWebSocketAddress = data.ObsWebSocketAddress ?? string.Empty;
            _obsWebSocketPort = data.ObsWebSocketPort ?? string.Empty;
            _obsWebSocketPassword = data.ObsWebSocketPassword ?? string.Empty;
            _gameSceneName = data.GameSceneName ?? string.Empty;
            _replaySceneName = data.ReplaySceneName ?? string.Empty;
            _replaySourceName = data.ReplaySourceName ?? "Replay";
            _killReplayEnabled = data.KillReplayEnabled;
            _eventActionEnabled = data.EventActionEnabled;
            _eventActionRules = data.EventActionRules ?? new();
            _eventActionPresets = data.EventActionPresets ?? new();
            _playerApiBaseUrl = string.IsNullOrWhiteSpace(data.PlayerApiBaseUrl)
                ? "https://majo-cup.laffeynyaa.com"
                : data.PlayerApiBaseUrl;
            _replayOutputPath = data.ReplayOutputPath ?? Path.Combine(Path.GetTempPath(), "CSDirectorTool");
        }
        catch
        {
            // 读取失败（如文件损坏）时回退为默认值。
        }
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var data = new SettingsData
            {
                Cs2Path = _cs2Path,
                FfmpegPath = _ffmpegPath,
                ObsWebSocketAddress = _obsWebSocketAddress,
                ObsWebSocketPort = _obsWebSocketPort,
                ObsWebSocketPassword = _obsWebSocketPassword,
                GameSceneName = _gameSceneName,
                ReplaySceneName = _replaySceneName,
                ReplaySourceName = _replaySourceName,
                KillReplayEnabled = _killReplayEnabled,
                EventActionEnabled = _eventActionEnabled,
                EventActionRules = _eventActionRules,
                EventActionPresets = _eventActionPresets,
                PlayerApiBaseUrl = _playerApiBaseUrl,
                ReplayOutputPath = _replayOutputPath
            };

            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // 保存失败时静默忽略，避免导致应用程序崩溃。
        }
    }

    private class SettingsData
    {
        public string Cs2Path { get; set; } = string.Empty;
        public string FfmpegPath { get; set; } = string.Empty;
        public string ObsWebSocketAddress { get; set; } = string.Empty;
        public string ObsWebSocketPort { get; set; } = string.Empty;
        public string ObsWebSocketPassword { get; set; } = string.Empty;
        public string GameSceneName { get; set; } = string.Empty;
        public string ReplaySceneName { get; set; } = string.Empty;
        public string ReplaySourceName { get; set; } = "Replay";
        public bool KillReplayEnabled { get; set; }
        public bool EventActionEnabled { get; set; }
        public List<Models.EventActionRule> EventActionRules { get; set; } = new();
        public List<Models.EventActionPreset> EventActionPresets { get; set; } = new();
        public string PlayerApiBaseUrl { get; set; } = "https://majo-cup.laffeynyaa.com";
        public string ReplayOutputPath { get; set; } = string.Empty;
    }
}
