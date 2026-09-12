using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CS2_Director_Tool.App.Converters;

/// <summary>
/// 将对象是否为 null 转换为布尔值，用于控制可见性。通过结构体初始化
/// 实例的 <see cref="Invert"/> 标记区分「非空」与「为空」两种场景。
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    /// <summary>反向：value 为 null 时返回 true。</summary>
    public bool Invert { get; set; }

    /// <summary>转换为布尔值：默认非空返回 true；启用 <see cref="Invert"/> 后为空返回 true。</summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Invert ? value is null : value is not null;
    }

    /// <summary>不做转换。</summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}