using System;
using System.Windows.Controls;
using Module.Business.Features.LuaScript;

namespace Module.Business.Features.LuaScript.Views;

/// <summary>
/// Lua 脚本管理界面。
/// 提供 Lua 脚本的新建、编辑、保存与删除入口，脚本内容保存到独立配置文件。
/// </summary>
public partial class LuaScriptView : UserControl
{
    /// <summary>
    /// 设计期构造函数，仅初始化界面元素。
    /// </summary>
    public LuaScriptView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 运行期构造函数，由视图工厂注入 ViewModel 并建立数据上下文。
    /// </summary>
    /// <param name="viewModel">Lua 脚本 ViewModel。</param>
    public LuaScriptView(LuaScriptViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
