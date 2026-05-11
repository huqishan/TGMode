using ControlLibrary;
using System.Linq;
using System.Windows.Data;

namespace Module.Communication.ViewModels;

public sealed partial class DeviceCommunicationConfigViewModel : ViewModelProperties
{
    public DeviceCommunicationConfigViewModel()
    {
        InitializeSelectionOptions();
        InitializeCommands();

        ProfilesView = CollectionViewSource.GetDefaultView(Profiles);
        ProfilesView.Filter = FilterProfiles;
        AvailableProtocolsView = CollectionViewSource.GetDefaultView(AvailableProtocols);
        AvailableProtocolsView.Filter = FilterAvailableProtocols;
        SupportedProtocolCommandsView = CollectionViewSource.GetDefaultView(SupportedProtocolCommands);
        SupportedProtocolCommandsView.Filter = FilterSupportedProtocolCommands;

        int loadedProfileCount = LoadProfilesFromDisk();
        if (loadedProfileCount == 0)
        {
            SeedProfiles();
        }

        RefreshAvailableProtocols();
        SelectedProfile = Profiles.FirstOrDefault();

        AppendReceiveLine(
            loadedProfileCount > 0
                ? $"Loaded {loadedProfileCount} communication profile(s) from {CommunicationConfigDirectory}."
                : $"No local communication profiles were found. A default profile was created and will be saved to {CommunicationConfigDirectory}.");
    }
}
