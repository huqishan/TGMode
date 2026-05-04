using ControlLibrary.ControlViews.Protocol.Models;
using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.PackMethod;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ControlLibrary.ControlViews.Protocol
{
    /// <summary>
    /// 协议配置页面，负责协议模板维护、本地保存和预览生成。
    /// </summary>
    public partial class ProtocolConfigView : UserControl, INotifyPropertyChanged
    {
        private static readonly string ProtocolConfigDirectory =
            Path.Combine(AppContext.BaseDirectory, "Config", "Protocol");

        private static readonly JsonSerializerOptions StorageJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly Brush SuccessBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

        private static readonly Brush WarningBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

        private static readonly Brush NeutralBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

        private const double CommandDrawerClosedOffset = 56d;
        private static readonly Duration CommandDrawerAnimationDuration = new Duration(TimeSpan.FromMilliseconds(220));
        private static readonly IEasingFunction CommandDrawerEasing = new CubicEase { EasingMode = EasingMode.EaseOut };

        private readonly Dictionary<ProtocolConfigProfile, string> _profileStorageFileNames = new Dictionary<ProtocolConfigProfile, string>();
        private ProtocolConfigProfile? _selectedProfile;
        private string _pageStatusText = "等待初始化";
        private Brush _pageStatusBrush = NeutralBrush;
        private string _requestPreviewStatusText = "等待输入";
        private Brush _requestPreviewStatusBrush = NeutralBrush;
        private string _responsePreviewStatusText = "等待输入";
        private Brush _responsePreviewStatusBrush = NeutralBrush;
        private string _requestPreviewText = "请先选择或创建一个协议配置。";
        private string _responsePreviewText = "请填写示例返回数据后查看解析预览。";
        private string _generatedCommandText = string.Empty;
        private string _parsedResultText = string.Empty;
        private string _searchText = string.Empty;
        private bool _isCommandDrawerOpen;

        public ProtocolConfigView()
        {
            InitializeComponent();

            PayloadFormats = new ObservableCollection<ProtocolOption<ProtocolPayloadFormat>>
            {
                new ProtocolOption<ProtocolPayloadFormat>(ProtocolPayloadFormat.Hex, "Hex", "按十六进制字节内容构建报文。"),
                new ProtocolOption<ProtocolPayloadFormat>(ProtocolPayloadFormat.Ascii, "ASCII", "按 ASCII 文本内容构建报文。")
            };

            CrcModes = new ObservableCollection<ProtocolOption<ProtocolCrcMode>>
            {
                new ProtocolOption<ProtocolCrcMode>(ProtocolCrcMode.None, "无校验", "不自动追加 CRC。"),
                new ProtocolOption<ProtocolCrcMode>(ProtocolCrcMode.ModbusCrc16, "Modbus CRC16", "低字节在前，高字节在后。"),
                new ProtocolOption<ProtocolCrcMode>(ProtocolCrcMode.Crc16Ibm, "CRC16-IBM", "IBM 反射模式，低字节在前。"),
                new ProtocolOption<ProtocolCrcMode>(ProtocolCrcMode.Crc16CcittFalse, "CRC16-CCITT-FALSE", "高字节在前，常用于工业协议。"),
                new ProtocolOption<ProtocolCrcMode>(ProtocolCrcMode.Crc32, "CRC32", "四字节 CRC32，小端追加。")
            };

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

            DataContext = this;
            SelectedProfile = Profiles.FirstOrDefault();
            Loaded += ProtocolConfigView_Loaded;
            Unloaded += ProtocolConfigView_Unloaded;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ProtocolConfigProfile> Profiles { get; } = new ObservableCollection<ProtocolConfigProfile>();

        public ICollectionView ProfilesView { get; private set; } = null!;

        public ObservableCollection<ProtocolOption<ProtocolPayloadFormat>> PayloadFormats { get; }

        public ObservableCollection<ProtocolOption<ProtocolCrcMode>> CrcModes { get; }

        public ProtocolConfigProfile? SelectedProfile
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
                UpdatePreviews();
                IsCommandDrawerOpen = false;
                UpdateCommandDrawerVisual(animate: false);
                ClearGeneratedOutputs();
            }
        }


        public string PageStatusText
        {
            get => _pageStatusText;
            private set
            {
                if (_pageStatusText == value)
                {
                    return;
                }

                _pageStatusText = value;
                OnPropertyChanged();
            }
        }

        public Brush PageStatusBrush
        {
            get => _pageStatusBrush;
            private set
            {
                if (ReferenceEquals(_pageStatusBrush, value))
                {
                    return;
                }

                _pageStatusBrush = value;
                OnPropertyChanged();
            }
        }

        public string RequestPreviewStatusText
        {
            get => _requestPreviewStatusText;
            private set
            {
                if (_requestPreviewStatusText == value)
                {
                    return;
                }

                _requestPreviewStatusText = value;
                OnPropertyChanged();
            }
        }

        public Brush RequestPreviewStatusBrush
        {
            get => _requestPreviewStatusBrush;
            private set
            {
                if (ReferenceEquals(_requestPreviewStatusBrush, value))
                {
                    return;
                }

                _requestPreviewStatusBrush = value;
                OnPropertyChanged();
            }
        }

        public string ResponsePreviewStatusText
        {
            get => _responsePreviewStatusText;
            private set
            {
                if (_responsePreviewStatusText == value)
                {
                    return;
                }

                _responsePreviewStatusText = value;
                OnPropertyChanged();
            }
        }

        public Brush ResponsePreviewStatusBrush
        {
            get => _responsePreviewStatusBrush;
            private set
            {
                if (ReferenceEquals(_responsePreviewStatusBrush, value))
                {
                    return;
                }

                _responsePreviewStatusBrush = value;
                OnPropertyChanged();
            }
        }

        public string RequestPreviewText
        {
            get => _requestPreviewText;
            private set
            {
                if (_requestPreviewText == value)
                {
                    return;
                }

                _requestPreviewText = value;
                OnPropertyChanged();
            }
        }

        public string ResponsePreviewText
        {
            get => _responsePreviewText;
            private set
            {
                if (_responsePreviewText == value)
                {
                    return;
                }

                _responsePreviewText = value;
                OnPropertyChanged();
            }
        }

        public string GeneratedCommandText
        {
            get => _generatedCommandText;
            private set
            {
                if (_generatedCommandText == value)
                {
                    return;
                }

                _generatedCommandText = value;
                OnPropertyChanged();
            }
        }

        public string ParsedResultText
        {
            get => _parsedResultText;
            private set
            {
                if (_parsedResultText == value)
                {
                    return;
                }

                _parsedResultText = value;
                OnPropertyChanged();
            }
        }

        public bool IsCommandDrawerOpen
        {
            get => _isCommandDrawerOpen;
            private set
            {
                if (_isCommandDrawerOpen == value)
                {
                    return;
                }

                _isCommandDrawerOpen = value;
                OnPropertyChanged();
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (string.Equals(_searchText, value, StringComparison.Ordinal))
                {
                    return;
                }

                _searchText = value ?? string.Empty;
                OnPropertyChanged();
                ProfilesView?.Refresh();
            }
        }

        public string ReplyWaitHelpText =>
            "发送后可强制等待一段时间，把这段时间内收到的数据拼接为同一帧。填 0 表示不额外等待。";

        public string CrcHelpText =>
            "发送帧预览会按当前 CRC 方式自动在末尾追加校验字节，便于直接检查最终报文。";

        public string PlaceholderHelpText =>
            "模板中使用 {{Placeholder}} 形式占位；占位符值会从模板自动提取，可在表格中填写对应值。";

        public string ParseRuleHelpText =>
            "规则格式：Field = Expression。支持 hex、ascii、utf8、text、len、hex(start,length)、ascii(start,length)、utf8(start,length)、u8(index)、u16le(index)、u16be(index)、u32le(index)、u32be(index)，其中 length=-1 表示截取到末尾。";

        public string WaitForResponseHelpText =>
            "选中后显示返回示例与解析规则，并允许对返回数据执行解析。";

        private void ClearGeneratedOutputs()
        {
            GeneratedCommandText = string.Empty;
            ParsedResultText = string.Empty;
        }

        private static string BuildGeneratedCommandText(ProtocolCommandConfig command, ProtocolRequestPreviewResult previewResult)
        {
            return command.RequestFormat == ProtocolPayloadFormat.Hex
                ? previewResult.RequestHex
                : previewResult.RequestAscii;
        }

        private void ProtocolConfigView_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateCommandDrawerVisual(animate: false);
            HideCommandDrawerProtocolNameLabel();
        }

        private void HideCommandDrawerProtocolNameLabel()
        {
            if (CommandDrawerSheet is null)
            {
                return;
            }

            foreach (TextBlock textBlock in FindVisualChildren<TextBlock>(CommandDrawerSheet))
            {
                if (!string.Equals(textBlock.Text, "协议名称", StringComparison.Ordinal))
                {
                    continue;
                }

                textBlock.Visibility = Visibility.Collapsed;
                break;
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
        {
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < childCount; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);
                if (child is T match)
                {
                    yield return match;
                }

                foreach (T descendant in FindVisualChildren<T>(child))
                {
                    yield return descendant;
                }
            }
        }

        private void OpenCommandDrawer()
        {
            IsCommandDrawerOpen = true;
            UpdateCommandDrawerVisual(animate: true);
        }

        private void CloseCommandDrawer()
        {
            IsCommandDrawerOpen = false;
            UpdateCommandDrawerVisual(animate: true);
        }

        private void UpdateCommandDrawerVisual(bool animate)
        {
            if (CommandDrawerHost is null || CommandDrawerTranslateTransform is null)
            {
                return;
            }

            double targetOpacity = IsCommandDrawerOpen ? 1d : 0d;
            double targetOffset = IsCommandDrawerOpen ? 0d : CommandDrawerClosedOffset;

            if (IsCommandDrawerOpen)
            {
                CommandDrawerHost.IsHitTestVisible = true;
            }

            if (!animate)
            {
                CommandDrawerHost.BeginAnimation(UIElement.OpacityProperty, null);
                CommandDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                CommandDrawerHost.Opacity = targetOpacity;
                CommandDrawerTranslateTransform.Y = targetOffset;
                CommandDrawerHost.IsHitTestVisible = IsCommandDrawerOpen;
                return;
            }

            DoubleAnimation opacityAnimation = new DoubleAnimation
            {
                To = targetOpacity,
                Duration = CommandDrawerAnimationDuration,
                EasingFunction = CommandDrawerEasing
            };

            if (!IsCommandDrawerOpen)
            {
                opacityAnimation.Completed += (_, _) =>
                {
                    if (!IsCommandDrawerOpen)
                    {
                        CommandDrawerHost.IsHitTestVisible = false;
                    }
                };
            }

            DoubleAnimation translateAnimation = new DoubleAnimation
            {
                To = targetOffset,
                Duration = CommandDrawerAnimationDuration,
                EasingFunction = CommandDrawerEasing
            };

            CommandDrawerHost.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            CommandDrawerTranslateTransform.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
        }

        private void NewProfileButton_Click(object sender, RoutedEventArgs e)
        {
            ProtocolConfigProfile profile = CreateGenericProfile(GenerateUniqueName("协议"));
            AddProfile(profile);
            SelectedProfile = profile;
            SetPageStatus($"已新建协议配置：{profile.Name}。", SuccessBrush);
        }

        private void DuplicateProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                return;
            }

            ProtocolConfigProfile profile = SelectedProfile.Clone(GenerateCopyName(SelectedProfile.Name));
            AddProfile(profile);
            SelectedProfile = profile;
            SetPageStatus($"已复制协议配置：{profile.Name}。", SuccessBrush);
        }

        private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                return;
            }

            int currentIndex = Profiles.IndexOf(SelectedProfile);
            ProtocolConfigProfile deletedProfile = SelectedProfile;
            Profiles.Remove(deletedProfile);
            DeleteStoredProfileFile(deletedProfile);

            if (Profiles.Count == 0)
            {
                ProtocolConfigProfile profile = CreateGenericProfile(GenerateUniqueName("协议"));
                AddProfile(profile);
            }

            SelectedProfile = Profiles[Math.Clamp(currentIndex, 0, Profiles.Count - 1)];
            SetPageStatus($"已删除协议配置：{deletedProfile.Name}。", NeutralBrush);
        }

        private void NewCommandButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                return;
            }

            ProtocolCommandConfig command = new ProtocolCommandConfig
            {
                Name = GenerateUniqueCommandName(SelectedProfile, "指令")
            };
            SelectedProfile.AddCommand(command);
            SelectedProfile.SelectedCommand = command;
            ClearGeneratedOutputs();
            OpenCommandDrawer();
            SetPageStatus($"已新建指令：{command.Name}。", SuccessBrush);
        }

        private void DuplicateCommandButton_Click(object sender, RoutedEventArgs e)
        {
            ProtocolConfigProfile? profile = SelectedProfile;
            ProtocolCommandConfig? selectedCommand = profile?.SelectedCommand;
            if (profile is null || selectedCommand is null)
            {
                return;
            }

            ProtocolCommandConfig command = selectedCommand.Clone(GenerateUniqueCommandName(profile, $"{selectedCommand.Name} 副本"));
            profile.AddCommand(command);
            profile.SelectedCommand = command;
            ClearGeneratedOutputs();
            OpenCommandDrawer();
            SetPageStatus($"已复制指令：{command.Name}。", SuccessBrush);
        }

        private void DeleteCommandButton_Click(object sender, RoutedEventArgs e)
        {
            ProtocolConfigProfile? profile = SelectedProfile;
            ProtocolCommandConfig? selectedCommand = profile?.SelectedCommand;
            if (profile is null || selectedCommand is null)
            {
                return;
            }

            ClearGeneratedOutputs();
            profile.RemoveCommand(selectedCommand);
            if (profile.Commands.Count == 0)
            {
                ProtocolCommandConfig command = new ProtocolCommandConfig
                {
                    Name = GenerateUniqueCommandName(profile, "指令")
                };
                profile.AddCommand(command);
            }

            SetPageStatus($"已删除指令：{selectedCommand.Name}。", NeutralBrush);
        }

        private void CommandListBox_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not ListBox listBox)
            {
                return;
            }

            ListBoxItem? clickedItem = ItemsControl.ContainerFromElement(
                listBox,
                e.OriginalSource as DependencyObject) as ListBoxItem;

            if (clickedItem?.DataContext is not ProtocolCommandConfig command)
            {
                return;
            }

            if (!ReferenceEquals(SelectedProfile?.SelectedCommand, command))
            {
                if (SelectedProfile is not null)
                {
                    SelectedProfile.SelectedCommand = command;
                }
                else
                {
                    listBox.SelectedItem = command;
                }
            }

            ClearGeneratedOutputs();
            OpenCommandDrawer();
        }

        private void CloseCommandDrawerButton_Click(object sender, RoutedEventArgs e)
        {
            CloseCommandDrawer();
        }

        private void CommandDrawerBackdrop_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            CloseCommandDrawer();
        }

        private void GenerateCommandButton_Click(object sender, RoutedEventArgs e)
        {
            ProtocolCommandConfig? selectedCommand = SelectedProfile?.SelectedCommand;
            if (selectedCommand is null)
            {
                GeneratedCommandText = string.Empty;
                SetPageStatus("请先选择设备指令后再生成实际指令。", WarningBrush);
                return;
            }

            if (ProtocolPreviewEngine.TryBuildRequestPreview(selectedCommand, out ProtocolRequestPreviewResult? previewResult, out string message) &&
                previewResult is not null)
            {
                GeneratedCommandText = BuildGeneratedCommandText(selectedCommand, previewResult);
                SetPageStatus(message, SuccessBrush);
                return;
            }

            GeneratedCommandText = string.Empty;
            SetPageStatus(message, WarningBrush);
        }

        private void ParseResultButton_Click(object sender, RoutedEventArgs e)
        {
            ProtocolCommandConfig? selectedCommand = SelectedProfile?.SelectedCommand;
            if (selectedCommand is null)
            {
                ParsedResultText = string.Empty;
                SetPageStatus("请先选择设备指令后再解析返回数据。", WarningBrush);
                return;
            }

            if (!selectedCommand.WaitForResponse)
            {
                ParsedResultText = string.Empty;
                SetPageStatus("当前指令未启用等待数据返回。", WarningBrush);
                return;
            }

            if (ProtocolPreviewEngine.TryBuildResponsePreview(selectedCommand, out ProtocolResponsePreviewResult? previewResult, out string message) &&
                previewResult is not null)
            {
                ParsedResultText = previewResult.ParsedJson;
                SetPageStatus(message, SuccessBrush);
                return;
            }

            ParsedResultText = string.Empty;
            SetPageStatus(message, WarningBrush);
        }

        private void SaveProfilesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int savedCount = SaveProfilesToDisk();
                SetPageStatus($"已保存 {savedCount} 个协议配置到 {ProtocolConfigDirectory}。", SuccessBrush);
            }
            catch (Exception ex)
            {
                SetPageStatus($"保存协议配置失败：{ex.Message}", WarningBrush);
            }
        }

        private void RebuildPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            UpdatePreviews();
        }

        private void ProtocolConfigView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile is not null)
            {
                _selectedProfile.PropertyChanged -= SelectedProfile_PropertyChanged;
            }
        }

        private void SelectedProfile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ProtocolConfigProfile.Name) or nameof(ProtocolConfigProfile.Summary))
            {
                ProfilesView.Refresh();
            }

            UpdatePreviews();
        }

        private bool FilterProfiles(object item)
        {
            if (item is not ProtocolConfigProfile profile)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return true;
            }

            string keyword = SearchText.Trim();
            return Contains(profile.Name, keyword) ||
                   Contains(profile.Summary, keyword) ||
                   profile.Commands.Any(command => Contains(command.Name, keyword));
        }

        private static bool Contains(string? source, string keyword)
        {
            return source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void UpdatePreviews()
        {
            ProtocolConfigProfile? profile = SelectedProfile;
            if (profile is null)
            {
                RequestPreviewText = "请先选择或创建一个协议配置。";
                ResponsePreviewText = "请先选择或创建一个协议配置。";
                SetRequestPreviewStatus("未选择配置", NeutralBrush);
                SetResponsePreviewStatus("未选择配置", NeutralBrush);
                return;
            }

            if (ProtocolPreviewEngine.TryBuildRequestPreview(profile, out ProtocolRequestPreviewResult? requestResult, out string requestMessage) &&
                requestResult is not null)
            {
                RequestPreviewText = BuildRequestPreviewText(requestResult);
                SetRequestPreviewStatus(requestMessage, SuccessBrush);
            }
            else
            {
                RequestPreviewText = $"当前配置无法生成发送帧预览。{Environment.NewLine}{Environment.NewLine}{requestMessage}";
                SetRequestPreviewStatus(requestMessage, WarningBrush);
            }

            if (ProtocolPreviewEngine.TryBuildResponsePreview(profile, out ProtocolResponsePreviewResult? responseResult, out string responseMessage) &&
                responseResult is not null)
            {
                ResponsePreviewText = BuildResponsePreviewText(responseResult);
                Brush responseBrush = string.IsNullOrWhiteSpace(profile.SampleResponseText) ||
                                     string.IsNullOrWhiteSpace(profile.ParseRulesText)
                    ? NeutralBrush
                    : SuccessBrush;
                SetResponsePreviewStatus(responseMessage, responseBrush);
            }
            else
            {
                ResponsePreviewText = $"当前配置无法生成返回解析预览。{Environment.NewLine}{Environment.NewLine}{responseMessage}";
                SetResponsePreviewStatus(responseMessage, WarningBrush);
            }
        }

        private static string BuildRequestPreviewText(ProtocolRequestPreviewResult previewResult)
        {
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                "渲染模板",
                previewResult.RenderedTemplate,
                "发送 Hex",
                previewResult.RequestHex,
                "发送 ASCII",
                previewResult.RequestAscii);
        }

        private static string BuildResponsePreviewText(ProtocolResponsePreviewResult previewResult)
        {
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                "返回 Hex",
                previewResult.ResponseHex,
                "返回 ASCII",
                previewResult.ResponseAscii,
                "解析结果(JSON)",
                previewResult.ParsedJson);
        }

        private void SeedProfiles()
        {
            AddProfile(CreateModbusDemoProfile("Modbus 读寄存器"));
            AddProfile(CreateAsciiDemoProfile("ASCII 文本协议"));
        }

        private static ProtocolConfigProfile CreateGenericProfile(string name)
        {
            return new ProtocolConfigProfile
            {
                Name = name,
                RequestFormat = ProtocolPayloadFormat.Hex,
                ResponseFormat = ProtocolPayloadFormat.Hex,
                ReplyAggregationMilliseconds = "200",
                CrcMode = ProtocolCrcMode.None,
                ContentTemplate = "AA {{Address}} {{Command}}",
                PlaceholderValuesText = "Address=01\r\nCommand=03",
                SampleResponseText = "AA 01 03",
                ParseRulesText = "FullHex = hex\r\nLength = len"
            };
        }

        private static ProtocolConfigProfile CreateModbusDemoProfile(string name)
        {
            ProtocolConfigProfile profile = new ProtocolConfigProfile
            {
                Name = name,
                CommandName = "读保持寄存器",
                RequestFormat = ProtocolPayloadFormat.Hex,
                ResponseFormat = ProtocolPayloadFormat.Hex,
                ReplyAggregationMilliseconds = "200",
                CrcMode = ProtocolCrcMode.ModbusCrc16,
                ContentTemplate = "{{Station}} {{Function}} {{AddressHi}} {{AddressLo}} {{CountHi}} {{CountLo}}",
                PlaceholderValuesText = "Station=01\r\nFunction=03\r\nAddressHi=00\r\nAddressLo=00\r\nCountHi=00\r\nCountLo=02",
                SampleResponseText = "01 03 04 00 0A 00 14",
                ParseRulesText = "Station = u8(0)\r\nFunction = u8(1)\r\nByteCount = u8(2)\r\nDataHex = hex(3,-1)"
            };

            profile.AddCommand(new ProtocolCommandConfig
            {
                Name = "写单个寄存器",
                RequestFormat = ProtocolPayloadFormat.Hex,
                ResponseFormat = ProtocolPayloadFormat.Hex,
                ReplyAggregationMilliseconds = "200",
                CrcMode = ProtocolCrcMode.ModbusCrc16,
                ContentTemplate = "{{Station}} 06 {{AddressHi}} {{AddressLo}} {{ValueHi}} {{ValueLo}}",
                PlaceholderValuesText = "Station=01\r\nAddressHi=00\r\nAddressLo=01\r\nValueHi=00\r\nValueLo=0A",
                SampleResponseText = "01 06 00 01 00 0A",
                ParseRulesText = "Station = u8(0)\r\nFunction = u8(1)\r\nAddress = hex(2,2)\r\nValue = hex(4,2)"
            });

            profile.SelectedCommand = null;
            return profile;
        }

        private static ProtocolConfigProfile CreateAsciiDemoProfile(string name)
        {
            ProtocolConfigProfile profile = new ProtocolConfigProfile
            {
                Name = name,
                CommandName = "读取通道",
                RequestFormat = ProtocolPayloadFormat.Ascii,
                ResponseFormat = ProtocolPayloadFormat.Ascii,
                ReplyAggregationMilliseconds = "300",
                CrcMode = ProtocolCrcMode.None,
                ContentTemplate = "READ {{Channel}}",
                PlaceholderValuesText = "Channel=T1",
                SampleResponseText = "OK,T1,25.6",
                ParseRulesText = "FullText = text\r\nLength = len\r\nPrefix = ascii(0,2)"
            };

            profile.AddCommand(new ProtocolCommandConfig
            {
                Name = "写入通道",
                RequestFormat = ProtocolPayloadFormat.Ascii,
                ResponseFormat = ProtocolPayloadFormat.Ascii,
                ReplyAggregationMilliseconds = "300",
                CrcMode = ProtocolCrcMode.None,
                ContentTemplate = "WRITE {{Channel}} {{Value}}",
                PlaceholderValuesText = "Channel=T1\r\nValue=25.6",
                SampleResponseText = "OK,T1",
                ParseRulesText = "FullText = text\r\nLength = len"
            });

            profile.SelectedCommand = null;
            return profile;
        }

        private void AddProfile(ProtocolConfigProfile profile)
        {
            Profiles.Add(profile);
        }

        private int LoadProfilesFromDisk()
        {
            if (!Directory.Exists(ProtocolConfigDirectory))
            {
                return 0;
            }

            int loadedCount = 0;
            foreach (string filePath in Directory.EnumerateFiles(ProtocolConfigDirectory, "*.json").OrderBy(Path.GetFileName))
            {
                try
                {
                    string storageText = File.ReadAllText(filePath, Encoding.UTF8);
                    ProtocolConfigProfileDocument? document =JsonHelper.DeserializeObject<ProtocolConfigProfileDocument>(storageText.DesDecrypt());
                        //ProtocolConfigStorageSerializer.Deserialize(storageText, StorageJsonOptions);
                    if (document is null)
                    {
                        continue;
                    }

                    ProtocolConfigProfile profile = document.ToProfile();
                    AddProfile(profile);
                    _profileStorageFileNames[profile] = Path.GetFileName(filePath);
                    loadedCount++;
                }
                catch (Exception ex)
                {
                    SetPageStatus($"读取协议配置失败：{Path.GetFileName(filePath)}，原因：{ex.Message}", WarningBrush);
                }
            }

            return loadedCount;
        }

        private int SaveProfilesToDisk()
        {
            Directory.CreateDirectory(ProtocolConfigDirectory);

            HashSet<string> usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int savedCount = 0;
            foreach (ProtocolConfigProfile profile in Profiles)
            {
                ValidateProfileForSave(profile);

                string fileName = BuildUniqueStorageFileName(profile.Name, usedFileNames);
                string filePath = Path.Combine(ProtocolConfigDirectory, fileName);
                string storageText = JsonHelper.SerializeObject(ProtocolConfigProfileDocument.FromProfile(profile)).Encrypt();
                File.WriteAllText(filePath, storageText, Encoding.UTF8);

                if (_profileStorageFileNames.TryGetValue(profile, out string? oldFileName) &&
                    !string.Equals(oldFileName, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteStorageFile(oldFileName);
                }

                _profileStorageFileNames[profile] = fileName;
                savedCount++;
            }

            return savedCount;
        }

        private void DeleteStoredProfileFile(ProtocolConfigProfile profile)
        {
            if (!_profileStorageFileNames.TryGetValue(profile, out string? fileName))
            {
                return;
            }

            TryDeleteStorageFile(fileName);
            _profileStorageFileNames.Remove(profile);
        }

        private static void ValidateProfileForSave(ProtocolConfigProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                throw new InvalidOperationException("协议名称不能为空。");
            }

            if (profile.Commands.Count == 0)
            {
                throw new InvalidOperationException($"协议 {profile.Name} 至少需要包含一条指令。");
            }

            foreach (ProtocolCommandConfig command in profile.Commands)
            {
                if (string.IsNullOrWhiteSpace(command.Name))
                {
                    throw new InvalidOperationException($"协议 {profile.Name} 存在未命名指令。");
                }

                if (!int.TryParse(command.ReplyAggregationMilliseconds.Trim(), out int replyWait) || replyWait < 0)
                {
                    throw new InvalidOperationException($"指令 {command.Name} 的强制等待拼接时长必须是大于等于 0 的整数毫秒。");
                }
            }
        }

        private static void TryDeleteStorageFile(string fileName)
        {
            try
            {
                string filePath = Path.Combine(ProtocolConfigDirectory, fileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
                // 删除旧文件失败不影响界面继续使用，后续可以人工清理。
            }
        }

        private static string BuildUniqueStorageFileName(string profileName, HashSet<string> usedFileNames)
        {
            string safeName = BuildSafeFileName(profileName);
            string fileName = $"{safeName}.json";
            for (int index = 2; usedFileNames.Contains(fileName); index++)
            {
                fileName = $"{safeName}_{index}.json";
            }

            usedFileNames.Add(fileName);
            return fileName;
        }

        private static string BuildSafeFileName(string value)
        {
            HashSet<char> invalidChars = new HashSet<char>(Path.GetInvalidFileNameChars());
            StringBuilder builder = new StringBuilder(value.Trim().Length);
            foreach (char current in value.Trim())
            {
                builder.Append(invalidChars.Contains(current) || char.IsControl(current)
                    ? '_'
                    : char.IsWhiteSpace(current) ? '_' : current);
            }

            string safeName = builder.ToString().Trim(' ', '.');
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "Protocol";
            }

            return safeName.Length <= 80 ? safeName : safeName[..80];
        }

        private string GenerateUniqueName(string prefix)
        {
            for (int index = 1; ; index++)
            {
                string name = $"{prefix} {index}";
                if (!Profiles.Any(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return name;
                }
            }
        }

        private static string GenerateUniqueCommandName(ProtocolConfigProfile profile, string prefix)
        {
            string baseName = string.IsNullOrWhiteSpace(prefix) ? "指令" : prefix.Trim();
            if (!profile.Commands.Any(command => string.Equals(command.Name, baseName, StringComparison.OrdinalIgnoreCase)))
            {
                return baseName;
            }

            for (int index = 2; ; index++)
            {
                string name = $"{baseName} {index}";
                if (!profile.Commands.Any(command => string.Equals(command.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return name;
                }
            }
        }

        private string GenerateCopyName(string baseName)
        {
            string prefix = string.IsNullOrWhiteSpace(baseName) ? "协议" : baseName.Trim();
            string firstName = $"{prefix} 副本";
            if (!Profiles.Any(profile => string.Equals(profile.Name, firstName, StringComparison.OrdinalIgnoreCase)))
            {
                return firstName;
            }

            for (int index = 2; ; index++)
            {
                string name = $"{prefix} 副本 {index}";
                if (!Profiles.Any(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return name;
                }
            }
        }

        private void SetPageStatus(string text, Brush brush)
        {
            PageStatusText = text;
            PageStatusBrush = brush;
        }

        private void SetRequestPreviewStatus(string text, Brush brush)
        {
            RequestPreviewStatusText = text;
            RequestPreviewStatusBrush = brush;
        }

        private void SetResponsePreviewStatus(string text, Brush brush)
        {
            ResponsePreviewStatusText = text;
            ResponsePreviewStatusBrush = brush;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
