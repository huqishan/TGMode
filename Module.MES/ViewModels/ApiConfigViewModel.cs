using ControlLibrary;
using ControlLibrary.Models.MediatorModels.MES;
using Module.MES.ViewModels.PropertyVMs;
using Shared.Infrastructure.Extensions;
using Shared.Infrastructure.Mediator;
using Shared.Infrastructure.PackMethod;
using Shared.Models.MES;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.MES.ViewModels;

public sealed class ApiConfigViewModel : ViewModelProperties
{
    #region 配置路径字段

    private static readonly string MesConfigRootDirectory =
        Path.Combine(AppContext.BaseDirectory, "Config", "MES_Config");

    private static readonly string ApiConfigDirectory =
        Path.Combine(MesConfigRootDirectory, "ApiConfig");

    private static readonly string MesSystemConfigFilePath =
        Path.Combine(MesConfigRootDirectory, "MesSystemConfig", "MesSystemConfig.json");

    private static readonly string DataStructureConfigDirectory =
        Path.Combine(MesConfigRootDirectory, "DataStructure");

    #endregion

    #region 状态颜色字段

    private static readonly Brush SuccessBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

    private static readonly Brush WarningBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EA580C"));

    private static readonly Brush NeutralBrush =
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

    #endregion

    #region 抽屉布局字段

    private const double HeaderDrawerClosedOffset = 40d;

    #endregion

    #region 私有状态字段

    private readonly Dictionary<ApiInterfaceProfile, string> _profileStorageFileNames = new();
    private ApiInterfaceProfile? _selectedProfile;
    private string _searchText = string.Empty;
    private string _pageStatusText = "等待编辑";
    private Brush _pageStatusBrush = NeutralBrush;
    private bool _isBusy;
    private bool _isHeaderDrawerOpen;
    private readonly IMediator _mediator;

    #endregion

    #region 集合属性

    public ObservableCollection<ApiInterfaceProfile> Profiles { get; } = new();

    public ObservableCollection<ApiOptionItem> MethodTypes { get; } = new();

    public ObservableCollection<ApiOptionItem> WebApiMethods { get; } = new();

    public ObservableCollection<string> DataStructureOptions { get; } = new();

    public ICollectionView ProfilesView { get; private set; } = null!;

    #endregion

    #region 当前编辑属性

    public ApiInterfaceProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (ReferenceEquals(_selectedProfile, value))
            {
                return;
            }

