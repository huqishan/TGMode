using Module.Business.ViewModels;
using System;
using System.Windows.Controls;

namespace Module.Business.Views;

/// <summary>
/// Lua script editor view.
/// </summary>
public partial class LuaScriptView : UserControl
{
    public LuaScriptView()
    {
        InitializeComponent();
    }

    public LuaScriptView(LuaScriptViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
