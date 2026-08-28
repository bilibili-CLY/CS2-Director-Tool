using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CS2_Director_Tool.App.Models;

namespace CS2_Director_Tool.App.Converters;

/// <summary>
/// 将枚举值转换为其中文 Description 描述文本。
/// </summary>
public class EnumToDescriptionConverter : IValueConverter
{
    /// <summary>转换为描述文本。</summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Enum e ? EnumDescription.GetDescription(e) : value;
    }

    /// <summary>不做转换。</summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}
