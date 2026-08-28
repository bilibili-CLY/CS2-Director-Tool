using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using CS2_Director_Tool.App.Models;

namespace CS2_Director_Tool.App.Services;

/// <summary>
/// 管理与 GSI 事件动作规则的订阅与执行。
/// </summary>
public interface IEventActionService
{
    /// <summary>
    /// 设置规则与开关的来源。参数为委托，以便即时反映用户在界面上的编辑。
    /// </summary>
    void SetRuleSource(Func<bool> enabledProvider, Func<IReadOnlyList<EventActionRule>> rulesProvider);
}

/// <summary>
/// 事件动作服务：订阅 GSI 事件，当事件触发时查找启用且匹配的规则，
/// 并按顺序执行规则中的动作（播放媒体 / 停止媒体 / 切换场景）。
/// </summary>
public class EventActionService : IEventActionService
{
    private readonly IObsService _obsService;
    private readonly IReplayWorkflowService _replayService;
    private readonly ILogService _log;

    private Func<bool> _enabledProvider = () => false;
    private Func<IReadOnlyList<EventActionRule>> _rulesProvider = () => Array.Empty<EventActionRule>();

    /// <summary>初始化 <see cref="EventActionService"/> 类的新实例。</summary>
    public EventActionService(IGsiService gsiService, IObsService obsService,
        IReplayWorkflowService replayService, ILogService log)
    {
        _obsService = obsService;
        _replayService = replayService;
        _log = log;

        gsiService.OnMatchStarted += (s, e) => OnEvent(GsiEventType.MatchStarted);
        gsiService.OnRoundStarted += (s, e) => OnEvent(GsiEventType.RoundStarted);
        gsiService.OnRoundEnded += (s, e) => OnEvent(GsiEventType.RoundEnded);
        gsiService.OnGameOver += (s, e) => OnEvent(GsiEventType.GameOver);
        gsiService.OnWarmupStarted += (s, e) => OnEvent(GsiEventType.WarmupStarted);
        gsiService.OnWarmupOver += (s, e) => OnEvent(GsiEventType.WarmupOver);
        gsiService.OnGamePaused += (s, e) => OnEvent(GsiEventType.PauseStarted);
        gsiService.OnGameResumed += (s, e) => OnEvent(GsiEventType.PauseOver);
        gsiService.OnBombPlanted += (s, e) => OnEvent(GsiEventType.BombPlanted);
        gsiService.OnBombDefused += (s, e) => OnEvent(GsiEventType.BombDefused);
        gsiService.OnBombExploded += (s, e) => OnEvent(GsiEventType.BombExploded);
        gsiService.OnPlayerDied += (s, e) => OnEvent(GsiEventType.PlayerDied);
        gsiService.OnKill += (s, e) => OnEvent(GsiEventType.Kill);
    }

    /// <inheritdoc/>
    public void SetRuleSource(Func<bool> enabledProvider, Func<IReadOnlyList<EventActionRule>> rulesProvider)
    {
        _enabledProvider = enabledProvider ?? (() => false);
        _rulesProvider = rulesProvider ?? (() => Array.Empty<EventActionRule>());
    }

    private void OnEvent(GsiEventType eventType)
    {
        if (!_enabledProvider())
            return;

        List<EventActionRule>? matchedRules = null;
        try
        {
            var rules = _rulesProvider();
            if (rules is not null)
            {
                foreach (var rule in rules)
                {
                    if (rule != null && rule.IsEnabled && rule.EventType == eventType)
                    {
                        (matchedRules ??= new List<EventActionRule>()).Add(rule);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.Log(LogCategory.EventAction, $"读取事件规则失败: {ex.Message}");
            return;
        }

        if (matchedRules is null)
            return;

        string eventName = EnumDescription.GetDescription(eventType);
        _log.Log(LogCategory.EventAction, $"事件触发: {eventName}，命中 {matchedRules.Count} 条规则");
        foreach (var rule in matchedRules)
        {
            _ = ExecuteRuleAsync(rule);
        }
    }

    private async Task ExecuteRuleAsync(EventActionRule rule)
    {
        string eventName = EnumDescription.GetDescription(rule.EventType);
        foreach (var action in rule.Actions)
        {
            bool isReplayAction = action.Type is EventActionType.StartReplayRecording
                or EventActionType.RecordKillPoint
                or EventActionType.GenerateReplay;

            if (!isReplayAction && string.IsNullOrWhiteSpace(action.Target))
            {
                _log.Log(LogCategory.EventAction, $"规则「{eventName}」动作「{action.TypeLabel}」的目标为空，跳过");
                continue;
            }

            try
            {
                switch (action.Type)
                {
                    case EventActionType.PlayMedia:
                        await _obsService.PlayMediaSourceAsync(action.Target);
                        break;
                    case EventActionType.StopMedia:
                        await _obsService.StopMediaSourceAsync(action.Target);
                        break;
                    case EventActionType.SwitchScene:
                        await _obsService.SwitchToSceneAsync(action.Target);
                        break;
                    case EventActionType.StartReplayRecording:
                        await _replayService.StartRecordingAsync();
                        break;
                    case EventActionType.RecordKillPoint:
                        await _replayService.RecordKillPointAsync();
                        break;
                    case EventActionType.GenerateReplay:
                        await _replayService.GenerateReplayAsync();
                        break;
                }
                _log.Log(LogCategory.EventAction, $"规则「{eventName}」执行动作「{action.TypeLabel}」→ {action.Target}");
            }
            catch (Exception ex)
            {
                _log.Log(LogCategory.EventAction, $"规则「{eventName}」动作「{action.TypeLabel}」执行失败: {ex.Message}");
            }
        }
    }
}
