using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace CS2_Director_Tool.App.Models;

/// <summary>
/// 事件触发后执行的一个具体动作。
/// </summary>
public class EventActionItem : ObservableObject
{
    private EventActionType _type = EventActionType.PlayMedia;
    private string _target = string.Empty;

    /// <summary>动作类型。</summary>
    public EventActionType Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    /// <summary>动作目标的名称：媒体源名或场景名（依 <see cref="Type"/> 而定）。</summary>
    public string Target
    {
        get => _target;
        set => SetProperty(ref _target, value ?? string.Empty);
    }

    /// <summary>动作类型的中文显示名。</summary>
    [JsonIgnore]
    public string TypeLabel => EnumDescription.GetDescription(_type);
}

/// <summary>
/// 提供 <see cref="System.ComponentModel.DescriptionAttribute"/> 的读取帮助方法。
/// </summary>
public static class EnumDescription
{
    /// <summary>返回枚举值的 Description 特性描述；未标注时回退为枚举名。</summary>
    public static string GetDescription(System.Enum value)
    {
        var field = value.GetType().GetField(value.ToString());
        var attr = field?.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
            .FirstOrDefault() as System.ComponentModel.DescriptionAttribute;
        return attr?.Description ?? value.ToString();
    }
}
