using ControlLibrary;
using ControlLibrary.Controls.TestDataTable.Models;
using ControlLibrary.Models.EventsModels.Test;
using ControlLibrary.Models.MediatorModels.Business;
using Module.Business.Services;
using Shared.Infrastructure.Events;
using Shared.Infrastructure.Mediator;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Business.Test.ViewModels;

/// <summary>
/// 单工位通信测试 ViewModel。
/// 展示工位的测试状态、产品信息、方案业务列表与测试数据，
/// 支持单步/连续测试、停止测试，并订阅执行状态事件同步界面展示。
/// </summary>
public sealed class TestMaxViewModel : ViewModelProperties, IDisposable
{
    #region 静态资源与字段

    /// <summary>待机状态颜色。</summary>
    private static readonly Brush IdleBrush = CreateBrush("#24C8F2");

    /// <summary>测试中状态颜色。</summary>
    private static readonly Brush RunningBrush = CreateBrush("#F59E0B");

    /// <summary>测试通过状态颜色。</summary>
    private static readonly Brush SuccessBrush = CreateBrush("#18A058");

    /// <summary>测试失败状态颜色。</summary>
    private static readonly Brush FailureBrush = CreateBrush("#D14343");

    /// <summary>事件聚合器，用于订阅业务执行状态变化事件。</summary>
    private readonly IEventAggregator _eventAggregator;

    /// <summary>中介者，用于查询方案列表。</summary>
    private readonly IMediator _mediator;

    /// <summary>测试计时器，用于展示整体耗时。</summary>
    private readonly Stopwatch _elapsedStopwatch = new();

    /// <summary>工位名称。</summary>
    private string _stationName;

    /// <summary>线体名称。</summary>
    private string _lineName;

    /// <summary>测试状态文案。</summary>
    private string _testStatus = "待机";

    /// <summary>产品名称。</summary>
    private string _productName = "产品名称";

    /// <summary>产品条码。</summary>
    private string _productBarcode = "未读码";

    /// <summary>当前方案名称。</summary>
    private string _schemeName = "未选择方案";

    /// <summary>工单号。</summary>
    private string _workOrderNo = "未下发";

    /// <summary>界面选中的产品名称。</summary>
    private string _selectedProductName = string.Empty;

    /// <summary>当前选中的方案选项。</summary>
    private TestProfileOption? _selectedProfile;

    /// <summary>当前选中的业务条目。</summary>
    private TestDataDisplayItem? _selectedWorkStep;

    /// <summary>运行状态文案（测试中/测试OK/测试NG/待机）。</summary>
    private string _runStateText = "待机";

    /// <summary>整体耗时文案。</summary>
    private string _elapsedTimeText = "0.0 s";

    /// <summary>当前执行的业务名称。</summary>
    private string _currentWorkStepName = "等待启动";

    /// <summary>当前业务耗时文案。</summary>
    private string _currentWorkStepElapsedTimeText = "0.0 s";

    /// <summary>状态指示颜色。</summary>
    private Brush _statusBrush = IdleBrush;

    /// <summary>当前单步测试是否正在执行。</summary>
    private bool _isBusy;

    /// <summary>是否已释放，保证 Dispose 幂等。</summary>
    private bool _disposed;

    /// <summary>防止同一工位重复启动单步测试的异步命令。</summary>
    private readonly AsyncRelayCommand _singleStepTestCommand;

    #endregion

    #region 构造函数

    /// <summary>
    /// 使用默认工位名“工位 1”构造，便于设计期与简单场景使用。
    /// </summary>
    public TestMaxViewModel(IEventAggregator eventAggregator, IMediator mediator)
        : this("工位 1", eventAggregator, mediator)
    {
    }

