using ControlLibrary.Models.MediatorModels.MES;
using Shared.Infrastructure.Mediator;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Module.MES.Services;

public sealed class ExecuteMesRequestHandler : IRequestHandler<ExecuteMesRequest, ExecuteMesResponse>
{
    private readonly MESService _mesService;

    public ExecuteMesRequestHandler(MESService mesService)
    {
        _mesService = mesService ?? throw new ArgumentNullException(nameof(mesService));
    }

    public Task<ExecuteMesResponse> Handle(
        ExecuteMesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_mesService.Execute(
            request.ApiName,
            request.RequestPayload,
            request.SourceData));
    }
}
