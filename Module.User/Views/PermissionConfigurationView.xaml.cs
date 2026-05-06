using Module.User.ViewModels;
using System.Windows.Controls;

namespace Module.User.Views;

public partial class PermissionConfigurationView : UserControl
{
    #region 构造方法

    public PermissionConfigurationView()
    {
        InitializeComponent();
        ApplySourceColumnVisibility();
    }

    #endregion

    #region 纯界面状态

    private void ApplySourceColumnVisibility()
    {
        if (DataContext is PermissionConfigurationViewModel viewModel)
        {
            SourceColumn.Visibility = viewModel.SourceColumnVisibility;
        }
    }

    #endregion
}
