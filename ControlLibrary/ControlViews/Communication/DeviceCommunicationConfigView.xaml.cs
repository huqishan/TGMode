using ControlLibrary.ControlViews.Communication.Models;
using Shared.Abstractions.Enum;
using Shared.Models.Communication;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ControlLibrary.ControlViews.Communication
{
    /// <summary>
    /// Device communication configuration page.
    /// </summary>
    public partial class DeviceCommunicationConfigView : UserControl, INotifyPropertyChanged
    {
        private static readonly JsonSerializerOptions PreviewJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private static readonly Brush SuccessBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

        private static readonly Brush WarningBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

        private static readonly Brush NeutralBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

        private DeviceCommunicationProfile? _selectedProfile;
        private string _previewStatusText = "请选择或创建一个通信配置。";
        private Brush _previewStatusBrush = NeutralBrush;
        private string _previewJson = "{ }";

        public DeviceCommunicationConfigView()
        {
            InitializeComponent();

            CommunicationTypes = new ObservableCollection<CommunicationTypeOption>
            {
                new CommunicationTypeOption(CommuniactionType.TCPClient, "TCP Client", "主动连接远端设备。"),
                new CommunicationTypeOption(CommuniactionType.TCPServer, "TCP Server", "本地开启端口监听。"),
                new CommunicationTypeOption(CommuniactionType.UDP, "UDP", "轻量无连接报文通信。"),
                new CommunicationTypeOption(CommuniactionType.COM, "COM", "串口通信。")
            };

            PortNameOptions = new ObservableCollection<string>(Enumerable.Range(1, 16).Select(index => $"COM{index}"));
            BaudRateOptions = new ObservableCollection<SelectionOption>
            {
                new SelectionOption("1200", "1200"),
                new SelectionOption("2400", "2400"),
                new SelectionOption("4800", "4800"),
                new SelectionOption("9600", "9600"),
                new SelectionOption("19200", "19200"),
                new SelectionOption("38400", "38400"),
                new SelectionOption("57600", "57600"),
                new SelectionOption("115200", "115200")
            };
            ParityOptions = new ObservableCollection<SelectionOption>
            {
                new SelectionOption("0", "0 - None"),
                new SelectionOption("1", "1 - Odd"),
                new SelectionOption("2", "2 - Even"),
                new SelectionOption("3", "3 - Mark"),
                new SelectionOption("4", "4 - Space")
            };
            DataBitOptions = new ObservableCollection<SelectionOption>
            {
                new SelectionOption("5", "5"),
                new SelectionOption("6", "6"),
                new SelectionOption("7", "7"),
                new SelectionOption("8", "8")
            };
            StopBitOptions = new ObservableCollection<SelectionOption>
            {
                new SelectionOption("0", "0 - None"),
                new SelectionOption("1", "1 - One"),
                new SelectionOption("2", "2 - Two"),
                new SelectionOption("3", "3 - OnePointFive")
            };

            SeedProfiles();
            SelectedProfile = Profiles.FirstOrDefault();
            DataContext = this;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<DeviceCommunicationProfile> Profiles { get; } = new ObservableCollection<DeviceCommunicationProfile>();

        public ObservableCollection<CommunicationTypeOption> CommunicationTypes { get; }

        public ObservableCollection<string> PortNameOptions { get; }

        public ObservableCollection<SelectionOption> BaudRateOptions { get; }

        public ObservableCollection<SelectionOption> ParityOptions { get; }

        public ObservableCollection<SelectionOption> DataBitOptions { get; }

        public ObservableCollection<SelectionOption> StopBitOptions { get; }

        public DeviceCommunicationProfile? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (ReferenceEquals(_selectedProfile, value))
                {
                    return;
                }

                if (_selectedProfile is not null)
                {
                    _selectedProfile.PropertyChanged -= SelectedProfile_PropertyChanged;
                }

                _selectedProfile = value;

                if (_selectedProfile is not null)
                {
                    _selectedProfile.PropertyChanged += SelectedProfile_PropertyChanged;
                }

                OnPropertyChanged();
                UpdatePreview();
            }
        }

        public string PreviewStatusText
        {
            get => _previewStatusText;
            private set
            {
                if (_previewStatusText == value)
                {
                    return;
                }

                _previewStatusText = value;
                OnPropertyChanged();
            }
        }

        public Brush PreviewStatusBrush
        {
            get => _previewStatusBrush;
            private set
            {
                if (ReferenceEquals(_previewStatusBrush, value))
                {
                    return;
                }

                _previewStatusBrush = value;
                OnPropertyChanged();
            }
        }

        public string PreviewJson
        {
            get => _previewJson;
            private set
            {
                if (_previewJson == value)
                {
                    return;
                }

                _previewJson = value;
                OnPropertyChanged();
            }
        }

        private void NewProfileButton_Click(object sender, RoutedEventArgs e)
        {
            CommuniactionType type = SelectedProfile?.Type ?? CommuniactionType.TCPClient;
            DeviceCommunicationProfile profile = CreateProfile(type, GenerateUniqueName(type));
            AddProfile(profile);
            SelectedProfile = profile;
        }

        private void DuplicateProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                return;
            }

            DeviceCommunicationProfile profile = SelectedProfile.Clone(GenerateUniqueName(SelectedProfile.Type));
            AddProfile(profile);
            SelectedProfile = profile;
        }

        private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                return;
            }

            int currentIndex = Profiles.IndexOf(SelectedProfile);
            DeviceCommunicationProfile deletedProfile = SelectedProfile;
            deletedProfile.PropertyChanged -= SelectedProfile_PropertyChanged;
            Profiles.Remove(deletedProfile);

            if (Profiles.Count == 0)
            {
                DeviceCommunicationProfile profile = CreateProfile(CommuniactionType.TCPClient, GenerateUniqueName(CommuniactionType.TCPClient));
                AddProfile(profile);
            }

            SelectedProfile = Profiles[Math.Clamp(currentIndex, 0, Profiles.Count - 1)];
        }

        private void ResetProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                return;
            }

            // Reset only type specific fields and keep the profile name for easier reuse.
            SelectedProfile.ResetToCurrentTypeDefaults();
            UpdatePreview();
        }

        private void SelectedProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void SeedProfiles()
        {
            AddProfile(CreateProfile(CommuniactionType.TCPClient, "TCPClient 1"));
            AddProfile(CreateProfile(CommuniactionType.TCPServer, "TCPServer 1"));
            AddProfile(CreateProfile(CommuniactionType.UDP, "UDP 1"));
            AddProfile(CreateProfile(CommuniactionType.COM, "COM 1"));
        }

        private DeviceCommunicationProfile CreateProfile(CommuniactionType type, string name)
        {
            DeviceCommunicationProfile profile = new DeviceCommunicationProfile
            {
                LocalName = name,
                Type = type
            };
            profile.ResetToCurrentTypeDefaults();
            return profile;
        }

        private void AddProfile(DeviceCommunicationProfile profile)
        {
            Profiles.Add(profile);
        }

        private string GenerateUniqueName(CommuniactionType type)
        {
            string prefix = type switch
            {
                CommuniactionType.TCPClient => "TCPClient",
                CommuniactionType.TCPServer => "TCPServer",
                CommuniactionType.UDP => "UDP",
                CommuniactionType.COM => "COM",
                _ => "Communication"
            };

            for (int index = 1; ; index++)
            {
                string name = $"{prefix} {index}";
                if (!Profiles.Any(profile => string.Equals(profile.LocalName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return name;
                }
            }
        }

        private void UpdatePreview()
        {
            if (SelectedProfile is null)
            {
                PreviewStatusBrush = NeutralBrush;
                PreviewStatusText = "请选择或创建一个通信配置。";
                PreviewJson = "{ }";
                return;
            }

            if (SelectedProfile.TryBuildRuntimeConfig(out CommuniactionConfigModel? config, out string message) && config is not null)
            {
                PreviewStatusBrush = SuccessBrush;
                PreviewStatusText = message;
                PreviewJson = JsonSerializer.Serialize(config, PreviewJsonOptions);
                return;
            }

            PreviewStatusBrush = WarningBrush;
            PreviewStatusText = message;
            PreviewJson = JsonSerializer.Serialize(
                new
                {
                    ValidationMessage = message,
                    Draft = new
                    {
                        SelectedProfile.LocalName,
                        Type = SelectedProfile.Type.ToString(),
                        SelectedProfile.LocalIPAddress,
                        SelectedProfile.LocalPort,
                        SelectedProfile.RemoteIPAddress,
                        SelectedProfile.RemotePort,
                        SelectedProfile.PortName,
                        SelectedProfile.BaudRate,
                        SelectedProfile.Parity,
                        SelectedProfile.DataBits,
                        SelectedProfile.StopBits
                    }
                },
                PreviewJsonOptions);
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
