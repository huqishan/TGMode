namespace Shared.Infrastructure.Mediator;

/// <summary>
/// 表示带返回值的请求。
/// </summary>
/// <typeparam name="TResponse">请求返回值类型。</typeparam>
public interface IRequest<out TResponse>
{
}

/// <summary>
/// 表示无返回值命令请求。
/// </summary>
public interface IRequest
{
}
