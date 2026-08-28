using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace CS2_Director_Tool.App.Models;

/// <summary>
/// 一条事件动作规则：当指定的 GSI 事件触发时，按顺序执行一组动作。
/// </summary>
public class EventActionRule : ObservableObject
{
    private bool _isEnabled = true;
    private GsiEventType _eventType = GsiEventType.PauseStarted;

    /// <summary>该规则是否启用。</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    /// <summary>触发该规则的事件类型。</summary>
    public GsiEventType EventType
    {
        get => _eventType;
        set
        {
            if (SetProperty(ref _eventType, value))
                OnPropertyChanged(nameof(EventTypeLabel));
        }
    }

    /// <summary>触发后按顺序执行的动作列表。</summary>
    public ObservableCollection<EventActionItem> Actions { get; } = new();

    /// <summary>事件类型的中文显示名。</summary>
    [JsonIgnore]
    public string EventTypeLabel => EnumDescription.GetDescription(_eventType);
}
