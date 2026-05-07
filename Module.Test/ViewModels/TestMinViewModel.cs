using ControlLibrary;
using Shared.Infrastructure.Events;
using Shared.Models.Test;
using System;
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
    private string _testStatus = "待配置";
    private string _productName = "产品名称";
    private string _productBarcode = "未读码";
    private string _schemeName = "未选择方案";
    private string _workOrderNo = "未下发";
    private Brush _statusBrush = WaitingBrush;
    private bool _disposed;

    public TestMinViewModel()
        : this("工位 1", EventAggregator.Current)
    {
    }

    public TestMinViewModel(string stationName)
        : this(stationName, EventAggregator.Current)
    {
    }

    public TestMinViewModel(string stationName, IEventAggregator eventAggregator)
    {
        _stationName = string.IsNullOrWhiteSpace(stationName) ? "未命名工位" : stationName.Trim();
        _lineName = "线体 A";
        _eventAggregator = eventAggregator;

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

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetField(ref _statusBrush, value);
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
}
