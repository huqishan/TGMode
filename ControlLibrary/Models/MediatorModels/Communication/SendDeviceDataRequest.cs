using Shared.Infrastructure.Mediator;
using Shared.Models.Communication;

namespace ControlLibrary.Models.MediatorModels.Communication;

/// <summary>
/// 请求向指定设备发送一条通信报文。
/// </summary>
public sealed record SendDeviceDataRequest(
    string DeviceName,
    SendReceiveModel ReadWriteModel,
    bool IsWait = false) : IRequest<DeviceExecutionActionResult>;
