using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CS2_Director_Tool.App.Models;

/// <summary>
/// 一组命名的事件动作规则预设，可整组保存并在需要时加载应用。
/// </summary>
public class EventActionPreset : ObservableObject
{
    private string _name = string.Empty;

    /// <summary>预设名称。</summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    /// <summary>预设包含的规则。</summary>
    public List<EventActionRule> Rules { get; set; } = new();
}
