using Shared.Infrastructure.Mediator;
using System.Collections.Generic;

namespace ControlLibrary.Models.MediatorModels.Business;

public sealed record GetBusinessSchemesRequest : IRequest<GetBusinessSchemesResponse>;

public sealed record GetBusinessStationsRequest : IRequest<GetBusinessStationsResponse>;

public sealed record GetBusinessSchemesResponse(IReadOnlyList<BusinessSchemeInfo> Schemes);

public sealed record GetBusinessStationsResponse(IReadOnlyList<BusinessStationInfo> Stations);

public sealed record BusinessSchemeInfo(
    string Id,
    string SchemeName,
    IReadOnlyList<BusinessSchemeWorkStepInfo> WorkSteps);

public sealed record BusinessSchemeWorkStepInfo(
    string Id,
    int DisplayOrder,
    string WorkStepName,
    string StepName,
    int OperationCount);

public sealed record BusinessStationInfo(
    string Id,
    string StationName,
    string StationCode,
    bool IsEnabled);
