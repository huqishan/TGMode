using ControlLibrary;
using System.Collections.ObjectModel;

namespace Module.User.Models;

#region 权限树节点模型

/// <summary>
/// 权限配置表格中的树形节点模型。
/// </summary>
public sealed class UiPermissionTreeNode : ViewModelProperties
{
    #region 字段

    private bool _isVisible;
    private bool _isEnabled;
    private bool _isExpanded;
    private int _level;

    #endregion

    #region 构造方法

    public UiPermissionTreeNode(UiPermissionNodeDefinition definition, bool isVisible, bool isEnabled)
    {
        Definition = definition;
        _isVisible = isVisible;
        _isEnabled = isEnabled;
    }

    #endregion

    #region 基础属性

    public UiPermissionNodeDefinition Definition { get; }

    public ObservableCollection<UiPermissionTreeNode> Children { get; } = new();

    public string Key => Definition.Key;

    public string DisplayName => Definition.DisplayName;

    public string KindDisplayName => Definition.KindDisplayName;

    public string ElementIdentifier =>
        Definition.Kind == UiPermissionNodeKind.Page ? string.Empty : Definition.ElementIdentifier;

    public string SourcePath => Definition.SourcePath;

    public bool HasChildren => Children.Count > 0;

    #endregion

    #region 展开与权限属性

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ExpandGlyph));
            }
        }
    }

    public string ExpandGlyph => !HasChildren
        ? string.Empty
        : IsExpanded ? "v" : ">";

    public int Level
    {
        get => _level;
        set
        {
            if (SetField(ref _level, value))
            {
                OnPropertyChanged(nameof(IndentWidth));
            }
        }
    }

    public double IndentWidth => Math.Min(Level, 4) * 22d;

    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    #endregion
}

#endregion
