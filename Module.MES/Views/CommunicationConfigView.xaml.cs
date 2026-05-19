using Module.MES.ViewModels;
using System;
using System.Windows.Controls;

namespace Module.MES.Views;

public partial class CommunicationConfigView : UserControl
{
    public CommunicationConfigView()
    {
        InitializeComponent();
    }

    public CommunicationConfigView(CommunicationConfigViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
