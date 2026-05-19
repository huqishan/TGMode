using Shared.Abstractions.Enum;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ControlLibrary.Models.MediatorModels.Communication;

public sealed class DeviceConnectionStateChangedEventArgs : EventArgs
{
    public DeviceConnectionStateChangedEventArgs(string deviceName, ConnectState connectState, DateTime changedAt)
    {
        DeviceName = deviceName;
        ConnectState = connectState;
        ChangedAt = changedAt;
    }

    public string DeviceName { get; }

    public ConnectState ConnectState { get; }

    public DateTime ChangedAt { get; }
}

public sealed class DeviceInitializationResult
{
    public DeviceInitializationResult(IReadOnlyList<DeviceExecutionActionResult> deviceResults)
    {
        DeviceResults = deviceResults;
    }

    public IReadOnlyList<DeviceExecutionActionResult> DeviceResults { get; }

    public int TotalCount => DeviceResults.Count;

    public int SuccessCount => DeviceResults.Count(item => item.IsSuccess);

    public bool IsSuccess => TotalCount > 0 && SuccessCount == TotalCount;
}

public sealed class DeviceExecutionActionResult
{
    public DeviceExecutionActionResult(
        bool isSuccess,
        string message,
        string deviceName = "",
        object? result = null)
    {
        IsSuccess = isSuccess;
        Message = message ?? string.Empty;
        DeviceName = deviceName ?? string.Empty;
        Result = result;
    }

    public bool IsSuccess { get; }

    public string Message { get; }

    public string DeviceName { get; }

    public object? Result { get; }

    public static DeviceExecutionActionResult Create(
        bool isSuccess,
        string message,
        string deviceName = "",
        object? result = null)
    {
        return new DeviceExecutionActionResult(isSuccess, message, deviceName, result);
    }

    public static DeviceExecutionActionResult CreateSuccess(
        string message,
        string deviceName = "",
        object? result = null)
    {
        return new DeviceExecutionActionResult(true, message, deviceName, result);
    }

    public static DeviceExecutionActionResult CreateFailure(
        string message,
        string deviceName = "",
        object? result = null)
    {
        return new DeviceExecutionActionResult(false, message, deviceName, result);
    }
}

public sealed class ParsedDeviceDataEntity
{
    public ParsedDeviceDataEntity(
        string deviceName,
        string key,
        string value,
        string data,
        string protocolName,
        string commandName,
        DateTime parsedAt)
    {
        DeviceName = deviceName;
        Key = key;
        Value = value;
        Data = data;
        ProtocolName = protocolName;
        CommandName = commandName;
        ParsedAt = parsedAt;
    }

    public string DeviceName { get; }

    public string Key { get; }

    public string Value { get; }

    public string Data { get; }

    public string ProtocolName { get; }

    public string CommandName { get; }

    public DateTime ParsedAt { get; }
}
