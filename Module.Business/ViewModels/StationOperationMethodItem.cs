namespace Module.Business.ViewModels;

public sealed class StationOperationMethodItem
{
    public string Kind { get; init; } = string.Empty;

    public string OperationType { get; init; } = string.Empty;

    public string OperationObject { get; init; } = string.Empty;

    public string ProtocolName { get; init; } = string.Empty;

    public string CommandName { get; init; } = string.Empty;

    public string InvokeMethod { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public int ParameterCount { get; init; }
}