            _selectedProfile = value;
            CloseHeaderDrawer();
            OnPropertyChanged();
            RaiseCommandStatesChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value ?? string.Empty))
            {
                return;
            }

            ProfilesView.Refresh();
        }
    }

    #endregion

    #region 页面状态属性

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
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RaiseCommandStatesChanged();
            }
        }
    }

    #endregion

    #region 请求头抽屉属性

    public bool IsHeaderDrawerOpen
    {
        get => _isHeaderDrawerOpen;
        private set
        {
            if (!SetField(ref _isHeaderDrawerOpen, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HeaderDrawerOpacity));
            OnPropertyChanged(nameof(HeaderDrawerOffset));
        }
    }

    public double HeaderDrawerOpacity => IsHeaderDrawerOpen ? 1d : 0d;

    public double HeaderDrawerOffset => IsHeaderDrawerOpen ? 0d : HeaderDrawerClosedOffset;

    #endregion

    #region 接口配置命令

    public ICommand NewProfileCommand { get; private set; } = null!;

    public ICommand DuplicateProfileCommand { get; private set; } = null!;

    public ICommand DeleteProfileCommand { get; private set; } = null!;

    public ICommand SaveProfilesCommand { get; private set; } = null!;

    public ICommand RefreshStructuresCommand { get; private set; } = null!;

    public ICommand GeneratePayloadCommand { get; private set; } = null!;

    public ICommand TestInterfaceCommand { get; private set; } = null!;

    #endregion

    #region 请求头命令

    public ICommand OpenHeaderDrawerCommand { get; private set; } = null!;

    public ICommand CloseHeaderDrawerCommand { get; private set; } = null!;

    public ICommand AddHeaderCommand { get; private set; } = null!;

    public ICommand DeleteHeaderCommand { get; private set; } = null!;

    public ICommand SaveHeaderCommand { get; private set; } = null!;

    #endregion

    #region 构造与初始化

    public ApiConfigViewModel(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        InitializeOptionItems();
        InitializeCommands();

        ProfilesView = CollectionViewSource.GetDefaultView(Profiles);
        ProfilesView.Filter = FilterProfiles;

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
        CloseHeaderDrawer();
    }

    private void InitializeOptionItems()
    {
        MethodTypes.Add(new ApiOptionItem("TCP CLIENT", "TCP Client", "通过 TCP Client 调用接口。"));
        MethodTypes.Add(new ApiOptionItem("WEBAPI", "WebApi", "通过 HTTP 接口调用 MES。"));
        MethodTypes.Add(new ApiOptionItem("WEBSERVICE", "WebService", "通过 SOAP/XML 调用 MES。"));
        MethodTypes.Add(new ApiOptionItem("FTP", "FTP", "通过 FTP 上传或下载文件。"));
        MethodTypes.Add(new ApiOptionItem("P-INVOKE", "P-Invoke", "保留扩展调用入口。"));

        WebApiMethods.Add(new ApiOptionItem("POST", "POST"));
        WebApiMethods.Add(new ApiOptionItem("GET", "GET"));
    }

    private void InitializeCommands()
    {
        NewProfileCommand = new RelayCommand(_ => NewProfile());
        DuplicateProfileCommand = new RelayCommand(_ => DuplicateProfile(), _ => HasSelectedProfile());
        DeleteProfileCommand = new RelayCommand(_ => DeleteProfile(), _ => HasSelectedProfile());
        SaveProfilesCommand = new RelayCommand(_ => SaveProfiles());
        RefreshStructuresCommand = new RelayCommand(_ => RefreshStructures());
        GeneratePayloadCommand = new RelayCommand(_ => GeneratePayload(), _ => HasSelectedProfile());
        TestInterfaceCommand = new AsyncRelayCommand(_ => TestInterfaceAsync(), _ => HasSelectedProfile() && !IsBusy);

        OpenHeaderDrawerCommand = new RelayCommand(_ => OpenHeaderDrawer(), _ => HasSelectedProfile());
        CloseHeaderDrawerCommand = new RelayCommand(_ => CloseHeaderDrawer());
        AddHeaderCommand = new RelayCommand(_ => AddHeader(), _ => HasSelectedProfile());
        DeleteHeaderCommand = new RelayCommand(_ => DeleteHeader(), _ => SelectedProfile?.SelectedHeader is not null);
        SaveHeaderCommand = new RelayCommand(_ => SaveHeader(), _ => HasSelectedProfile());
    }

    #endregion

    #region 接口配置命令方法

    private void NewProfile()
    {
        ApiInterfaceProfile profile = CreateDefaultProfile(GenerateUniqueName("MES接口"));
        AddProfile(profile);
        SelectedProfile = profile;
        SetPageStatus("已新增接口配置。", SuccessBrush);
    }

    private void DuplicateProfile()
    {
        if (SelectedProfile is null)
        {
            SetPageStatus("请先选择需要复制的接口配置。", WarningBrush);
            return;
        }

        ApiInterfaceProfile copy = SelectedProfile.Clone(GenerateCopyName(SelectedProfile.ApiName));
        AddProfile(copy);
        SelectedProfile = copy;
        SetPageStatus("已复制当前接口配置。", SuccessBrush);
    }

    private void DeleteProfile()
    {
        if (SelectedProfile is null)
        {
            SetPageStatus("请先选择需要删除的接口配置。", WarningBrush);
            return;
        }

        int selectedIndex = Profiles.IndexOf(SelectedProfile);
        ApiInterfaceProfile profile = SelectedProfile;
        DeleteStoredProfileFile(profile);
        profile.PropertyChanged -= Profile_PropertyChanged;
        Profiles.Remove(profile);

        if (Profiles.Count == 0)
        {
            AddProfile(CreateDefaultProfile(GenerateUniqueName("MES 接口")));
        }

        SelectedProfile = Profiles[Math.Clamp(selectedIndex, 0, Profiles.Count - 1)];
        SetPageStatus("已删除接口配置。", WarningBrush);
    }

    private void SaveProfiles()
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

    private void RefreshStructures()
    {
        RefreshDataStructureOptions();
        SetPageStatus($"已刷新数据结构选项，共 {DataStructureOptions.Count} 个。", SuccessBrush);
    }

    private void GeneratePayload()
    {
        if (SelectedProfile is null)
        {
            SetPageStatus("请先选择接口配置。", WarningBrush);
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

    private async Task TestInterfaceAsync()
    {
        if (SelectedProfile is null || IsBusy)
        {
            return;
        }

        ApiInterfaceProfile profile = SelectedProfile;

        try
        {
            ValidateProfileForSave(profile);
            EnsureMesSystemConfigExists();
            SaveProfilesToDisk(profile);

            string requestPayload = profile.SampleRequestBody ?? string.Empty;
            IsBusy = true;
            SetPageStatus($"正在测试接口：{profile.ApiName}", NeutralBrush);

            ExecuteMesResponse testResult =
                await _mediator.Send(new ExecuteMesRequest(profile.ApiName, requestPayload));

            profile.SampleRequestBody = testResult.RequestPayload;
            profile.SampleResponseBody = FormatResponseText(testResult.Result.Message);

            if (testResult.Result.State == MesStatus.ResultOK)
            {
                SetPageStatus($"接口测试成功：{profile.ApiName}", SuccessBrush);
                return;
            }

            SetPageStatus($"接口测试完成，但返回状态为 {testResult.Result.State}。", WarningBrush);
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

    #endregion

    #region 请求头抽屉方法

    private void OpenHeaderDrawer()
    {
        if (SelectedProfile is null)
        {
            SetPageStatus("请先选择接口配置。", WarningBrush);
            return;
        }

        IsHeaderDrawerOpen = true;
    }

    private void CloseHeaderDrawer()
    {
        IsHeaderDrawerOpen = false;
    }

    private void AddHeader()
    {
        if (SelectedProfile is null)
        {
            SetPageStatus("请先选择接口配置。", WarningBrush);
            return;
        }

        ApiHeaderItem header = new()
        {
            Key = "Content-Type",
            Value = "application/json"
        };

        SelectedProfile.Heads.Add(header);
        SelectedProfile.SelectedHeader = header;
        SetPageStatus("已新增请求头。", SuccessBrush);
        RaiseCommandStatesChanged();
    }

    private void DeleteHeader()
    {
        if (SelectedProfile?.SelectedHeader is null)
        {
            SetPageStatus("请先选择需要删除的请求头。", WarningBrush);
            return;
        }

        ApiHeaderItem header = SelectedProfile.SelectedHeader;
        SelectedProfile.Heads.Remove(header);
        SelectedProfile.SelectedHeader = SelectedProfile.Heads.FirstOrDefault();
        SetPageStatus("已删除请求头。", WarningBrush);
        RaiseCommandStatesChanged();
    }

    private void SaveHeader()
    {
        if (SelectedProfile is null)
        {
            SetPageStatus("请先选择接口配置。", WarningBrush);
            return;
        }

        try
        {
            SaveProfilesToDisk(SelectedProfile);
            SetPageStatus("请求头配置已保存。", SuccessBrush);
            CloseHeaderDrawer();
        }
        catch (Exception ex)
        {
            SetPageStatus($"请求头保存失败：{ex.Message}", WarningBrush);
        }
    }

    #endregion

    #region 配置持久化方法

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
        IEnumerable<ApiInterfaceProfile> targetProfiles = apiProfile is null
            ? Profiles
            : new[] { apiProfile };
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

    #endregion

    #region 数据结构与报文方法

    private void RefreshDataStructureOptions()
    {
        DataStructureOptions.Clear();

        if (!Directory.Exists(DataStructureConfigDirectory))
        {
            return;
        }

        foreach (string filePath in Directory.EnumerateFiles(DataStructureConfigDirectory, "*.json").OrderBy(Path.GetFileName))
        {
            DataStructureOptions.Add(Path.GetFileName(filePath).Replace(".json", ""));
        }
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

    #endregion

    #region 配置工厂方法

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
        profile.SelectedHeader = profile.Heads.FirstOrDefault();
        return profile;
    }

    private static ApiInterfaceProfile CreateDefaultProfile(string name)
    {
        ApiInterfaceProfile profile = new()
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

    private string GenerateUniqueName(string prefix)
    {
        for (int index = 1; ; index++)
        {
            string name = $"{prefix}{index}";
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

    #endregion

    #region 列表过滤与状态刷新方法

    private void Profile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ApiInterfaceProfile.ApiName) or
            nameof(ApiInterfaceProfile.Remarks) or
            nameof(ApiInterfaceProfile.DataStructName) or
            nameof(ApiInterfaceProfile.SelectMESType) or
            nameof(ApiInterfaceProfile.IsEnabledAPI) or
            nameof(ApiInterfaceProfile.Summary))
        {
            ProfilesView.Refresh();
        }

        if (e.PropertyName == nameof(ApiInterfaceProfile.SelectMESType))
        {
            CloseHeaderDrawer();
        }

        if (e.PropertyName is nameof(ApiInterfaceProfile.SelectedHeader) or
            nameof(ApiInterfaceProfile.HasSelectedHeader))
        {
            RaiseCommandStatesChanged();
        }
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

    private bool HasSelectedProfile()
    {
        return SelectedProfile is not null;
    }

    private void RaiseCommandStatesChanged()
    {
        RaiseCommandState(DuplicateProfileCommand);
        RaiseCommandState(DeleteProfileCommand);
        RaiseCommandState(GeneratePayloadCommand);
        RaiseCommandState(TestInterfaceCommand);
        RaiseCommandState(OpenHeaderDrawerCommand);
        RaiseCommandState(AddHeaderCommand);
        RaiseCommandState(DeleteHeaderCommand);
        RaiseCommandState(SaveHeaderCommand);
    }

    private static void RaiseCommandState(ICommand? command)
    {
        switch (command)
        {
            case RelayCommand relayCommand:
                relayCommand.RaiseCanExecuteChanged();
                break;
            case AsyncRelayCommand asyncRelayCommand:
                asyncRelayCommand.RaiseCanExecuteChanged();
                break;
        }
    }

    #endregion
}
