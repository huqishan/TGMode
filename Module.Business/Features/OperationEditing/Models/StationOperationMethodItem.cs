namespace Module.Business.Features.OperationEditing.Models;

/// <summary>
/// 工位业务方法条目。
/// 用于在操作编辑界面展示某工位可调用的方法信息，包括操作类型、协议、指令与参数个数。
/// </summary>
public sealed class StationOperationMethodItem
{
    /// <summary>条目种类，用于区分方法来源（如内置方法或协议指令）。</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>操作类型，与业务配置中的操作类型一致。</summary>
    public string OperationType { get; init; } = string.Empty;

    /// <summary>操作对象，表示方法作用的目标对象名称。</summary>
    public string OperationObject { get; init; } = string.Empty;

    /// <summary>协议名称，方法所属的通信协议。</summary>
    public string ProtocolName { get; init; } = string.Empty;

    /// <summary>指令名称，方法对应的协议指令。</summary>
    public string CommandName { get; init; } = string.Empty;

    /// <summary>调用方法名，运行时反射调用的方法标识。</summary>
    public string InvokeMethod { get; init; } = string.Empty;

    /// <summary>方法摘要说明，展示在界面列表中。</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>参数个数，用于界面快速判断方法复杂度。</summary>
    public int ParameterCount { get; init; }
}
