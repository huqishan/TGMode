using ControlLibrary;
using ControlLibrary.Models.MediatorModels.Communication;
using Module.Communication.Features.DeviceCommunicationConfig.Models;
using Module.Communication.Services;
using Shared.Abstractions.Enum;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;

namespace WpfApp.ViewModels;

/// <summary>
/// 首页生产总览数据与界面状态。
/// </summary>
public sealed class HomeViewModel : ViewModelProperties
{
    #region 首页常量与状态

    /// <summary>小时统计周期的内部标识。</summary>
    private const string HourlyPeriod = "Hourly";

    /// <summary>班次统计周期的内部标识。</summary>
    private const string ShiftPeriod = "Shift";

    /// <summary>保存当前选中的统计周期。</summary>
    private string _selectedPeriod = HourlyPeriod;

    /// <summary>设备初始化流程是否正在执行。</summary>
    private bool _isBusy;

    /// <summary>首页设备状态弹框当前是否打开。</summary>
    private bool _isDeviceStatusPopupOpen;

    /// <summary>保存通讯服务实例，用于加载配置及跟踪实时连接状态。</summary>
    private readonly CommunicationService _communicationService;

    /// <summary>按设备名称索引首页状态项，便于连接事件快速更新。</summary>
    private readonly Dictionary<string, HomeDeviceStatusItem> _deviceItemsByName = new(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region 初始化与绑定集合

    /// <summary>
    /// 初始化首页指标和产能图表，并异步加载设备配置及连接状态。
    /// </summary>
    public HomeViewModel(CommunicationService communicationService)
    {
        _communicationService = communicationService ?? throw new ArgumentNullException(nameof(communicationService));
        DateText = DateTime.Now.ToString("yyyy年MM月dd日", CultureInfo.InvariantCulture);
        LastUpdatedText = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        SelectPeriodCommand = new RelayCommand(parameter =>
        {
            SelectedPeriod = string.Equals(parameter?.ToString(), ShiftPeriod, StringComparison.Ordinal)
                ? ShiftPeriod
                : HourlyPeriod;
        });
        ToggleDeviceStatusPopupCommand = new RelayCommand(_ =>
        {
            IsDeviceStatusPopupOpen = !IsDeviceStatusPopupOpen;
        });
        CloseDeviceStatusPopupCommand = new RelayCommand(_ => IsDeviceStatusPopupOpen = false);

        ProductionItems = new ObservableCollection<HomeProductionBar>();
        AxisLabels = new ObservableCollection<string>();
        Devices = new ObservableCollection<HomeDeviceStatusItem>();
        _communicationService.DeviceConnectionStateChanged += CommunicationService_DeviceConnectionStateChanged;

        RebuildProductionItems();
        _ = InitializeDevicesAsync();
    }

    /// <summary>产能图表当前展示的 OK/NG 时段数据。</summary>
    public ObservableCollection<HomeProductionBar> ProductionItems { get; }

    /// <summary>产能图表纵轴从高到低显示的刻度文本。</summary>
    public ObservableCollection<string> AxisLabels { get; }

    /// <summary>首页展示的关键设备连接状态。</summary>
    public ObservableCollection<HomeDeviceStatusItem> Devices { get; }

    /// <summary>切换小时与班次统计的命令。</summary>
    public ICommand SelectPeriodCommand { get; }

    /// <summary>切换首页设备状态弹框显示状态的命令。</summary>
    public ICommand ToggleDeviceStatusPopupCommand { get; }

    /// <summary>关闭首页设备状态弹框的命令。</summary>
    public ICommand CloseDeviceStatusPopupCommand { get; }

    #endregion

    #region 页面指标

    /// <summary>当前页面显示的统计日期。</summary>
    public string DateText { get; }

    /// <summary>首页数据最近一次更新的时间文本。</summary>
    public string LastUpdatedText { get; }

    /// <summary>今日累计检测数量。</summary>
    public int ProductionTotal => 12864;

    /// <summary>今日计划产量。</summary>
    public int ProductionTarget => 15000;

    /// <summary>今日 OK 数量。</summary>
    public int OkCount => 12699;

    /// <summary>今日 NG 数量。</summary>
    public int NgCount => 165;

    /// <summary>今日 OK 占比文本。</summary>
    public string OkRateText => "98.72%";

    /// <summary>今日 NG 占比文本。</summary>
    public string NgRateText => "1.28%";

    /// <summary>与昨日同期相比的产量变化文本。</summary>
    public string ProductionChangeText => "↑ 8.6%";

    /// <summary>与昨日相比的不良率变化文本。</summary>
    public string NgChangeText => "较昨日 -0.3%";

    /// <summary>今日产量目标完成比例，取值范围为零到一。</summary>
    public double TargetCompletionRatio => ProductionTotal / (double)ProductionTarget;

    /// <summary>目标产量的辅助显示文本。</summary>
    public string TargetText => $"目标 {ProductionTarget:N0}";

    /// <summary>在线设备数量。</summary>
    public int OnlineDeviceCount => Devices.Count(device => device.IsOnline);

    /// <summary>当前设备管理中已配置的设备总数。</summary>
    public int TotalDeviceCount => Devices.Count;

    /// <summary>已完成初始化但当前未连接的设备数量。</summary>
    public int OfflineDeviceCount => Devices.Count(device => device.IsInitialized && !device.IsOnline);

    /// <summary>首页设备卡片的连接初始化状态文本。</summary>
    public string DeviceInitializationStatusText
    {
        get
        {
            if (IsBusy)
            {
                return "设备连接初始化中";
            }

            if (Devices.Count == 0)
            {
                return "设备管理中暂无设备";
            }

            return OfflineDeviceCount == 0
                ? "全部设备已连接"
                : $"{OfflineDeviceCount} 台设备未连接";
        }
    }

    /// <summary>显示初始化过程或当前离线设备数量。</summary>
    public string OfflineDeviceSummaryText => IsBusy
        ? "初始化中"
        : OfflineDeviceCount == 0 ? "全部正常" : $"离线 {OfflineDeviceCount} 台";

    /// <summary>设备连接初始化任务是否正在后台执行。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(DeviceInitializationStatusText));
                OnPropertyChanged(nameof(OfflineDeviceSummaryText));
            }
        }
    }

    /// <summary>首页设备状态弹框是否处于打开状态。</summary>
    public bool IsDeviceStatusPopupOpen
    {
        get => _isDeviceStatusPopupOpen;
        set => SetField(ref _isDeviceStatusPopupOpen, value);
    }

    /// <summary>生产线名称。</summary>
    public string LineName => "一号装配线";

    /// <summary>当前班次名称。</summary>
    public string ShiftName => "班次 A";

    /// <summary>图表下方显示的峰值产能说明。</summary>
    public string PeakProductionText { get; private set; } = string.Empty;

    /// <summary>图表图例显示的目标产能说明。</summary>
    public string ChartTargetText { get; private set; } = string.Empty;

    /// <summary>图表目标线相对于绘图区顶边的显示位置。</summary>
    public Thickness TargetLineMargin => new(0, IsShiftSelected ? 0 : 40, 0, 0);

    /// <summary>当前选中的产能统计周期。</summary>
    public string SelectedPeriod
    {
        get => _selectedPeriod;
        set
        {
            if (!SetField(ref _selectedPeriod, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsHourlySelected));
            OnPropertyChanged(nameof(IsShiftSelected));
            RebuildProductionItems();
        }
    }

    /// <summary>小时统计切换项是否选中。</summary>
    public bool IsHourlySelected => string.Equals(SelectedPeriod, HourlyPeriod, StringComparison.Ordinal);

    /// <summary>班次统计切换项是否选中。</summary>
    public bool IsShiftSelected => string.Equals(SelectedPeriod, ShiftPeriod, StringComparison.Ordinal);

    #endregion

    #region 设备初始化与状态同步

    /// <summary>
    /// 后台读取设备配置并逐台启动通信，保证网络等待不会阻塞首页显示。
    /// </summary>
    private async Task InitializeDevicesAsync()
    {
        IsBusy = true;
        try
        {
            IReadOnlyList<DeviceCommunicationProfile> profiles = await Task.Run(
                _communicationService.GetConfiguredDeviceProfiles);

            foreach (DeviceCommunicationProfile profile in profiles)
            {
                HomeDeviceStatusItem item = new(
                    profile.LocalName,
                    $"{profile.TypeDisplayName} · {profile.Summary}",
                    profile.TypeDisplayName);
                Devices.Add(item);
                _deviceItemsByName[profile.LocalName] = item;
            }

            OnPropertyChanged(nameof(TotalDeviceCount));
            OnPropertyChanged(nameof(OnlineDeviceCount));
            OnPropertyChanged(nameof(OfflineDeviceCount));
            OnPropertyChanged(nameof(DeviceInitializationStatusText));
                OnPropertyChanged(nameof(OfflineDeviceSummaryText));

            if (profiles.Count == 0)
            {
                return;
            }

            (DeviceInitializationResult Result,
                IReadOnlyDictionary<string, ConnectState> States,
                DateTime SnapshotAt) initialization = await Task.Run(() =>
                {
                    DeviceInitializationResult result = _communicationService.InitializeAllDevices();
                    IReadOnlyDictionary<string, ConnectState> states =
                        _communicationService.GetActiveDeviceConnectionStates();
                    return (result, states, DateTime.Now);
                });

            foreach (DeviceExecutionActionResult result in initialization.Result.DeviceResults)
            {
                if (!_deviceItemsByName.TryGetValue(result.DeviceName, out HomeDeviceStatusItem? item))
                {
                    continue;
                }

                bool isOnline = initialization.States.TryGetValue(result.DeviceName, out ConnectState connectState)
                    ? connectState == ConnectState.Connected
                    : false;
                item.UpdateConnectionState(isOnline, initialization.SnapshotAt, result.Message);
            }

            OnPropertyChanged(nameof(OnlineDeviceCount));
            OnPropertyChanged(nameof(OfflineDeviceCount));
            OnPropertyChanged(nameof(DeviceInitializationStatusText));
            OnPropertyChanged(nameof(OfflineDeviceSummaryText));
        }
        catch (Exception ex)
        {
            foreach (HomeDeviceStatusItem item in Devices)
            {
                item.UpdateConnectionState(false, DateTime.Now, ex.Message);
            }

            OnPropertyChanged(nameof(OnlineDeviceCount));
            OnPropertyChanged(nameof(OfflineDeviceCount));
            OnPropertyChanged(nameof(DeviceInitializationStatusText));
            OnPropertyChanged(nameof(OfflineDeviceSummaryText));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 接收通讯服务的连接变化，并切回 WPF 调度线程更新绑定数据。
    /// </summary>
    private void CommunicationService_DeviceConnectionStateChanged(object? sender, DeviceConnectionStateChangedEventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_deviceItemsByName.TryGetValue(e.DeviceName, out HomeDeviceStatusItem? item))
            {
                return;
            }

            bool isOnline = e.ConnectState == ConnectState.Connected;
            item.UpdateConnectionState(isOnline, e.ChangedAt, isOnline ? "连接正常" : "连接中断");
            OnPropertyChanged(nameof(OnlineDeviceCount));
            OnPropertyChanged(nameof(OfflineDeviceCount));
            OnPropertyChanged(nameof(DeviceInitializationStatusText));
            OnPropertyChanged(nameof(OfflineDeviceSummaryText));
        }));
    }

    #endregion

    #region 图表数据更新

    /// <summary>
    /// 根据当前统计周期重新生成柱状图数据、坐标刻度和峰值说明。
    /// </summary>
    private void RebuildProductionItems()
    {
        bool isShiftView = string.Equals(SelectedPeriod, ShiftPeriod, StringComparison.Ordinal);
        string[] labels = isShiftView
            ? ["A班", "B班", "C班"]
            : ["08:00", "09:00", "10:00", "11:00", "12:00", "13:00", "14:00", "15:00", "16:00", "17:00", "18:00", "19:00"];
        int[] totals = isShiftView
            ? [5640, 4680, 2544]
            : [840, 940, 1090, 1190, 1290, 1416, 1250, 1160, 1070, 970, 888, 760];
        int[] ngCounts = isShiftView
            ? [72, 61, 32]
            : [12, 14, 15, 14, 16, 18, 16, 15, 14, 14, 12, 5];
        int axisMaximum = isShiftView ? 6000 : 1500;

        ProductionItems.Clear();
        AxisLabels.Clear();
        for (int index = 0; index < labels.Length; index++)
        {
            int total = totals[index];
            int ng = ngCounts[index];
            double heightPerCount = 240 * 0.985 / axisMaximum;
            double totalHeight = total * heightPerCount;
            double ngHeight = Math.Max(4, ng * heightPerCount);

            ProductionItems.Add(new HomeProductionBar(
                labels[index],
                Math.Max(0, totalHeight - ngHeight),
                ngHeight,
                total,
                ng));
        }

        for (int index = 4; index >= 0; index--)
        {
            AxisLabels.Add((axisMaximum * index / 4).ToString("N0", CultureInfo.InvariantCulture));
        }

        PeakProductionText = isShiftView
            ? "产量峰值 A班 · 5,640 件"
            : "产量峰值 13:00 · 1,416 件";
        ChartTargetText = isShiftView
            ? "目标产能 6,000 件/班"
            : "目标产能 1,250 件/时";
        OnPropertyChanged(nameof(PeakProductionText));
        OnPropertyChanged(nameof(ChartTargetText));
        OnPropertyChanged(nameof(TargetLineMargin));
    }

    #endregion
}

