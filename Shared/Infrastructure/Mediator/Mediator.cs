using Autofac;
using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Shared.Infrastructure.Mediator;

/// <summary>
/// 基于 Autofac 的轻量请求分发器。
/// </summary>
public sealed class Mediator : IMediator
{
    private static readonly MethodInfo SendResponseCoreMethod =
        typeof(Mediator).GetMethod(nameof(SendResponseCore), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo SendCommandCoreMethod =
        typeof(Mediator).GetMethod(nameof(SendCommandCore), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly ILifetimeScope _scope;

    public Mediator(ILifetimeScope scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            object? task = SendResponseCoreMethod
                .MakeGenericMethod(request.GetType(), typeof(TResponse))
                .Invoke(this, [request, cancellationToken]);

            return (Task<TResponse>)task!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    public Task Send(IRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            object? task = SendCommandCoreMethod
                .MakeGenericMethod(request.GetType())
                .Invoke(this, [request, cancellationToken]);

            return (Task)task!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private Task<TResponse> SendResponseCore<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        IRequestHandler<TRequest, TResponse>? handler =
            _scope.ResolveOptional<IRequestHandler<TRequest, TResponse>>();

        if (handler is null)
        {
            throw MediatorHandlerNotFoundException.ForRequest(typeof(TRequest), typeof(TResponse));
        }

        return handler.Handle(request, cancellationToken);
    }

    private Task SendCommandCore<TRequest>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest
    {
        IRequestHandler<TRequest>? handler =
            _scope.ResolveOptional<IRequestHandler<TRequest>>();

        if (handler is null)
        {
            throw MediatorHandlerNotFoundException.ForRequest(typeof(TRequest));
        }

        return handler.Handle(request, cancellationToken);
    }
}
