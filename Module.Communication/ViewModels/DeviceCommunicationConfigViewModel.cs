using ControlLibrary;
using System.Linq;

namespace Module.Communication.ViewModels;

/// <summary>
/// 设备通信配置界面的 ViewModel 入口，负责初始化下拉选项、命令和本地配置。
/// </summary>
public sealed partial class DeviceCommunicationConfigViewModel : ViewModelProperties
{
    #region 构造方法
    public DeviceCommunicationConfigViewModel()
    {
        InitializeSelectionOptions();
        InitializeCommands();

        int loadedProfileCount = LoadProfilesFromDisk();
        if (loadedProfileCount == 0)
        {
            SeedProfiles();
        }

        SelectedProfile = Profiles.FirstOrDefault();

        AppendReceiveLine(
            loadedProfileCount > 0
                ? $"已从 {CommunicationConfigDirectory} 读取 {loadedProfileCount} 个通信配置。"
                : $"未发现本地通信配置，已创建默认配置。保存后会写入 {CommunicationConfigDirectory}。");
    }

    #endregion
}
