using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfApp.Models.DataManagement;

/// <summary>
/// 单个 GridView 列配置，对应配置界面里的一行列定义。
/// </summary>
public sealed class TestDataGridColumnConfig : INotifyPropertyChanged
{
    private string _columnName = string.Empty;
    private string _bindingPath = string.Empty;
    private bool _isVisible = true;
    private double _width = 150d;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ColumnName
    {
        get => _columnName;
        set => SetField(ref _columnName, value?.Trim() ?? string.Empty);
    }

    // 备注：BindingPath 保存 TestDataRecord 的属性名，运行时用它创建 DataGridTextColumn.Binding。
    public string BindingPath
    {
        get => _bindingPath;
        set => SetField(ref _bindingPath, value?.Trim() ?? string.Empty);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    public double Width
    {
        get => _width;
        set => SetField(ref _width, value <= 0 ? 150d : value);
    }

    // 备注：复制配置时需要深拷贝列，避免两个配置共用同一个列对象。
    public TestDataGridColumnConfig Clone()
    {
        return new TestDataGridColumnConfig
        {
            ColumnName = ColumnName,
            BindingPath = BindingPath,
            IsVisible = IsVisible,
            Width = Width
        };
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
