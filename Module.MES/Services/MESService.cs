using ControlLibrary;
using ControlLibrary.Models.MediatorModels.MES;
using Shared.Infrastructure.Events;
using Shared.Infrastructure.PackMethod;
using Shared.Models.MES;
using System;

namespace Module.MES.Services;

public class MESService : ModuleService
{
    public MESService(IEventAggregator eventAggregator)
    {
        _EventAggregator = eventAggregator;
    }

    public ExecuteMesResponse Execute(
        string apiName,
        string requestPayload = "",
        MesDataInfoTree? sourceData = null)
    {
        string normalizedApiName = string.IsNullOrWhiteSpace(apiName)
            ? sourceData?.ApiName ?? string.Empty
            : apiName.Trim();
        string payload = requestPayload ?? string.Empty;
        MesResult result = MesDataConvert.SendMES(normalizedApiName, ref payload, sourceData);
        return new ExecuteMesResponse(result, payload);
    }
}
