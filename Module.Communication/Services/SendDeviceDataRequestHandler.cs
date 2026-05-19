using ControlLibrary.Models.MediatorModels.Communication;
using Shared.Infrastructure.Mediator;
using Shared.Models.Communication;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Module.Communication.Services;

/// <summary>
/// 处理跨模块设备发送请求。
/// </summary>
public sealed class SendDeviceDataRequestHandler : IRequestHandler<SendDeviceDataRequest, DeviceExecutionActionResult>
{
    private readonly CommunicationService _communicationService;

    public SendDeviceDataRequestHandler(CommunicationService communicationService)
    {
        _communicationService = communicationService ??
                                throw new ArgumentNullException(nameof(communicationService));
    }

    public Task<DeviceExecutionActionResult> Handle(
        SendDeviceDataRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _communicationService.SendDataAsync(
            request.DeviceName,
            request.ReadWriteModel,
            cancellationToken);
    }
}
