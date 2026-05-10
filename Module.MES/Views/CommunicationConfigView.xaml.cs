using Module.MES.ViewModels;
using System.Windows.Controls;

namespace Module.MES.Views;

public partial class CommunicationConfigView : UserControl
{
    public CommunicationConfigView()
    {
        InitializeComponent();
        DataContext = new CommunicationConfigViewModel();
    }
}
