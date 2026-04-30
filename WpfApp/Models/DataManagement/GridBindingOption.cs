using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace WpfApp.Models.DataManagement;

/// <summary>
/// 数据绑定下拉框选项，来源于测试数据模型的 public 属性。
/// </summary>
public sealed class GridBindingOption
{
    public GridBindingOption(string propertyName, string displayName, Type propertyType)
    {
        PropertyName = propertyName;
        DisplayName = displayName;
        PropertyType = propertyType;
    }

    public string PropertyName { get; }

    public string DisplayName { get; }

    public Type PropertyType { get; }

    public string TypeName => Nullable.GetUnderlyingType(PropertyType)?.Name ?? PropertyType.Name;

    // 备注：界面显示中文名和真实属性名，方便配置时看清绑定到哪个字段。
    public string DisplayText => $"{DisplayName} ({PropertyName})";

    // 备注：通过反射读取 model 字段，新增 model 属性后下拉框会自动出现对应选项。
    public static IReadOnlyList<GridBindingOption> FromModel<TModel>()
    {
        return typeof(TModel)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetMethod is not null && property.GetMethod.IsPublic)
            .Select(property => new GridBindingOption(
                property.Name,
                property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? property.Name,
                property.PropertyType))
            .ToList();
    }
}
