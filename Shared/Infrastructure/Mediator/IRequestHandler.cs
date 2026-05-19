using System.Threading;
using System.Threading.Tasks;

namespace Shared.Infrastructure.Mediator;

/// <summary>
/// 处理带返回值请求。
/// </summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// 处理无返回值命令请求。
/// </summary>
public interface IRequestHandler<in TRequest>
    where TRequest : IRequest
{
    Task Handle(TRequest request, CancellationToken cancellationToken = default);
}
