using Shared.Infrastructure.Mediator;
using Shared.Models.MES;

namespace ControlLibrary.Models.MediatorModels.MES;

public sealed record ExecuteMesRequest(
    string ApiName,
    string RequestPayload = "",
    MesDataInfoTree? SourceData = null) : IRequest<ExecuteMesResponse>;

public sealed record ExecuteMesResponse(
    MesResult Result,
    string RequestPayload);
