using ControlLibrary;
using ControlLibrary.Controls.TestDataTable.Models;
using ControlLibrary.Models.MediatorModels.Business;
using Shared.Infrastructure.Events;
using Shared.Infrastructure.Mediator;
using Shared.Models.Test;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Test.ViewModels;

public sealed class TestMaxViewModel : ViewModelProperties, IDisposable
{
    private static readonly Brush WaitingBrush = CreateBrush("#1A69FF");
    private static readonly Brush RunningBrush = CreateBrush("#2F80ED");
    private static readonly Brush SuccessBrush = CreateBrush("#18A058");
    private static readonly Brush FailureBrush = CreateBrush("#D14343");

    private readonly IEventAggregator _eventAggregator;
    private readonly IMediator _mediator;
    private string _stationName;
    private string _lineName;
    private string _testStatus = "待配置";
    private string _productName = "产品名称";
    private string _productBarcode = "未读码";
    private string _schemeName = "未选择方案";
    private string _workOrderNo = "未下发";
    private string _selectedProductName = string.Empty;
    private TestProfileOption? _selectedProfile;
    private string _runStateText = "待机";
    private string _elapsedTimeText = "0.0 s";
    private Brush _statusBrush = WaitingBrush;
    private readonly Stopwatch _elapsedStopwatch = new();
    private bool _disposed;

    public TestMaxViewModel(IEventAggregator eventAggregator, IMediator mediator)
        : this("工位 1", eventAggregator, mediator)
    {
    }

    public TestMaxViewModel(
        string stationName,
        IEventAggregator eventAggregator,
        IMediator mediator)
    {
        _stationName = string.IsNullOrWhiteSpace(stationName) ? "未命名工位" : stationName.Trim();
        _lineName = "线体 A";
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

        SingleStepTestCommand = new RelayCommand(_ => StartSingleStepTest());
        ContinuousTestCommand = new RelayCommand(_ => StartContinuousTest());
        StopTestCommand = new RelayCommand(_ => StopTest());
        RefreshProductsCommand = new RelayCommand(_ => RefreshProducts());
        RefreshSchemesCommand = new AsyncRelayCommand(_ => LoadSchemesAsync());

        TestData = new ObservableCollection<TestDataDisplayItem>();
        WorkSteps = new ObservableCollection<TestDataDisplayItem>();
        ProductOptions = new ObservableCollection<string>(CreateDefaultProductOptions());
        ProfilesView = new ObservableCollection<TestProfileOption>();
        _selectedProductName = ProductOptions.Count > 0 ? ProductOptions[0] : string.Empty;

        LoadDefaultTestData();

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

    public ObservableCollection<TestDataDisplayItem> TestData { get; }

    public ObservableCollection<TestDataDisplayItem> WorkSteps { get; }

    public ObservableCollection<string> ProductOptions { get; }

    public ObservableCollection<TestProfileOption> ProfilesView { get; }

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

    private void StartSingleStepTest()
    {
        StartTest("单步测试");
    }

    private void StartContinuousTest()
    {
        StartTest("连续测试");
    }

    private void StopTest()
    {
        _elapsedStopwatch.Stop();
        UpdateElapsedTime();
        ApplyCurrentWorkStepElapsedTime(ElapsedTimeText);
        _elapsedStopwatch.Reset();
        TestStatus = "已停止";
        RunStateText = "待机";
        StatusBrush = WaitingBrush;
    }

    private void StartTest(string statusText)
    {
        TestStatus = statusText;
        RunStateText = "运行中";
        StatusBrush = RunningBrush;
        _elapsedStopwatch.Restart();
        UpdateElapsedTime();
        ApplyCurrentWorkStepElapsedTime(ElapsedTimeText);
    }

    private void UpdateElapsedTime()
    {
        ElapsedTimeText = $"{_elapsedStopwatch.Elapsed.TotalSeconds:0.0} s";
    }

    private void ApplyCurrentWorkStepElapsedTime(string elapsedTimeText)
    {
        string workStepName = SelectedProfile?.WorkStepName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(workStepName))
        {
            return;
        }

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
        StatusBrush = ResolveStatusBrush(message);
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
    }

    private void LoadDefaultTestData()
    {
        WorkSteps.Clear();
        TestData.Clear();

        foreach (TestDataDisplayItem item in CreateDefaultTestData())
        {
            WorkSteps.Add(item);
            TestData.Add(item);
        }

        for (int i = 0; i < 10; i++)
        {
            TestDataDisplayItem item = new()
            {
                WorkStep = "单体电压",
                Name = $"单体电压{i}",
                TestValue = "23.8V",
                JudgmentCondition = "电压 > 22V",
                Result = "OK"
            };

            TestData.Add(item);
        }
    }

    private static Brush ResolveStatusBrush(TestExecutionStatusMessage message)
    {
        if (message.IsSuccess == true)
        {
            return SuccessBrush;
        }

        if (message.IsSuccess == false)
        {
            return FailureBrush;
        }

        return message.TestStatus.Contains("测试中", StringComparison.OrdinalIgnoreCase) ||
               message.TestStatus.Contains("执行中", StringComparison.OrdinalIgnoreCase)
            ? RunningBrush
            : WaitingBrush;
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
                WorkStep = "上电检查",
                Name = "电压采样",
                TestValue = "23.8V",
                JudgmentCondition = "电压 > 22V",
                Result = "OK"
            },
            new()
            {
                WorkStep = "上电检查",
                Name = "电流采样",
                TestValue = "1.2A",
                JudgmentCondition = "电压 > 22V",
                Result = "OK"
            },
            new()
            {
                WorkStep = "上电检查",
                Name = "PLC握手",
                TestValue = "286ms",
                JudgmentCondition = "响应时间 < 200ms",
                Result = "NG"
            },
            new()
            {
                WorkStep = "通讯测试",
                Name = "读取条码",
                TestValue = "A2405060001",
                JudgmentCondition = "条码不为空",
                Result = "OK"
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
