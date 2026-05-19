using Shared.Infrastructure.DependencyInjection;
using Shared.Infrastructure.Mediator;
using Autofac;
using System.Reflection;

namespace TestProject;

[TestFixture]
public sealed class MediatorTests
{
    [Test]
    public async Task Send_WithResponseRequest_ResolvesSingleHandler()
    {
        using IContainer container = BuildContainer();
        IMediator mediator = container.Resolve<IMediator>();

        int result = await mediator.Send(new SumRequest(2, 3));

        Assert.That(result, Is.EqualTo(5));
    }

    [Test]
    public async Task Send_CommandRequest_InvokesHandler()
    {
        using IContainer container = BuildContainer();
        IMediator mediator = container.Resolve<IMediator>();
        CommandExecutionLog log = container.Resolve<CommandExecutionLog>();

        await mediator.Send(new MarkExecutedCommand("scheme-1"));

        Assert.That(log.ExecutedIds, Is.EquivalentTo(new[] { "scheme-1" }));
    }

    [Test]
    public void Send_WithoutHandler_ThrowsMeaningfulException()
    {
        using IContainer container = BuildContainer();
        IMediator mediator = container.Resolve<IMediator>();

        Assert.ThrowsAsync<MediatorHandlerNotFoundException>(async () =>
            await mediator.Send(new MissingRequest()));
    }

    private static IContainer BuildContainer()
    {
        return ServiceCollectionHelper.Build(builder =>
        {
            builder.RegisterType<CommandExecutionLog>().SingleInstance();
            ServiceCollectionHelper.RegisterMediatorHandlers(builder, Assembly.GetExecutingAssembly());
        });
    }

    private sealed record SumRequest(int Left, int Right) : IRequest<int>;

    private sealed class SumRequestHandler : IRequestHandler<SumRequest, int>
    {
        public Task<int> Handle(SumRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(request.Left + request.Right);
        }
    }

    private sealed record MarkExecutedCommand(string Id) : IRequest;

    private sealed class MarkExecutedCommandHandler : IRequestHandler<MarkExecutedCommand>
    {
        private readonly CommandExecutionLog _log;

        public MarkExecutedCommandHandler(CommandExecutionLog log)
        {
            _log = log;
        }

        public Task Handle(MarkExecutedCommand request, CancellationToken cancellationToken = default)
        {
            _log.ExecutedIds.Add(request.Id);
            return Task.CompletedTask;
        }
    }

    private sealed record MissingRequest : IRequest<int>;

    private sealed class CommandExecutionLog
    {
        public List<string> ExecutedIds { get; } = [];
    }
}
