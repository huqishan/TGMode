using System;

namespace Shared.Infrastructure.Mediator;

/// <summary>
/// 未找到请求处理器时抛出的异常。
/// </summary>
public sealed class MediatorHandlerNotFoundException : InvalidOperationException
{
    private MediatorHandlerNotFoundException(string message, Type requestType, Type? responseType)
        : base(message)
    {
        RequestType = requestType;
        ResponseType = responseType;
    }

    public Type RequestType { get; }

    public Type? ResponseType { get; }

    public static MediatorHandlerNotFoundException ForRequest(Type requestType)
    {
        return new MediatorHandlerNotFoundException(
            $"未找到请求处理器：{requestType.FullName}",
            requestType,
            null);
    }

    public static MediatorHandlerNotFoundException ForRequest(Type requestType, Type responseType)
    {
        return new MediatorHandlerNotFoundException(
            $"未找到请求处理器：{requestType.FullName} -> {responseType.FullName}",
            requestType,
            responseType);
    }
}
