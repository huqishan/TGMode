using ControlLibrary;
using System.Linq;
using System.Windows.Data;

namespace Module.Communication.ViewModels;

/// <summary>
/// 协议配置界面的 ViewModel 入口，负责初始化下拉选项、命令和协议配置集合。
/// </summary>
public sealed partial class ProtocolConfigViewModel : ViewModelProperties
{
    #region 构造方法
    public ProtocolConfigViewModel()
    {
        InitializeOptionCollections();
        InitializeCommands();

        int loadedProfileCount = LoadProfilesFromDisk();
        if (loadedProfileCount == 0)
        {
            SeedProfiles();
            SetPageStatus("未发现本地协议配置，已创建默认示例。", NeutralBrush);
        }
        else
        {
            SetPageStatus($"已从 {ProtocolConfigDirectory} 读取 {loadedProfileCount} 个协议配置。", SuccessBrush);
        }

        ProfilesView = CollectionViewSource.GetDefaultView(Profiles);
        ProfilesView.Filter = FilterProfiles;
        SelectedProfile = Profiles.FirstOrDefault();
    }

    #endregion
}
