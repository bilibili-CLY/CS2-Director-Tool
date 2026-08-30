namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 提供应用程序设置的持久化。
/// </summary>
public interface ISettingsService
{
    string Cs2Path { get; set; }
    string FfmpegPath { get; set; }
    string ObsWebSocketAddress { get; set; }
    string ObsWebSocketPort { get; set; }
    string ObsWebSocketPassword { get; set; }
    string GameSceneName { get; set; }
    string ReplaySceneName { get; set; }

    /// <summary>获取或设置用于回放播放的媒体源名称。</summary>
    string ReplaySourceName { get; set; }

    /// <summary>获取或设置是否启用击杀回放功能。</summary>
    bool KillReplayEnabled { get; set; }

    /// <summary>获取或设置是否启用事件动作功能。</summary>
    bool EventActionEnabled { get; set; }

    /// <summary>获取或设置事件动作规则列表。</summary>
    System.Collections.Generic.List<Models.EventActionRule> EventActionRules { get; set; }

    /// <summary>获取或设置事件动作规则的命名预设列表。</summary>
    System.Collections.Generic.List<Models.EventActionPreset> EventActionPresets { get; set; }

    /// <summary>获取或设置玩家改名 API 的基础地址（可配置）。</summary>
    string PlayerApiBaseUrl { get; set; }

    /// <summary>获取或设置回放输出目录。</summary>
    string ReplayOutputPath { get; set; }

    /// <summary>从持久化存储加载设置。</summary>
    void Load();

    /// <summary>将设置保存到持久化存储。</summary>
    void Save();
}