    /// <summary>
    /// 构造函数，初始化命令、默认数据与集合，并订阅业务执行状态事件。
    /// </summary>
    /// <param name="stationName">工位名称。</param>
    /// <param name="eventAggregator">事件聚合器。</param>
    /// <param name="mediator">中介者。</param>
    public TestMaxViewModel(string stationName, IEventAggregator eventAggregator, IMediator mediator)
    {
        _stationName = string.IsNullOrWhiteSpace(stationName) ? "未命名工位" : stationName.Trim();
        _lineName = "线体 A";
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

        _singleStepTestCommand = new AsyncRelayCommand(
            _ => StartSingleStepTestAsync(),
            _ => !IsBusy && SelectedProfile is not null && SelectedWorkStep is not null);
        SingleStepTestCommand = _singleStepTestCommand;
        ContinuousTestCommand = new RelayCommand(_ => StartContinuousTest());
        StopTestCommand = new RelayCommand(_ => StopTest());
        RefreshProductsCommand = new RelayCommand(_ => RefreshProducts());
        RefreshSchemesCommand = new AsyncRelayCommand(_ => LoadSchemesAsync());

        TestData = new ObservableCollection<TestDataDisplayItem>();
        WorkSteps = new ObservableCollection<TestDataDisplayItem>();
        ProductOptions = new ObservableCollection<string>(CreateDefaultProductOptions());
        ProfilesView = new ObservableCollection<TestProfileOption>();
        RunningLogs = new ObservableCollection<string>();
        MesLogs = new ObservableCollection<string>();
        ExceptionLogs = new ObservableCollection<string>();
        FunctionLogs = new ObservableCollection<string>();
        _selectedProductName = ProductOptions.Count > 0 ? ProductOptions[0] : string.Empty;

        LoadDefaultTestData();
        LoadDefaultLogs();

        _eventAggregator
            .GetEvent<TestExecutionStatusChangedEvent>()
            .Subscribe(ApplyStatus, ThreadOption.UIThread, true);
    }

    #endregion

    #region 界面绑定属性

    /// <summary>工位名称。</summary>
    public string StationName
    {
        get => _stationName;
        set => SetField(ref _stationName, value ?? string.Empty, true);
    }

    /// <summary>线体名称。</summary>
    public string LineName
    {
        get => _lineName;
        set => SetField(ref _lineName, value ?? string.Empty, true);
    }

    /// <summary>测试状态文案。</summary>
    public string TestStatus
    {
        get => _testStatus;
        private set => SetField(ref _testStatus, value);
    }

    /// <summary>产品名称。</summary>
    public string ProductName
    {
        get => _productName;
        private set => SetField(ref _productName, value);
    }

    /// <summary>产品条码。</summary>
    public string ProductBarcode
    {
        get => _productBarcode;
        private set => SetField(ref _productBarcode, value);
    }

    /// <summary>当前方案名称。</summary>
    public string SchemeName
    {
        get => _schemeName;
        private set => SetField(ref _schemeName, value);
    }

    /// <summary>工单号。</summary>
    public string WorkOrderNo
    {
        get => _workOrderNo;
        set => SetField(ref _workOrderNo, value ?? string.Empty, true);
    }

    /// <summary>界面选中的产品名称，变化时同步到产品名称展示。</summary>
    public string SelectedProductName
    {
        get => _selectedProductName;
        set
        {
            if (SetField(ref _selectedProductName, value ?? string.Empty, true))
            {
                ProductName = _selectedProductName;
            }
        }
    }

