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

public sealed class TestMaxViewModel : ViewModelProperties, IDisposable
{
    private static readonly Brush IdleBrush = CreateBrush("#24C8F2");
    private static readonly Brush RunningBrush = CreateBrush("#F59E0B");
    private static readonly Brush SuccessBrush = CreateBrush("#18A058");
    private static readonly Brush FailureBrush = CreateBrush("#D14343");

    private readonly IEventAggregator _eventAggregator;
    private readonly IMediator _mediator;
    private readonly Stopwatch _elapsedStopwatch = new();
    private string _stationName;
    private string _lineName;
    private string _testStatus = "待机";
    private string _productName = "产品名称";
    private string _productBarcode = "未读码";
    private string _schemeName = "未选择方案";
    private string _workOrderNo = "未下发";
    private string _selectedProductName = string.Empty;
    private TestProfileOption? _selectedProfile;
    private TestDataDisplayItem? _selectedWorkStep;
    private string _runStateText = "待机";
    private string _elapsedTimeText = "0.0 s";
    private string _currentWorkStepName = "等待启动";
    private string _currentWorkStepElapsedTimeText = "0.0 s";
    private Brush _statusBrush = IdleBrush;
    /// <summary>当前单步测试是否正在执行。</summary>
    private bool _isBusy;
    private bool _disposed;
    /// <summary>防止同一工位重复启动单步测试的异步命令。</summary>
    private readonly AsyncRelayCommand _singleStepTestCommand;

    public TestMaxViewModel(IEventAggregator eventAggregator, IMediator mediator)
        : this("工位 1", eventAggregator, mediator)
    {
    }

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

    public string StationName
    {
        get => _stationName;
        set => SetField(ref _stationName, value ?? string.Empty, true);
    }

    public string LineName
    {
        get => _lineName;
        set => SetField(ref _lineName, value ?? string.Empty, true);
    }

    public string TestStatus
    {
        get => _testStatus;
        private set => SetField(ref _testStatus, value);
    }

    public string ProductName
    {
        get => _productName;
        private set => SetField(ref _productName, value);
    }

    public string ProductBarcode
    {
        get => _productBarcode;
        private set => SetField(ref _productBarcode, value);
    }

    public string SchemeName
    {
        get => _schemeName;
        private set => SetField(ref _schemeName, value);
    }

    public string WorkOrderNo
    {
        get => _workOrderNo;
        set => SetField(ref _workOrderNo, value ?? string.Empty, true);
    }

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

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetField(ref _statusBrush, value);
    }

    public string RunStateText
    {
        get => _runStateText;
        private set => SetField(ref _runStateText, value);
    }

    public string ElapsedTimeText
    {
        get => _elapsedTimeText;
        private set => SetField(ref _elapsedTimeText, value);
    }

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

    public string CurrentWorkStepElapsedTimeText
    {
        get => _currentWorkStepElapsedTimeText;
        private set => SetField(ref _currentWorkStepElapsedTimeText, value);
    }

    public ObservableCollection<TestDataDisplayItem> TestData { get; }

    public ObservableCollection<TestDataDisplayItem> WorkSteps { get; }

    public TestDataDisplayItem? SelectedWorkStep
    {
        get => _selectedWorkStep;
        set
        {
            SelectWorkStep(value);
            _singleStepTestCommand.RaiseCanExecuteChanged();
        }
    }

    public ObservableCollection<string> ProductOptions { get; }

    public ObservableCollection<TestProfileOption> ProfilesView { get; }

    public ObservableCollection<string> RunningLogs { get; }

    public ObservableCollection<string> MesLogs { get; }

    public ObservableCollection<string> ExceptionLogs { get; }

    public ObservableCollection<string> FunctionLogs { get; }

    public ICommand SingleStepTestCommand { get; }

    public ICommand ContinuousTestCommand { get; }

    public ICommand StopTestCommand { get; }

    public ICommand RefreshProductsCommand { get; }

    public ICommand RefreshSchemesCommand { get; }

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

    /// <summary>执行当前选中的方案工步，并同步界面状态与工步结果。</summary>
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
            AddLog(RunningLogs, $"{DateTime.Now:HH:mm:ss} 工步 {targetWorkStep.WorkStep}：{result.Message}");
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

    private void StartContinuousTest()
    {
        StartTest("连续测试", null);
    }

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

    private void UpdateElapsedTime()
    {
        ElapsedTimeText = $"{_elapsedStopwatch.Elapsed.TotalSeconds:0.0} s";
        CurrentWorkStepElapsedTimeText = ElapsedTimeText;
    }

    private void ApplyCurrentWorkStepElapsedTime(string elapsedTimeText)
    {
        ApplyCurrentWorkStepElapsedTime(elapsedTimeText, null);
    }

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

    private string ResolveCurrentWorkStepName()
    {
        return ResolveCurrentWorkStepName(null);
    }

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

    private void LoadDefaultLogs()
    {
        RunningLogs.Clear();
        MesLogs.Clear();
        ExceptionLogs.Clear();
        FunctionLogs.Clear();

        RunningLogs.Add($"{DateTime.Now:HH:mm:ss} {StationName} 已加载默认方案，等待启动。");
        RunningLogs.Add($"{DateTime.Now:HH:mm:ss} 当前工步：{CurrentWorkStepName}。");
        MesLogs.Add($"{DateTime.Now:HH:mm:ss} MES 连接正常，等待上传。");
        ExceptionLogs.Add($"{DateTime.Now:HH:mm:ss} 暂无异常。");
        FunctionLogs.Add($"{DateTime.Now:HH:mm:ss} 测试模块初始化完成。");
    }

    private static void AddLog(ObservableCollection<string> logs, string message)
    {
        logs.Insert(0, message);
        while (logs.Count > 50)
        {
            logs.RemoveAt(logs.Count - 1);
        }
    }

    private static bool IsRunningStatus(string status)
    {
        return !string.IsNullOrWhiteSpace(status) &&
               (status.Contains("测试", StringComparison.OrdinalIgnoreCase) ||
                status.Contains("执行", StringComparison.OrdinalIgnoreCase) ||
                status.Contains("运行", StringComparison.OrdinalIgnoreCase));
    }

    private static Brush CreateBrush(string colorText)
    {
        return (Brush)new BrushConverter().ConvertFromString(colorText)!;
    }

    private static string UseFallback(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

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

public sealed class TestProfileOption
{
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

    public string SchemeId { get; }

    public string WorkStepName { get; }

    public string TypeDisplayName { get; }

    public string Summary { get; }

    public IReadOnlyList<TestProfileWorkStepOption> WorkSteps { get; }

    public static TestProfileOption FromScheme(BusinessSchemeInfo scheme)
    {
        TestProfileWorkStepOption[] workSteps = scheme.WorkSteps
            .OrderBy(step => step.DisplayOrder)
            .Select(TestProfileWorkStepOption.FromWorkStep)
            .ToArray();

        return new TestProfileOption(
            scheme.Id,
            scheme.SchemeName,
            $"{workSteps.Length} 个工步",
            workSteps.Length == 0
                ? "未配置工步。"
                : string.Join(" / ", workSteps.Select(step => step.StepName)),
            workSteps);
    }
}

public sealed class TestProfileWorkStepOption
{
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

    public string Id { get; }

    public int DisplayOrder { get; }

    public string WorkStepName { get; }

    public string StepName { get; }

    public int OperationCount { get; }

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