/// <summary>
/// 首页柱状图中单个小时或班次的 OK/NG 产能数据。
/// </summary>
public sealed class HomeProductionBar
{
    #region 初始化

    /// <summary>创建柱状图项并保存显示标签、绘制高度和统计数量。</summary>
    public HomeProductionBar(string label, double okHeight, double ngHeight, int totalCount, int ngCount)
    {
        Label = label;
        OkHeight = okHeight;
        NgHeight = ngHeight;
        TotalCount = totalCount;
        NgCount = ngCount;
    }

    #endregion

    #region 图表绑定属性

    /// <summary>柱状图横轴显示的时间或班次标签。</summary>
    public string Label { get; }

    /// <summary>OK 柱段绘制高度。</summary>
    public double OkHeight { get; }

    /// <summary>NG 柱段绘制高度。</summary>
    public double NgHeight { get; }

    /// <summary>该时段的总产能。</summary>
    public int TotalCount { get; }

    /// <summary>该时段的 NG 数量。</summary>
    public int NgCount { get; }

    #endregion
}

/// <summary>
/// 首页设备连接卡片展示的数据。
/// </summary>
public sealed class HomeDeviceStatusItem : ViewModelProperties
{
    #region 初始化

    /// <summary>创建设备状态项并保存设备名称、通信说明和通信类型。</summary>
    public HomeDeviceStatusItem(string name, string description, string communicationType)
    {
        Name = name;
        Description = description;
        CommunicationType = communicationType;
    }