    /// <summary>当前选中的方案选项，变化时刷新业务列表与命令可用状态。</summary>
    public TestProfileOption? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetField(ref _selectedProfile, value))
            {
                ApplySelectedProfile();
                _singleStepTestCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>当前是否正在执行单步测试。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                _singleStepTestCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>状态指示颜色。</summary>
    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetField(ref _statusBrush, value);
    }

    /// <summary>运行状态文案（测试中/测试OK/测试NG/待机）。</summary>
    public string RunStateText
    {
        get => _runStateText;
        private set => SetField(ref _runStateText, value);
    }

    /// <summary>整体耗时文案。</summary>
    public string ElapsedTimeText
    {
        get => _elapsedTimeText;
        private set => SetField(ref _elapsedTimeText, value);
    }

    /// <summary>当前执行的业务名称，变化时刷新业务高亮标记。</summary>
    public string CurrentWorkStepName
    {
        get => _currentWorkStepName;
        private set
        {
            if (SetField(ref _currentWorkStepName, value))
            {
                RefreshCurrentWorkStepMarkers();
            }
        }
    }

    /// <summary>当前业务耗时文案。</summary>
    public string CurrentWorkStepElapsedTimeText
    {
        get => _currentWorkStepElapsedTimeText;
        private set => SetField(ref _currentWorkStepElapsedTimeText, value);
    }

    /// <summary>测试数据展示集合。</summary>
    public ObservableCollection<TestDataDisplayItem> TestData { get; }

    /// <summary>方案业务列表集合。</summary>
    public ObservableCollection<TestDataDisplayItem> WorkSteps { get; }

    /// <summary>当前选中的业务条目，变化时同步当前业务名称与耗时。</summary>
    public TestDataDisplayItem? SelectedWorkStep
    {
        get => _selectedWorkStep;
        set
        {
            SelectWorkStep(value);
            _singleStepTestCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>产品名称候选列表。</summary>
    public ObservableCollection<string> ProductOptions { get; }

    /// <summary>可选方案列表。</summary>
    public ObservableCollection<TestProfileOption> ProfilesView { get; }

    /// <summary>运行日志集合，最新日志插入顶部。</summary>
    public ObservableCollection<string> RunningLogs { get; }

    /// <summary>MES 上传日志集合。</summary>
    public ObservableCollection<string> MesLogs { get; }

    /// <summary>异常日志集合。</summary>
    public ObservableCollection<string> ExceptionLogs { get; }

    /// <summary>功能日志集合。</summary>
    public ObservableCollection<string> FunctionLogs { get; }

    /// <summary>单步测试命令。</summary>
    public ICommand SingleStepTestCommand { get; }

    /// <summary>连续测试命令。</summary>
    public ICommand ContinuousTestCommand { get; }

    /// <summary>停止测试命令。</summary>
    public ICommand StopTestCommand { get; }

    /// <summary>刷新产品候选命令。</summary>
    public ICommand RefreshProductsCommand { get; }

    /// <summary>刷新方案列表命令。</summary>
    public ICommand RefreshSchemesCommand { get; }

    #endregion

    #region 公开方法

    /// <summary>
    /// 从方案配置加载可选方案列表，并尽量保持当前选中项；
    /// 无可选方案时回退为未选择方案状态并加载默认展示数据。
    /// </summary>
    public async Task LoadSchemesAsync()
    {
        GetBusinessSchemesResponse response = await _mediator.Send(new GetBusinessSchemesRequest());
        string? selectedSchemeId = SelectedProfile?.SchemeId;

        ProfilesView.Clear();
        foreach (BusinessSchemeInfo scheme in response.Schemes)
        {
            ProfilesView.Add(TestProfileOption.FromScheme(scheme));
        }

        SelectedProfile = ProfilesView.FirstOrDefault(profile =>
                              string.Equals(profile.SchemeId, selectedSchemeId, StringComparison.Ordinal)) ??
                          ProfilesView.FirstOrDefault();

        if (SelectedProfile is null)
        {
            SchemeName = "未选择方案";
            WorkSteps.Clear();
            LoadDefaultTestData();
        }
    }

    #endregion

    /// <summary>
    /// 取消订阅业务执行状态事件，界面卸载时调用。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _eventAggregator
            .GetEvent<TestExecutionStatusChangedEvent>()
            .Unsubscribe(ApplyStatus);
        _disposed = true;
    }

    #region 测试命令

    /// <summary>执行当前选中的方案业务，并同步界面状态与业务结果。</summary>
    private async Task StartSingleStepTestAsync()
    {
        TestDataDisplayItem? targetWorkStep = SelectedWorkStep;
        TestProfileOption? profile = SelectedProfile;
        if (targetWorkStep is null || profile is null)
        {
            return;
        }

        int selectedIndex = WorkSteps.IndexOf(targetWorkStep);
        if (selectedIndex < 0 || selectedIndex >= profile.WorkSteps.Count)
        {
            return;
        }

        TestProfileWorkStepOption selectedOption = profile.WorkSteps[selectedIndex];
        IsBusy = true;
        StartTest("单步测试", targetWorkStep);
        try
        {
            SchemeExecutionResult result = await SchemeExecutionService.ExecuteWorkStepAsync(
                StationName,
                profile.SchemeId,
                selectedOption.Id);

            _elapsedStopwatch.Stop();
            UpdateElapsedTime();
            ApplyCurrentWorkStepElapsedTime(ElapsedTimeText, targetWorkStep);
            targetWorkStep.Result = result.IsSuccess ? "OK" : result.IsCanceled ? "已停止" : "NG";
            TestStatus = result.IsSuccess ? "单步测试完成" : result.IsCanceled ? "已停止" : "单步测试失败";
            RunStateText = result.IsSuccess ? "测试OK" : result.IsCanceled ? "已停止" : "测试NG";
            StatusBrush = result.IsSuccess ? SuccessBrush : result.IsCanceled ? IdleBrush : FailureBrush;
            AddLog(RunningLogs, $"{DateTime.Now:HH:mm:ss} 业务 {targetWorkStep.WorkStep}：{result.Message}");
            if (!result.IsSuccess && !result.IsCanceled)
            {
                AddLog(ExceptionLogs, $"{DateTime.Now:HH:mm:ss} {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _elapsedStopwatch.Stop();
            UpdateElapsedTime();
            targetWorkStep.Result = "NG";
            TestStatus = "单步测试失败";
            RunStateText = "测试NG";
            StatusBrush = FailureBrush;
            AddLog(ExceptionLogs, $"{DateTime.Now:HH:mm:ss} {StationName} 单步测试失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>启动连续测试：从首个未通过业务开始顺序执行。</summary>
    private void StartContinuousTest()
    {
        StartTest("连续测试", null);
    }

    /// <summary>停止当前工位的测试：通知执行服务停止并复位状态与计时。</summary>
    private void StopTest()
    {
        if (IsBusy)
        {
            SchemeExecutionService.Stop(StationName);
        }

        _elapsedStopwatch.Stop();
        UpdateElapsedTime();
        ApplyCurrentWorkStepElapsedTime(ElapsedTimeText);
        _elapsedStopwatch.Reset();
        TestStatus = "待机";
        RunStateText = "待机";
        StatusBrush = IdleBrush;
        CurrentWorkStepElapsedTimeText = "0.0 s";
        AddLog(RunningLogs, $"{DateTime.Now:HH:mm:ss} {StationName} 已停止，进入待机。");
    }

    /// <summary>测试开始时的公共处理：切换状态、设定当前业务并重置计时。</summary>
    private void StartTest(string statusText, TestDataDisplayItem? targetWorkStep)
    {
        TestStatus = statusText;
        RunStateText = "测试中";
        StatusBrush = RunningBrush;
        CurrentWorkStepName = ResolveCurrentWorkStepName(targetWorkStep);
        _elapsedStopwatch.Restart();
        UpdateElapsedTime();
        ApplyCurrentWorkStepElapsedTime(ElapsedTimeText, targetWorkStep);
        AddLog(RunningLogs, $"{DateTime.Now:HH:mm:ss} {StationName} 开始{statusText}，执行 {CurrentWorkStepName}。");
    }

    #endregion

    /// <summary>根据计时器刷新整体耗时与当前业务耗时文案。</summary>
    private void UpdateElapsedTime()
    {
        ElapsedTimeText = $"{_elapsedStopwatch.Elapsed.TotalSeconds:0.0} s";
        CurrentWorkStepElapsedTimeText = ElapsedTimeText;
    }

    /// <summary>把当前业务耗时同步到当前业务条目。</summary>
    /// <param name="elapsedTimeText">耗时文案。</param>
    private void ApplyCurrentWorkStepElapsedTime(string elapsedTimeText)
    {
        ApplyCurrentWorkStepElapsedTime(elapsedTimeText, null);
    }

    /// <summary>
    /// 把耗时文案写入指定业务条目；集合元素为不可变记录，通过替换元素触发界面刷新。
    /// </summary>
    /// <param name="elapsedTimeText">耗时文案。</param>
    /// <param name="targetWorkStep">目标业务条目，为空时按当前业务名匹配。</param>
    private void ApplyCurrentWorkStepElapsedTime(string elapsedTimeText, TestDataDisplayItem? targetWorkStep)
    {
        string workStepName = ResolveCurrentWorkStepName(targetWorkStep);
        if (string.IsNullOrWhiteSpace(workStepName))
        {
            return;
        }

        CurrentWorkStepName = workStepName;
        CurrentWorkStepElapsedTimeText = elapsedTimeText;

        for (int i = 0; i < TestData.Count; i++)
        {
            TestDataDisplayItem item = TestData[i];
            if (!string.Equals(item.WorkStep, workStepName, StringComparison.Ordinal))
            {
                continue;
            }

            TestData[i] = new TestDataDisplayItem
            {
                WorkStep = item.WorkStep,
                Name = item.Name,
                TestValue = item.TestValue,
                JudgmentCondition = item.JudgmentCondition,
                Result = item.Result,
                WorkStepElapsedTime = elapsedTimeText
            };
        }
    }

    /// <summary>刷新产品候选列表：集合为空时填充默认产品，并保证存在选中项。</summary>
    private void RefreshProducts()
    {
        if (ProductOptions.Count == 0)
        {
            foreach (string productName in CreateDefaultProductOptions())
            {
                ProductOptions.Add(productName);
            }
        }

        if (string.IsNullOrWhiteSpace(SelectedProductName) && ProductOptions.Count > 0)
        {
            SelectedProductName = ProductOptions[0];
        }
    }

    /// <summary>
    /// 处理业务执行状态变化事件：按工位名过滤后同步状态、产品信息，
    /// 并依据成功/失败/停止/运行中更新运行状态与指示颜色。
    /// </summary>
    /// <param name="message">执行状态消息。</param>
    private void ApplyStatus(TestExecutionStatusMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.StationName) &&
            !string.Equals(message.StationName.Trim(), StationName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        TestStatus = UseFallback(message.TestStatus, TestStatus);
        ProductName = UseFallback(message.ProductName, ProductName);
        ProductBarcode = UseFallback(message.ProductBarcode, ProductBarcode);
        SchemeName = UseFallback(message.SchemeName, SchemeName);

        if (message.IsSuccess == true)
        {
            RunStateText = "测试OK";
            StatusBrush = SuccessBrush;
        }
        else if (message.IsSuccess == false &&
                 (message.TestStatus.Contains("停止", StringComparison.OrdinalIgnoreCase) ||
                  message.TestStatus.Contains("取消", StringComparison.OrdinalIgnoreCase)))
        {
            RunStateText = "已停止";
            StatusBrush = IdleBrush;
        }
        else if (message.IsSuccess == false)
        {
            RunStateText = "测试NG";
            StatusBrush = FailureBrush;
            AddLog(ExceptionLogs, $"{DateTime.Now:HH:mm:ss} {StationName} 测试结果 NG，请检查异常项。");
        }
        else if (IsRunningStatus(message.TestStatus))
        {
            RunStateText = "测试中";
            StatusBrush = RunningBrush;
        }
        else
        {
            RunStateText = "待机";
            StatusBrush = IdleBrush;
        }
    }

    /// <summary>应用选中的方案：刷新方案名称、重建业务列表与测试数据，未选中时加载默认数据。</summary>
    private void ApplySelectedProfile()
    {
        SchemeName = SelectedProfile?.WorkStepName ?? "未选择方案";
        WorkSteps.Clear();
        TestData.Clear();

        if (SelectedProfile is null)
        {
            LoadDefaultTestData();
            return;
        }

        foreach (TestProfileWorkStepOption workStep in SelectedProfile.WorkSteps)
        {
            TestDataDisplayItem item = new()
            {
                WorkStep = workStep.StepName,
                Name = workStep.WorkStepName,
                TestValue = string.Empty,
                JudgmentCondition = workStep.OperationCount > 0
                    ? $"{workStep.OperationCount} 个步骤"
                    : "无步骤",
                Result = "待测"
            };

            WorkSteps.Add(item);
            TestData.Add(item);
        }

        CurrentWorkStepName = ResolveCurrentWorkStepName();
        RefreshCurrentWorkStepMarkers();
        CurrentWorkStepElapsedTimeText = "0.0 s";
    }

    /// <summary>解析当前业务名称：默认取首个未通过的业务。</summary>
    private string ResolveCurrentWorkStepName()
    {
        return ResolveCurrentWorkStepName(null);
    }

    /// <summary>
    /// 解析当前业务名称：优先取指定条目，其次取首个未通过业务，再退回首条，最后返回占位文案。
    /// </summary>
    /// <param name="targetWorkStep">指定业务条目。</param>
    private string ResolveCurrentWorkStepName(TestDataDisplayItem? targetWorkStep)
    {
        if (targetWorkStep is not null && WorkSteps.Contains(targetWorkStep))
        {
            return targetWorkStep.WorkStep;
        }

        return WorkSteps.FirstOrDefault(step =>
                   !string.Equals(step.Result, "OK", StringComparison.OrdinalIgnoreCase))?.WorkStep ??
               WorkSteps.FirstOrDefault()?.WorkStep ??
               TestData.FirstOrDefault()?.WorkStep ??
               "等待启动";
    }

    /// <summary>加载默认的业务列表与测试数据，用于未选择方案时的界面展示。</summary>
    private void LoadDefaultTestData()
    {
        WorkSteps.Clear();
        TestData.Clear();

        foreach (TestDataDisplayItem item in CreateDefaultWorkSteps())
        {
            WorkSteps.Add(item);
        }

        foreach (TestDataDisplayItem item in CreateDefaultTestData())
        {
            TestData.Add(item);
        }

        CurrentWorkStepName = ResolveCurrentWorkStepName();
        RefreshCurrentWorkStepMarkers();
        CurrentWorkStepElapsedTimeText = "0.0 s";
    }

    /// <summary>刷新业务列表的“当前业务”高亮标记，并同步选中项。</summary>
    private void RefreshCurrentWorkStepMarkers()
    {
        TestDataDisplayItem? currentWorkStep = _selectedWorkStep is not null && WorkSteps.Contains(_selectedWorkStep)
            ? _selectedWorkStep
            : WorkSteps.FirstOrDefault(item =>
                string.Equals(item.WorkStep, CurrentWorkStepName, StringComparison.Ordinal));

        foreach (TestDataDisplayItem item in WorkSteps)
        {
            item.IsCurrent = ReferenceEquals(item, currentWorkStep);
        }

        SetField(ref _selectedWorkStep, currentWorkStep, nameof(SelectedWorkStep));
    }

    /// <summary>处理业务条目选中变化：同步选中项、当前业务名称与耗时展示。</summary>
    private void SelectWorkStep(TestDataDisplayItem? workStep)
    {
        if (workStep is null)
        {
            SetField(ref _selectedWorkStep, null, nameof(SelectedWorkStep));
            RefreshCurrentWorkStepMarkers();
            return;
        }

        SetField(ref _selectedWorkStep, workStep, nameof(SelectedWorkStep));
        SetField(ref _currentWorkStepName, workStep.WorkStep, nameof(CurrentWorkStepName));
        CurrentWorkStepElapsedTimeText = string.IsNullOrWhiteSpace(workStep.WorkStepElapsedTime)
            ? "0.0 s"
            : workStep.WorkStepElapsedTime;
        RefreshCurrentWorkStepMarkers();
    }

    /// <summary>清空并写入各日志面板的初始占位内容。</summary>
    private void LoadDefaultLogs()
    {
        RunningLogs.Clear();
        MesLogs.Clear();
        ExceptionLogs.Clear();
        FunctionLogs.Clear();

        RunningLogs.Add($"{DateTime.Now:HH:mm:ss} {StationName} 已加载默认方案，等待启动。");
        RunningLogs.Add($"{DateTime.Now:HH:mm:ss} 当前业务：{CurrentWorkStepName}。");
        MesLogs.Add($"{DateTime.Now:HH:mm:ss} MES 连接正常，等待上传。");
        ExceptionLogs.Add($"{DateTime.Now:HH:mm:ss} 暂无异常。");
        FunctionLogs.Add($"{DateTime.Now:HH:mm:ss} 测试模块初始化完成。");
    }

    /// <summary>写入日志：新日志插入顶部，超出 50 条时移除最旧的日志。</summary>
    private static void AddLog(ObservableCollection<string> logs, string message)
    {
        logs.Insert(0, message);
        while (logs.Count > 50)
        {
            logs.RemoveAt(logs.Count - 1);
        }
    }

    /// <summary>判断状态文案是否属于运行中（包含测试/执行/运行关键字）。</summary>
    private static bool IsRunningStatus(string status)
    {
        return !string.IsNullOrWhiteSpace(status) &&
               (status.Contains("测试", StringComparison.OrdinalIgnoreCase) ||
                status.Contains("执行", StringComparison.OrdinalIgnoreCase) ||
                status.Contains("运行", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>按十六进制颜色文本创建画刷。</summary>
    private static Brush CreateBrush(string colorText)
    {
        return (Brush)new BrushConverter().ConvertFromString(colorText)!;
    }

    /// <summary>取非空文本，为空时回退到当前值，避免事件消息覆盖界面已有展示。</summary>
    private static string UseFallback(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    /// <summary>创建未选择方案时展示的默认测试数据。</summary>
    private static TestDataDisplayItem[] CreateDefaultTestData()
    {
        return
        [
            new()
            {
                WorkStep = "绝缘耐压",
                Name = "输入电压",
                TestValue = "24.1V",
                JudgmentCondition = "23.0V - 25.5V",
                Result = "OK"
            },
            new()
            {
                WorkStep = "绝缘耐压",
                Name = "绝缘电阻",
                TestValue = "36.8MΩ",
                JudgmentCondition = "> 20MΩ",
                Result = "OK"
            },
            new()
            {
                WorkStep = "绝缘耐压",
                Name = "漏电流",
                TestValue = "2.4mA",
                JudgmentCondition = "< 3.0mA",
                Result = "OK"
            },
            new()
            {
                WorkStep = "温度复测",
                Name = "NTC 复测",
                TestValue = "41.8℃",
                JudgmentCondition = "20℃ - 38℃",
                Result = "NG"
            }
        ];
    }

    /// <summary>创建未选择方案时展示的默认业务列表。</summary>
    private static TestDataDisplayItem[] CreateDefaultWorkSteps()
    {
        return
        [
            new()
            {
                WorkStep = "上电自检",
                Name = "上电自检",
                Result = "OK"
            },
            new()
            {
                WorkStep = "条码绑定",
                Name = "条码绑定",
                Result = "OK"
            },
            new()
            {
                WorkStep = "绝缘耐压",
                Name = "绝缘耐压",
                Result = "待测"
            },
            new()
            {
                WorkStep = "结果上传",
                Name = "结果上传",
                Result = "待测"
            }
        ];
    }

    /// <summary>创建默认的产品名称候选列表。</summary>
    private static string[] CreateDefaultProductOptions()
    {
        return
        [
            "标准电池包",
            "高压控制盒",
            "产线样件"
        ];
    }
}

/// <summary>
/// 测试界面的方案选项。
/// 封装方案基本信息与其包含的业务列表，由方案信息转换而来。
/// </summary>
public sealed class TestProfileOption
{
    /// <summary>私有构造函数，外部统一通过 FromScheme 创建。</summary>
    private TestProfileOption(
        string schemeId,
        string workStepName,
        string typeDisplayName,
        string summary,
        IReadOnlyList<TestProfileWorkStepOption> workSteps)
    {
        SchemeId = schemeId;
        WorkStepName = workStepName;
        TypeDisplayName = typeDisplayName;
        Summary = summary;
        WorkSteps = workSteps;
    }

    /// <summary>方案唯一标识。</summary>
    public string SchemeId { get; }

    /// <summary>方案名称。</summary>
    public string WorkStepName { get; }

    /// <summary>类型展示文案（业务数量）。</summary>
    public string TypeDisplayName { get; }

    /// <summary>方案摘要（业务名称拼接）。</summary>
    public string Summary { get; }

    /// <summary>方案包含的业务选项列表。</summary>
    public IReadOnlyList<TestProfileWorkStepOption> WorkSteps { get; }

    /// <summary>
    /// 由方案信息构建测试方案选项，业务按展示顺序排序。
    /// </summary>
    /// <param name="scheme">方案信息。</param>
    public static TestProfileOption FromScheme(BusinessSchemeInfo scheme)
    {
        TestProfileWorkStepOption[] workSteps = scheme.WorkSteps
            .OrderBy(step => step.DisplayOrder)
            .Select(TestProfileWorkStepOption.FromWorkStep)
            .ToArray();

        return new TestProfileOption(
            scheme.Id,
            scheme.SchemeName,
            $"{workSteps.Length} 个业务",
            workSteps.Length == 0
                ? "未配置业务。"
                : string.Join(" / ", workSteps.Select(step => step.StepName)),
            workSteps);
    }
}

/// <summary>
/// 测试界面的业务选项。
/// 封装方案内单个业务的标识、顺序、名称与操作数量，供单步测试定位执行目标。
/// </summary>
public sealed class TestProfileWorkStepOption
{
    /// <summary>私有构造函数，外部统一通过 FromWorkStep 创建。</summary>
    private TestProfileWorkStepOption(
        string id,
        int displayOrder,
        string workStepName,
        string stepName,
        int operationCount)
    {
        Id = id;
        DisplayOrder = displayOrder;
        WorkStepName = workStepName;
        StepName = stepName;
        OperationCount = operationCount;
    }

    /// <summary>业务唯一标识。</summary>
    public string Id { get; }

    /// <summary>展示顺序。</summary>
    public int DisplayOrder { get; }

    /// <summary>业务名称。</summary>
    public string WorkStepName { get; }

    /// <summary>业务步骤名称，为空时回退为业务名称。</summary>
    public string StepName { get; }

    /// <summary>业务包含的操作数量。</summary>
    public int OperationCount { get; }

    /// <summary>
    /// 由方案业务信息构建业务选项，空名称时相互回退。
    /// </summary>
    /// <param name="workStep">方案业务信息。</param>
    public static TestProfileWorkStepOption FromWorkStep(BusinessSchemeWorkStepInfo workStep)
    {
        string stepName = string.IsNullOrWhiteSpace(workStep.StepName)
            ? workStep.WorkStepName
            : workStep.StepName;

        return new TestProfileWorkStepOption(
            workStep.Id,
            workStep.DisplayOrder,
            string.IsNullOrWhiteSpace(workStep.WorkStepName) ? stepName : workStep.WorkStepName,
            stepName,
            workStep.OperationCount);
    }
}
