using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace Module.Business.Features.OperationEditing.Converters;

/// <summary>
/// 根据参数类型选择共享的参数值候选集合。
/// 输入参数与条件执行左右参数统一使用该转换器，避免界面事件手动刷新。
/// </summary>
public sealed class ParameterValueOptionsConverter : IMultiValueConverter
{
    /// <summary>
    /// 使用参数类型从返回值和业务值共享集合中选择一个作为下拉候选。
    /// </summary>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        string parameterType = values.Length > 0 ? values[0]?.ToString()?.Trim() ?? string.Empty : string.Empty;
        return parameterType switch
        {
            "返回值" when values.Length > 1 && values[1] is IEnumerable returnValueOptions => returnValueOptions,
            "业务值" when values.Length > 2 && values[2] is IEnumerable workStepValueOptions => workStepValueOptions,
            _ => Array.Empty<string>()
        };
    }

    /// <summary>
    /// 候选集合只用于单向显示，不支持反向写回。
    /// </summary>
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        return targetTypes.Select(_ => DependencyProperty.UnsetValue).ToArray();
    }
}
