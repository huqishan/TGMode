using ControlLibrary;
using Shared.Infrastructure.Events;
using Shared.Models.Test;
using System;
using System.Windows.Media;

namespace Module.Test.ViewModels;

public sealed class TestMinViewModel : ViewModelProperties, IDisposable
{
    private static readonly Brush NeutralBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
    private static readonly Brush RunningBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
    private static readonly Brush SuccessBrush = new SolidColorBrush(Color.FromRgb(22, 163, 74));
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6));
    private static readonly Brush FailureBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));

    private readonly IEventAggregator _eventAggregator;
    private string _stationName = "未选择工位";
    private string _testStatus = "等待测试";
    private string _productBarcode = "未读取";
    private string _schemeName = "未选择方案";
    private string _productName = "未选择产品";
    private string _statusMessage = "等待测试状态更新";
    private string _lastUpdatedText = "最后更新：--";
    private Brush _statusBrush = NeutralBrush;
    private bool _disposed;

    public TestMinViewModel()
        : this(EventAggregator.Current)
    {
    }

    public TestMinViewModel(IEventAggregator eventAggregator)
    {
        _eventAggregator = eventAggregator;
        _eventAggregator
            .GetEvent<TestExecutionStatusChangedEvent>()
            .Subscribe(ApplyStatus, ThreadOption.UIThread, true);
    }

    public string StationName
    {
        get => _stationName;
        private set => SetField(ref _stationName, value);
    }

    public string TestStatus
    {
        get => _testStatus;
        private set => SetField(ref _testStatus, value);
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

    public string ProductName
    {
        get => _productName;
        private set => SetField(ref _productName, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => SetField(ref _lastUpdatedText, value);
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
        StationName = UseFallback(message.StationName, "未选择工位");
        TestStatus = UseFallback(message.TestStatus, "等待测试");
        ProductBarcode = UseFallback(message.ProductBarcode, "未读取");
        SchemeName = UseFallback(message.SchemeName, "未选择方案");
        ProductName = UseFallback(message.ProductName, "未选择产品");
        StatusMessage = UseFallback(message.Message, "测试状态已更新");
        LastUpdatedText = $"最后更新：{message.OccurredAt:yyyy-MM-dd HH:mm:ss}";
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

        string status = message.TestStatus.Trim();
        if (status.Contains("准备", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("等待", StringComparison.OrdinalIgnoreCase))
        {
            return WarningBrush;
        }

        if (status.Contains("测试中", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("执行中", StringComparison.OrdinalIgnoreCase))
        {
            return RunningBrush;
        }

        return NeutralBrush;
    }

    private static string UseFallback(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
