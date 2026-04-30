using Module.MES.ViewModels.VMs;
using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.PackMethod;
using Shared.Models.MES;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Module.MES.Views
{
    /// <summary>
    /// ApiConfigView.xaml 的交互逻辑
    /// </summary>
    public partial class ApiConfigView : UserControl, INotifyPropertyChanged
    {
        private static readonly string MesConfigRootDirectory =
            Path.Combine(AppContext.BaseDirectory, "Config", "MES_Config");

        private static readonly string ApiConfigDirectory =
            Path.Combine(MesConfigRootDirectory, "ApiConfig");

        private static readonly string MesSystemConfigFilePath =
            Path.Combine(MesConfigRootDirectory, "MesSystemConfig", "MesSystemConfig.json");

        private static readonly string DataStructureConfigDirectory =
            Path.Combine(MesConfigRootDirectory, "DataStructure");


        private static readonly Brush SuccessBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

        private static readonly Brush WarningBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

        private static readonly Brush NeutralBrush =
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

        private const double HeaderDrawerClosedOffset = 40d;
        private static readonly Duration HeaderDrawerAnimationDuration = new(TimeSpan.FromMilliseconds(220));
        private static readonly IEasingFunction HeaderDrawerEasing = new CubicEase { EasingMode = EasingMode.EaseOut };

        private readonly Dictionary<ApiInterfaceProfile, string> _profileStorageFileNames = new();
        private ApiInterfaceProfile? _selectedProfile;
        private string _searchText = string.Empty;
        private string _pageStatusText = "等待编辑";
        private Brush _pageStatusBrush = NeutralBrush;
        private bool _isBusy;
        private bool _isHeaderDrawerOpen;
        

        public ApiConfigView()
        {
            InitializeComponent();

            MethodTypes = new ObservableCollection<ApiOptionItem>
            {
                new ApiOptionItem("TCP CLIENT", "TCP Client", "通过 TCP Client 调用接口。"),
                new ApiOptionItem("WEBAPI", "WebApi", "通过 HTTP 接口调用 MES。"),
                new ApiOptionItem("WEBSERVICE", "WebService", "通过 SOAP/XML 调用 MES。"),
                new ApiOptionItem("FTP", "FTP", "通过 FTP 上传或下载文件。"),
                new ApiOptionItem("P-INVOKE", "P-Invoke", "保留扩展调用入口。")
            };

            WebApiMethods = new ObservableCollection<ApiOptionItem>
            {
                new ApiOptionItem("POST", "POST"),
                new ApiOptionItem("GET", "GET")
            };

            ProfilesView = CollectionViewSource.GetDefaultView(Profiles);
            ProfilesView.Filter = FilterProfiles;

            DataContext = this;

            RefreshDataStructureOptions();
            int loadedCount = LoadProfilesFromDisk();
            if (loadedCount == 0)
            {
                ApiInterfaceProfile sampleProfile = CreateSampleProfile();
                AddProfile(sampleProfile);
                SetPageStatus("未发现本地接口配置，已创建默认示例。", NeutralBrush);
            }
            else
            {
                SetPageStatus($"已读取 {loadedCount} 个接口配置。", SuccessBrush);
            }

            SelectedProfile = Profiles.FirstOrDefault();
            UpdateHeaderDrawerVisual(animate: false);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ApiInterfaceProfile> Profiles { get; } = new ObservableCollection<ApiInterfaceProfile>();
        public ObservableCollection<ApiOptionItem> MethodTypes { get; }

        public ObservableCollection<ApiOptionItem> WebApiMethods { get; }

        public ObservableCollection<string> DataStructureOptions { get; } = new ObservableCollection<string>();

        public ICollectionView ProfilesView { get; }

        public ApiInterfaceProfile? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (ReferenceEquals(_selectedProfile, value))
                {
                    return;
                }
                if (value != null)
                    _selectedProfile = value;
                else _selectedProfile = _selectedProfile.Clone(_selectedProfile.ApiName);
                CloseHeaderDrawer(animate: false);
                OnPropertyChanged();
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (!SetField(ref _searchText, value))
                {
                    return;
                }
                ProfilesView.Refresh();
            }
        }
        public string PageStatusText
        {
            get => _pageStatusText;
            private set => SetField(ref _pageStatusText, value);
        }

        public Brush PageStatusBrush
        {
            get => _pageStatusBrush;
            private set => SetField(ref _pageStatusBrush, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetField(ref _isBusy, value);
        }
        
        private void NewProfileButton_Click(object sender, RoutedEventArgs e)
        {
            ApiInterfaceProfile profile = CreateDefaultProfile(GenerateUniqueName("MES 接口"));
            AddProfile(profile);
            SelectedProfile = profile;
            SetPageStatus("已新增接口配置。", SuccessBrush);
        }

        private void DuplicateProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                return;
            }

            ApiInterfaceProfile copy = SelectedProfile.Clone(GenerateCopyName(SelectedProfile.ApiName));
            AddProfile(copy);
            SelectedProfile = copy;
            SetPageStatus("已复制当前接口配置。", SuccessBrush);
        }

        private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                return;
            }

            ApiInterfaceProfile profile = SelectedProfile;
            DeleteStoredProfileFile(profile);
            profile.PropertyChanged -= Profile_PropertyChanged;
            Profiles.Remove(profile);
            SelectedProfile = Profiles.FirstOrDefault();
            SetPageStatus("已删除接口配置。", WarningBrush);
        }

        private void SaveProfilesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int savedCount = SaveProfilesToDisk();
                SetPageStatus($"已保存 {savedCount} 个接口配置。", SuccessBrush);
            }
            catch (Exception ex)
            {
                SetPageStatus($"保存失败：{ex.Message}", WarningBrush);
            }
        }

        private void RefreshStructuresButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshDataStructureOptions();
            SetPageStatus($"已刷新数据结构选项，共 {DataStructureOptions.Count} 个。", SuccessBrush);
        }

        private void AddHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                return;
            }

            ApiHeaderItem header = new ApiHeaderItem
            {
                Key = "Content-Type",
                Value = "application/json"
            };

            SelectedProfile.Heads.Add(header);
            SelectedProfile.SelectedHeader = header;
            SetPageStatus("已新增请求头。", SuccessBrush);
        }

        private void DeleteHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile?.SelectedHeader is null)
            {
                return;
            }

            ApiHeaderItem header = SelectedProfile.SelectedHeader;
            SelectedProfile.Heads.Remove(header);
            SelectedProfile.SelectedHeader = SelectedProfile.Heads.FirstOrDefault();
            SetPageStatus("已删除请求头。", WarningBrush);
        }

        private void OpenHeaderDrawerButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                return;
            }

            _isHeaderDrawerOpen = true;
            UpdateHeaderDrawerVisual(animate: true);
        }

        private void CloseHeaderDrawerButton_Click(object sender, RoutedEventArgs e)
        {
            CloseHeaderDrawer(animate: true);
        }

        private void HeaderDrawerBackdrop_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            CloseHeaderDrawer(animate: true);
        }

        private void SaveHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                return;
            }

            try
            {
                SaveProfilesToDisk(SelectedProfile);
                SetPageStatus("请求头配置已保存。", SuccessBrush);
                CloseHeaderDrawer(animate: true);
            }
            catch (Exception ex)
            {
                SetPageStatus($"请求头保存失败：{ex.Message}", WarningBrush);
            }
        }

        private void GeneratePayloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedProfile.DataStructName))
            {
                SetPageStatus("请先选择数据结构，再生成上传报文。", WarningBrush);
                return;
            }

            try
            {
                string? payload = MesDataConvert.Convert(new MesDataInfoTree(), SelectedProfile.DataStructName);
                if (string.IsNullOrWhiteSpace(payload))
                {
                    SetPageStatus($"数据结构 {SelectedProfile.DataStructName} 未生成有效报文，请检查结构内容。", WarningBrush);
                    return;
                }

                SelectedProfile.SampleRequestBody = FormatResponseText(payload);
                SetPageStatus($"已生成上传报文：{SelectedProfile.DataStructName}", SuccessBrush);
            }
            catch (Exception ex)
            {
                SetPageStatus($"生成上传报文失败：{ex.Message}", WarningBrush);
            }
        }

        private async void TestInterfaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProfile is null || IsBusy)
            {
                return;
            }

            ApiInterfaceProfile profile = SelectedProfile;

            try
            {
                ValidateProfileForSave(profile);

                if (string.IsNullOrWhiteSpace(profile.SampleRequestBody) &&
                    !string.IsNullOrWhiteSpace(profile.DataStructName))
                {
                    //profile.SampleRequestBody = generatedPayload;
                }

                EnsureMesSystemConfigExists();
                SaveProfilesToDisk(profile);

                string requestPayload = profile.SampleRequestBody ?? string.Empty;
                IsBusy = true;
                SetPageStatus($"正在测试接口：{profile.ApiName}", NeutralBrush);

                (MesResult Result, string Payload) testResult = await Task.Run(() =>
                {
                    string payloadCopy = requestPayload;
                    MesResult result = MesDataConvert.SendMES(profile.ApiName, ref payloadCopy, null);
                    return (result, payloadCopy);
                });

                profile.SampleRequestBody = testResult.Payload;
                profile.SampleResponseBody = FormatResponseText(testResult.Result.Message);

                switch (testResult.Result.State)
                {
                    case MesStatus.ResultOK:
                        SetPageStatus($"接口测试成功：{profile.ApiName}", SuccessBrush);
                        break;
                    default:
                        SetPageStatus($"接口测试完成，但返回状态为 {testResult.Result.State}。", WarningBrush);
                        break;
                }
            }
            catch (Exception ex)
            {
                profile.SampleResponseBody = ex.Message;
                SetPageStatus($"测试失败：{ex.Message}", WarningBrush);
            }
            finally
            {
                IsBusy = false;
            }
        }

        #region Lua弹框
        private bool _isCommandDrawerOpen;
        private const double CommandDrawerClosedOffset = 56d;
        private static readonly Duration CommandDrawerAnimationDuration = new Duration(TimeSpan.FromMilliseconds(220));
        private static readonly IEasingFunction CommandDrawerEasing = new CubicEase { EasingMode = EasingMode.EaseOut };
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
        private void CloseLuaDrawerButton_Click(object sender, RoutedEventArgs e)
        {
            CloseCommandDrawer();
        }
        private void CommandDrawerBackdrop_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            CloseCommandDrawer();
        }
        private void LuaButton_Click(object sender, RoutedEventArgs e)
        {
            OpenCommandDrawer();
        }
        private void CloseCommandDrawer()
        {
            IsCommandDrawerOpen = false;
            UpdateCommandDrawerVisual(animate: true);
        }
        private void OpenCommandDrawer()
        {
            IsCommandDrawerOpen = true;
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
        #endregion

        private void Profile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ApiInterfaceProfile.ApiName) or
                nameof(ApiInterfaceProfile.Remarks) or
                nameof(ApiInterfaceProfile.DataStructName) or
                nameof(ApiInterfaceProfile.SelectMESType) or
                nameof(ApiInterfaceProfile.IsEnabledAPI))
            {
                ProfilesView.Refresh();
            }

            if (e.PropertyName == nameof(ApiInterfaceProfile.SelectMESType) &&
                SelectedProfile is not null)
            {
                CloseHeaderDrawer(animate: true);
            }
        }

        private void CloseHeaderDrawer(bool animate)
        {
            _isHeaderDrawerOpen = false;
            UpdateHeaderDrawerVisual(animate);
        }

        private void UpdateHeaderDrawerVisual(bool animate)
        {
            if (HeaderDrawerHost is null || HeaderDrawerTranslateTransform is null)
            {
                return;
            }

            double targetOpacity = _isHeaderDrawerOpen ? 1d : 0d;
            double targetOffset = _isHeaderDrawerOpen ? 0d : HeaderDrawerClosedOffset;

            if (_isHeaderDrawerOpen)
            {
                HeaderDrawerHost.IsHitTestVisible = true;
            }

            if (!animate)
            {
                HeaderDrawerHost.BeginAnimation(UIElement.OpacityProperty, null);
                HeaderDrawerTranslateTransform.BeginAnimation(TranslateTransform.XProperty, null);
                HeaderDrawerHost.Opacity = targetOpacity;
                HeaderDrawerTranslateTransform.X = targetOffset;
                HeaderDrawerHost.IsHitTestVisible = _isHeaderDrawerOpen;
                return;
            }

            DoubleAnimation opacityAnimation = new()
            {
                To = targetOpacity,
                Duration = HeaderDrawerAnimationDuration,
                EasingFunction = HeaderDrawerEasing
            };

            if (!_isHeaderDrawerOpen)
            {
                opacityAnimation.Completed += (_, _) =>
                {
                    if (!_isHeaderDrawerOpen)
                    {
                        HeaderDrawerHost.IsHitTestVisible = false;
                    }
                };
            }

            DoubleAnimation translateAnimation = new()
            {
                To = targetOffset,
                Duration = HeaderDrawerAnimationDuration,
                EasingFunction = HeaderDrawerEasing
            };

            HeaderDrawerHost.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            HeaderDrawerTranslateTransform.BeginAnimation(TranslateTransform.XProperty, translateAnimation);
        }

        private void AddProfile(ApiInterfaceProfile profile)
        {
            profile.PropertyChanged += Profile_PropertyChanged;
            Profiles.Add(profile);
        }

        private int LoadProfilesFromDisk()
        {
            if (!Directory.Exists(ApiConfigDirectory))
            {
                return 0;
            }

            int loadedCount = 0;
            foreach (string filePath in Directory.EnumerateFiles(ApiConfigDirectory, "*.json").OrderBy(Path.GetFileName))
            {
                try
                {
                    APIConfig? config = JsonHelper.ReadJson<APIConfig>(filePath);
                    if (config is null)
                    {
                        continue;
                    }
                    string fallbackName = Path.GetFileNameWithoutExtension(filePath);
                    ApiInterfaceProfile profile = ApiInterfaceProfile.FromApiConfig(config, fallbackName);
                    AddProfile(profile);
                    _profileStorageFileNames[profile] = Path.GetFileName(filePath);
                    loadedCount++;
                }
                catch (Exception ex)
                {
                    SetPageStatus($"读取接口配置失败：{Path.GetFileName(filePath)}，原因：{ex.Message}", WarningBrush);
                }
            }

            return loadedCount;
        }

        
        private int SaveProfilesToDisk(ApiInterfaceProfile? apiProfile = null)
        {
            Directory.CreateDirectory(ApiConfigDirectory);
            ObservableCollection<ApiInterfaceProfile> targetProfiles = apiProfile is null ? Profiles : new ObservableCollection<ApiInterfaceProfile> { apiProfile };
            HashSet<string> usedFileNames = new(StringComparer.OrdinalIgnoreCase);
            int savedCount = 0;

            foreach (ApiInterfaceProfile profile in targetProfiles)
            {
                ValidateProfileForSave(profile);

                string fileName = BuildUniqueStorageFileName(profile.ApiName, usedFileNames);
                string filePath = Path.Combine(ApiConfigDirectory, fileName);
                JsonHelper.SaveJson(profile.ToApiConfig(), filePath);
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
        
        private static void ValidateProfileForSave(ApiInterfaceProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.ApiName))
            {
                throw new InvalidOperationException("方法名称不能为空。");
            }

            ValidatePort(profile.TCPLocalPort, "本地端口");
            ValidatePort(profile.TCPRemotePort, "远程端口");
        }

        private void RefreshDataStructureOptions()
        {
            DataStructureOptions.Clear();

            if (Directory.Exists(DataStructureConfigDirectory))
            {
                foreach (string filePath in Directory.EnumerateFiles(DataStructureConfigDirectory, "*.json").OrderBy(Path.GetFileName))
                {
                    DataStructureOptions.Add(Path.GetFileName(filePath).Replace(".json", ""));
                }
            }
        }
        private static TreeModel ConvertLayoutDocumentToTreeModel(DataStructureLayoutDocument document)
        {
            bool isWhile = document.IsWhile || document.WhileCount > 0;
            TreeModel model = CreateTreeModel(
                document.ClientCode ?? string.Empty,
                document.MesCode ?? string.Empty,
                DataStructureFieldDataTypes.Normalize(document.DataType),
                document.DefaultValue ?? string.Empty,
                isRoot: false);

            model.IsNull = document.IsNull;
            model.IsWhile = isWhile;
            model.WhileCount = isWhile && document.WhileCount <= 0 ? 1 : Math.Max(0, document.WhileCount);
            model.KeepDecimalLength = document.KeepCount.ToString(CultureInfo.InvariantCulture);
            model.XMLNameSpace = document.XmlNamespace ?? string.Empty;
            model.JudgeValue = document.JudgeValue ?? string.Empty;
            model.OKText = document.OKText ?? string.Empty;
            model.NGText = document.NGText ?? string.Empty;

            foreach (DataStructureLayoutDocument child in document.Children ?? Enumerable.Empty<DataStructureLayoutDocument>())
            {
                model.Children.Add(ConvertLayoutDocumentToTreeModel(child));
            }

            return model;
        }

        private static TreeModel CreateTreeModel(
            string clientCode,
            string mesCode,
            string dataType,
            string defaultValue,
            bool isRoot)
        {
            return new TreeModel
            {
                ClientCode = clientCode,
                MESCode = mesCode,
                DataType = dataType,
                DefectValue = defaultValue,
                KeepDecimalLength = "0",
                XMLNameSpace = string.Empty,
                JudgeValue = string.Empty,
                OKText = string.Empty,
                NGText = string.Empty
            };
        }

        private static void NormalizeTreeModel(TreeModel model, bool isRoot)
        {
            model.ClientCode ??= string.Empty;
            model.MESCode ??= string.Empty;
            model.DataType = isRoot
                ? NormalizeRootDataType(model.DataType)
                : DataStructureFieldDataTypes.Normalize(model.DataType);
            model.DefectValue ??= string.Empty;
            model.KeepDecimalLength = string.IsNullOrWhiteSpace(model.KeepDecimalLength)
                ? "0"
                : model.KeepDecimalLength;
            model.XMLNameSpace ??= string.Empty;
            model.JudgeValue ??= string.Empty;
            model.OKText ??= string.Empty;
            model.NGText ??= string.Empty;
            model.Children ??= new List<TreeModel>();

            foreach (TreeModel child in model.Children)
            {
                NormalizeTreeModel(child, isRoot: false);
            }
        }

        private static string NormalizeRootDataType(string? dataType)
        {
            if (string.IsNullOrWhiteSpace(dataType))
            {
                return "JSON";
            }

            return dataType.Trim().ToUpperInvariant() switch
            {
                "SAOP" or "SOAP" or "XML" => "SOAP",
                "JOIN" or "JOINT" => "JOINT",
                "JSONREMOVEQUE" => "JSONREMOVEQUE",
                _ => "JSON"
            };
        }

        private static void ValidatePort(string value, string displayName)
        {
            if (!ushort.TryParse(value?.Trim(), out _))
            {
                throw new InvalidOperationException($"{displayName} 必须是 0-65535 之间的整数。");
            }
        }

        private void DeleteStoredProfileFile(ApiInterfaceProfile profile)
        {
            if (!_profileStorageFileNames.TryGetValue(profile, out string? fileName))
            {
                return;
            }

            TryDeleteStorageFile(fileName);
            _profileStorageFileNames.Remove(profile);
        }

        private static void TryDeleteStorageFile(string fileName)
        {
            try
            {
                string filePath = Path.Combine(ApiConfigDirectory, fileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
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
            string sourceValue = string.IsNullOrWhiteSpace(value) ? "ApiConfig" : value.Trim();
            HashSet<char> invalidChars = new(Path.GetInvalidFileNameChars());
            StringBuilder builder = new(sourceValue.Length);
            foreach (char current in sourceValue)
            {
                builder.Append(invalidChars.Contains(current) || char.IsControl(current)
                    ? '_'
                    : char.IsWhiteSpace(current) ? '_' : current);
            }

            string safeName = builder.ToString().Trim(' ', '.');
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "ApiConfig";
            }

            return safeName.Length <= 80 ? safeName : safeName[..80];
        }
        private string GenerateUniqueName(string prefix)
        {
            for (int index = 1; ; index++)
            {
                string name = $"{prefix} {index}";
                if (!Profiles.Any(profile => string.Equals(profile.ApiName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return name;
                }
            }
        }

        private string GenerateCopyName(string baseName)
        {
            string prefix = string.IsNullOrWhiteSpace(baseName) ? "MES 接口" : baseName.Trim();
            string firstName = $"{prefix} 副本";
            if (!Profiles.Any(profile => string.Equals(profile.ApiName, firstName, StringComparison.OrdinalIgnoreCase)))
            {
                return firstName;
            }

            for (int index = 2; ; index++)
            {
                string name = $"{firstName} {index}";
                if (!Profiles.Any(profile => string.Equals(profile.ApiName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return name;
                }
            }
        }

        private ApiInterfaceProfile CreateSampleProfile()
        {
            ApiInterfaceProfile profile = CreateDefaultProfile("工站状态上报");
            profile.Remarks = "默认示例接口，用于快速补齐 WebApi 调用参数。";
            profile.ResultCheck = "\"code\":200";
            profile.Url = "https://mes.example.com/api/report";
            profile.TokenUrl = "https://mes.example.com/api/token";
            profile.TokenName = "accessToken";
            profile.Heads.Clear();
            profile.Heads.Add(new ApiHeaderItem { Key = "Content-Type", Value = "application/json" });
            profile.Heads.Add(new ApiHeaderItem { Key = "Authorization", Value = "ACCESS_TOKEN" });
            profile.SelectedHeader = profile.Heads.FirstOrDefault();
            return profile;
        }

        private static ApiInterfaceProfile CreateDefaultProfile(string name)
        {
            ApiInterfaceProfile profile = new ApiInterfaceProfile
            {
                ApiName = name,
                SelectMESType = "WEBAPI",
                IsEnabledAPI = true,
                IsCommunicationQueryVisible = true,
                WebApiType = "POST",
                Lua = $"return SendMES(\"{name}\")"
            };

            profile.Heads.Add(new ApiHeaderItem { Key = "Content-Type", Value = "application/json" });
            profile.SelectedHeader = profile.Heads.FirstOrDefault();
            return profile;
        }

        private void EnsureMesSystemConfigExists()
        {
            if (File.Exists(MesSystemConfigFilePath))
            {
                return;
            }

            JsonHelper.SaveJson(new MesSystemConfig(), MesSystemConfigFilePath);
        }

        private static string FormatResponseText(string? responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return string.Empty;
            }

            string trimmed = responseText.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                return trimmed.ToJsonFormat();
            }

            if (trimmed.StartsWith("<", StringComparison.Ordinal))
            {
                return trimmed.ToXMLFormat();
            }

            return responseText;
        }

        private bool FilterProfiles(object item)
        {
            if (item is not ApiInterfaceProfile profile)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                return true;
            }

            string keyword = SearchText.Trim();
            return Contains(profile.ApiName, keyword) ||
                   Contains(profile.Remarks, keyword) ||
                   Contains(profile.DataStructName, keyword) ||
                   Contains(profile.TypeDisplayName, keyword) ||
                   Contains(profile.Summary, keyword);
        }

        private static bool Contains(string? source, string keyword)
        {
            return source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetPageStatus(string text, Brush brush)
        {
            PageStatusText = text;
            PageStatusBrush = brush;
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }
}
