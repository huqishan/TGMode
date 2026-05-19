using System.Threading;
using System.Threading.Tasks;

namespace Shared.Infrastructure.Mediator;

/// <summary>
/// 模块间请求和通知分发入口。
/// </summary>
public interface IMediator
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    Task Send(IRequest request, CancellationToken cancellationToken = default);
}
