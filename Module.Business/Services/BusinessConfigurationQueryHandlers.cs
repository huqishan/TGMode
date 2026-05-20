using ControlLibrary.Models.MediatorModels.Business;
using Module.Business.ViewModels;
using Module.Business.ViewModels.PropertyVMs;
using Shared.Infrastructure.Mediator;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Module.Business.Services;

public sealed class GetBusinessSchemesRequestHandler
    : IRequestHandler<GetBusinessSchemesRequest, GetBusinessSchemesResponse>
{
    public Task<GetBusinessSchemesResponse> Handle(
        GetBusinessSchemesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var schemes = BusinessConfigurationStore.LoadCatalog()
            .Schemes
            .OrderBy(scheme => scheme.SchemeName, StringComparer.OrdinalIgnoreCase)
            .Select(scheme => new BusinessSchemeInfo(
                scheme.Id,
                scheme.SchemeName,
                scheme.Steps
                    .OrderBy(step => step.DisplayOrder)
                    .Select(step => new BusinessSchemeWorkStepInfo(
                        step.Id,
                        step.DisplayOrder,
                        ResolveWorkStepName(step),
                        step.SchemeStepName,
                        step.Operations.Count))
                    .ToList()))
            .ToList();

        return Task.FromResult(new GetBusinessSchemesResponse(schemes));
    }

    private static string ResolveWorkStepName(SchemeWorkStepItem step)
    {
        return string.IsNullOrWhiteSpace(step.StepName)
            ? step.SchemeStepName
            : step.StepName;
    }
}

public sealed class GetBusinessStationsRequestHandler
    : IRequestHandler<GetBusinessStationsRequest, GetBusinessStationsResponse>
{
    public Task<GetBusinessStationsResponse> Handle(
        GetBusinessStationsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var stations = BusinessConfigurationStore.LoadStationCatalog()
            .Stations
            .OrderBy(station => station.StationCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(station => station.StationName, StringComparer.OrdinalIgnoreCase)
            .Select(station => new BusinessStationInfo(
                station.Id,
                station.StationName,
                station.StationCode,
                station.IsEnabled))
            .ToList();

        return Task.FromResult(new GetBusinessStationsResponse(stations));
    }
}