    /// <summary>更新设备连接状态、最近变化时间及状态说明。</summary>
    public void UpdateConnectionState(bool isOnline, DateTime changedAt, string connectionDetail)
    {
        if (changedAt < _lastChangedAt)
        {
            return;
        }

        _lastChangedAt = changedAt;
        IsInitialized = true;
        IsOnline = isOnline;
        UpdateText = changedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        ConnectionDetail = connectionDetail;
        OnPropertyChanged(nameof(StatusText));
    }

    #endregion

    #region 设备卡片绑定属性

    /// <summary>设备是否已完成首次通信初始化。</summary>
    private bool _isInitialized;

    /// <summary>设备当前是否处于在线状态。</summary>
    private bool _isOnline;

    /// <summary>设备最近一次状态变化时间文本。</summary>
    private string _updateText = "--";

    /// <summary>设备连接状态对应的详细说明。</summary>
    private string _connectionDetail = "等待初始化";

    /// <summary>该设备最后一次已应用状态的时间戳，避免旧事件覆盖新快照。</summary>
    private DateTime _lastChangedAt;

    /// <summary>设备或通信节点名称。</summary>
    public string Name { get; }

    /// <summary>设备型号及用途说明。</summary>
    public string Description { get; }

    /// <summary>设备当前是否处于在线状态。</summary>
    public bool IsOnline
    {
        get => _isOnline;
        private set => SetField(ref _isOnline, value);
    }

    /// <summary>设备是否已完成首次初始化。</summary>
    public bool IsInitialized
    {
        get => _isInitialized;
        private set => SetField(ref _isInitialized, value);
    }

    /// <summary>根据初始化和连接状态生成设备状态文本。</summary>
    public string StatusText => !IsInitialized ? "初始化中" : IsOnline ? "在线" : "离线";

    /// <summary>设备卡片底部显示的通信类型。</summary>
    public string MetricLabel => "通信方式";

    /// <summary>设备卡片底部显示的通信类型名称。</summary>
    public string MetricValue => CommunicationType;

    /// <summary>设备卡片底部显示的最近状态变化时间。</summary>
    public string UpdateText
    {
        get => _updateText;
        private set => SetField(ref _updateText, value);
    }

    /// <summary>设备状态的详细说明，供卡片悬停提示使用。</summary>
    public string ConnectionDetail
    {
        get => _connectionDetail;
        private set => SetField(ref _connectionDetail, value);
    }

    /// <summary>设备使用的通信类型。</summary>
    public string CommunicationType { get; }

    #endregion
}
