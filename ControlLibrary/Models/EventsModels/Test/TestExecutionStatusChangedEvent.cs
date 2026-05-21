using Shared.Infrastructure.Events;

namespace ControlLibrary.Models.EventsModels.Test;

public sealed class TestExecutionStatusChangedEvent : PubSubEvent<TestExecutionStatusMessage>
{
}
public sealed class TestExecutionStatusMessage
{
    public TestExecutionStatusMessage(
        string stationName,
        string testStatus,
        string productBarcode,
        string schemeName,
        string productName = "",
        string message = "",
        bool? isSuccess = null,
        DateTime? occurredAt = null)
    {
        StationName = stationName ?? string.Empty;
        TestStatus = testStatus ?? string.Empty;
        ProductBarcode = productBarcode ?? string.Empty;
        SchemeName = schemeName ?? string.Empty;
        ProductName = productName ?? string.Empty;
        Message = message ?? string.Empty;
        IsSuccess = isSuccess;
        OccurredAt = occurredAt ?? DateTime.Now;
    }

    public string StationName { get; }

    public string TestStatus { get; }

    public string ProductBarcode { get; }

    public string SchemeName { get; }

    public string ProductName { get; }

    public string Message { get; }

    public bool? IsSuccess { get; }

    public DateTime OccurredAt { get; }
}
