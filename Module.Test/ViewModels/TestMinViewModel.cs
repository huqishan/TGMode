using ControlLibrary;
using ControlLibrary.Controls.TestDataTable.Models;
using Shared.Infrastructure.Events;
using Shared.Models.Test;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media;

namespace Module.Test.ViewModels;

public sealed class TestMinViewModel : ViewModelProperties, IDisposable
{
    private static readonly Brush WaitingBrush = CreateBrush("#1A69FF");
    private static readonly Brush RunningBrush = CreateBrush("#2F80ED");
    private static readonly Brush SuccessBrush = CreateBrush("#18A058");
    private static readonly Brush FailureBrush = CreateBrush("#D14343");

    private readonly IEventAggregator _eventAggregator;
    private string _stationName;
    private string _lineName;
    private string _testStatus = "\u5f85\u914d\u7f6e";
    private string _productName = "\u4ea7\u54c1\u540d\u79f0";
    private string _productBarcode = "\u672a\u8bfb\u7801";
    private string _schemeName = "\u672a\u9009\u62e9\u65b9\u6848";
    private string _workOrderNo = "\u672a\u4e0b\u53d1";
    private string _selectedProductName = string.Empty;
    private TestProfileOption? _selectedProfile;
    private string _runStateText = "Waiting";
    private string _elapsedTimeText = "0.0 s";
    private Brush _statusBrush = WaitingBrush;
    private readonly Stopwatch _elapsedStopwatch = new();
    private bool _disposed;

    public TestMinViewModel()
        : this("\u5de5\u4f4d 1", EventAggregator.Current)
    {
    }

    public TestMinViewModel(string stationName)
        : this(stationName, EventAggregator.Current)
    {
    }

    public TestMinViewModel(string stationName, IEventAggregator eventAggregator)
    {
        _stationName = string.IsNullOrWhiteSpace(stationName) ? "\u672a\u547d\u540d\u5de5\u4f4d" : stationName.Trim();
        _lineName = "\u7ebf\u4f53 A";
        _eventAggregator = eventAggregator;

        SingleStepTestCommand = new RelayCommand(_ => StartSingleStepTest());
        ContinuousTestCommand = new RelayCommand(_ => StartContinuousTest());
        StopTestCommand = new RelayCommand(_ => StopTest());
        RefreshProductsCommand = new RelayCommand(_ => RefreshProducts());

        TestData = new ObservableCollection<TestDataDisplayItem>(CreateDefaultTestData());
        ProductOptions = new ObservableCollection<string>(CreateDefaultProductOptions());
        ProfilesView = new ObservableCollection<TestProfileOption>(CreateDefaultProfiles());
        _selectedProductName = ProductOptions.Count > 0 ? ProductOptions[0] : string.Empty;
        _selectedProfile = ProfilesView.Count > 0 ? ProfilesView[0] : null;
        if (_selectedProfile is not null)
        {
            _schemeName = _selectedProfile.WorkStepName;
        }

        for (int i = 0; i < 10; i++)
        {
            TestData.Add(new()
            {
                WorkStep = "单体电压",
                Name = $"单体电压{i}",
                TestValue = "23.8V",
                JudgmentCondition = "\u7535\u538b > 22V",
                Result = "OK"
            });
        }

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
                SchemeName = value?.WorkStepName ?? "\u672a\u9009\u62e9\u65b9\u6848";
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

    public ObservableCollection<string> ProductOptions { get; }

    public ObservableCollection<TestProfileOption> ProfilesView { get; }

    public ICommand SingleStepTestCommand { get; }

    public ICommand ContinuousTestCommand { get; }

    public ICommand StopTestCommand { get; }

    public ICommand RefreshProductsCommand { get; }

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
        StartTest("\u5355\u6b65\u6d4b\u8bd5");
    }

    private void StartContinuousTest()
    {
        StartTest("\u8fde\u7eed\u6d4b\u8bd5");
    }

    private void StopTest()
    {
        _elapsedStopwatch.Stop();
        UpdateElapsedTime();
        ApplyCurrentWorkStepElapsedTime(ElapsedTimeText);
        _elapsedStopwatch.Reset();
        TestStatus = "\u5df2\u505c\u6b62";
        RunStateText = "Waiting";
        StatusBrush = WaitingBrush;
    }

    private void StartTest(string statusText)
    {
        TestStatus = statusText;
        RunStateText = "Running";
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

        return message.TestStatus.Contains("\u6d4b\u8bd5\u4e2d", StringComparison.OrdinalIgnoreCase) ||
               message.TestStatus.Contains("\u6267\u884c\u4e2d", StringComparison.OrdinalIgnoreCase)
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
                WorkStep = "\u4e0a\u7535\u68c0\u67e5",
                Name = "\u7535\u538b\u91c7\u6837",
                TestValue = "23.8V",
                JudgmentCondition = "\u7535\u538b > 22V",
                Result = "OK"
            },
            new()
            {
                WorkStep = "\u4e0a\u7535\u68c0\u67e5",
                Name = "\u7535\u6d41\u91c7\u6837",
                TestValue = "1.2A",
                JudgmentCondition = "\u7535\u538b > 22V",
                Result = "OK"
            },
            new()
            {
                WorkStep = "\u4e0a\u7535\u68c0\u67e5",
                Name = "PLC\u63e1\u624b",
                TestValue = "286ms",
                JudgmentCondition = "\u54cd\u5e94\u65f6\u95f4 < 200ms",
                Result = "NG"
            },
            new()
            {
                WorkStep = "\u901a\u8baf\u6d4b\u8bd5",
                Name = "\u8bfb\u53d6\u6761\u7801",
                TestValue = "A2405060001",
                JudgmentCondition = "\u6761\u7801\u4e0d\u4e3a\u7a7a",
                Result = "OK"
            }
        ];
    }

    private static string[] CreateDefaultProductOptions()
    {
        return
        [
            "\u6807\u51c6\u7535\u6c60\u5305",
            "\u9ad8\u538b\u63a7\u5236\u76d2",
            "\u4ea7\u7ebf\u6837\u4ef6"
        ];
    }

    private static TestProfileOption[] CreateDefaultProfiles()
    {
        return
        [
            new(
                "\u4e0a\u7535\u68c0\u67e5",
                "\u5355\u6b65",
                "\u6821\u9a8c\u7535\u538b\u3001\u7535\u6d41\u548c PLC \u63e1\u624b\u72b6\u6001\u3002"),
            new(
                "\u901a\u8baf\u6d4b\u8bd5",
                "\u8fde\u7eed",
                "\u5faa\u73af\u8bfb\u53d6\u6761\u7801\u3001\u5de5\u5355\u548c MES \u8fd4\u56de\u7ed3\u679c\u3002"),
            new(
                "\u7ed3\u679c\u590d\u6838",
                "\u5224\u5b9a",
                "\u6c47\u603b\u6d4b\u8bd5\u6570\u636e\u5e76\u5237\u65b0\u5224\u5b9a\u72b6\u6001\u3002")
        ];
    }
}

public sealed class TestProfileOption
{
    public TestProfileOption(string workStepName, string typeDisplayName, string summary)
    {
        WorkStepName = workStepName;
        TypeDisplayName = typeDisplayName;
        Summary = summary;
    }

    public string WorkStepName { get; }

    public string TypeDisplayName { get; }

    public string Summary { get; }
}
