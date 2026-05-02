using Shared.Abstractions;
using Shared.Abstractions.Enum;
using Shared.Models.Communication;
using Shared.Models.Log;
using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Shared.Infrastructure.Communication
{
    /// <summary>
    /// PLC communication based on Mitsubishi MX Component ActUtlType.
    /// </summary>
    public sealed class MxPlcCommunication : ICommunication
    {
        private readonly int _logicalStationNumber;
        private readonly string? _password;
        private readonly object _syncRoot = new object();
        private dynamic? _actUtlType;
        private ConnectState _isConnected = ConnectState.DisConnected;

        public MxPlcCommunication(CommuniactionConfigModel config)
        {
            LocalName = config.LocalName;
            _logicalStationNumber = config.PLCActLogicalStationNumber;
            _password = config.PassWord;
        }

        public event ReceiveData OnReceive = (_, _) => string.Empty;

        public event StateChanged StateChange = delegate { };

        public event Action<LogMessageModel> OnLog = delegate { };

        public ConnectState IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected == value)
                {
                    return;
                }

                _isConnected = value;
                Task.Run(() => StateChange(value, LocalName));
            }
        }

        public string LocalName { get; }

        public bool Start()
        {
            lock (_syncRoot)
            {
                CloseCore();

                Type? actType = Type.GetTypeFromProgID("ActUtlType.ActUtlType");
                if (actType is null)
                {
                    WriteLog("未检测到 Mitsubishi MX Component：ActUtlType.ActUtlType。", LogType.ERROR);
                    IsConnected = ConnectState.DisConnected;
                    return false;
                }

                try
                {
                    _actUtlType = Activator.CreateInstance(actType);
                    _actUtlType.ActLogicalStationNumber = _logicalStationNumber;
                    TrySetPassword(_actUtlType, _password);

                    int resultCode = _actUtlType.Open();
                    bool isConnected = resultCode == 0;
                    IsConnected = isConnected ? ConnectState.Connected : ConnectState.DisConnected;
                    WriteLog(
                        isConnected
                            ? $"{LocalName} PLC 连接成功，逻辑站号：{_logicalStationNumber}。"
                            : $"{LocalName} PLC 连接失败，返回码：{resultCode}。",
                        isConnected ? LogType.INFO : LogType.ERROR);
                    return isConnected;
                }
                catch (Exception ex)
                {
                    CloseCore();
                    IsConnected = ConnectState.DisConnected;
                    WriteLog($"{LocalName} PLC 连接异常：{ex.Message}", LogType.ERROR);
                    return false;
                }
            }
        }

        public bool Close()
        {
            lock (_syncRoot)
            {
                CloseCore();
                IsConnected = ConnectState.DisConnected;
                WriteLog($"{LocalName} PLC 通信已断开。", LogType.WARN);
                return true;
            }
        }

        public bool Write(ref ReadWriteModel readWriteModel, bool isWait = false)
        {
            lock (_syncRoot)
            {
                if (!EnsureConnected(readWriteModel.PLCAddress, out string address))
                {
                    readWriteModel.Result = "PLC 未连接或地址为空。";
                    return false;
                }

                int[] values;
                try
                {
                    values = ParseWriteValues(readWriteModel.Message);
                }
                catch (Exception ex)
                {
                    readWriteModel.Result = ex.Message;
                    WriteLog($"{LocalName} PLC 写入参数错误：{ex.Message}", LogType.ERROR);
                    return false;
                }

                try
                {
                    int resultCode = _actUtlType!.WriteDeviceBlock(address, values.Length, ref values[0]);
                    bool success = resultCode == 0;
                    readWriteModel.Result = success ? "OK" : $"返回码：{resultCode}";
                    WriteLog(
                        $"{LocalName} PLC 写入 {address}，长度 {values.Length}，值 {string.Join(", ", values)}，结果：{(success ? "成功" : $"失败 {resultCode}")}。",
                        success ? LogType.INFO : LogType.ERROR);
                    return success;
                }
                catch (Exception ex)
                {
                    readWriteModel.Result = ex.Message;
                    WriteLog($"{LocalName} PLC 写入异常：{ex.Message}", LogType.ERROR);
                    return false;
                }
            }
        }

        public Task<bool> WriteAsync(ReadWriteModel readWriteModel)
        {
            return Task.Run(() => Write(ref readWriteModel));
        }

        public bool Read(ref ReadWriteModel readWriteModel)
        {
            lock (_syncRoot)
            {
                if (!EnsureConnected(readWriteModel.PLCAddress, out string address))
                {
                    readWriteModel.Result = "PLC 未连接或地址为空。";
                    return false;
                }

                int length = Math.Max(1, readWriteModel.Lenght);
                int[] values = new int[length];

                try
                {
                    int resultCode = _actUtlType!.ReadDeviceBlock(address, length, out values[0]);
                    bool success = resultCode == 0;
                    string resultText = success
                        ? FormatValues(values, readWriteModel.Type)
                        : $"返回码：{resultCode}";

                    readWriteModel.Result = resultText;
                    WriteLog(
                        $"{LocalName} PLC 读取 {address}，长度 {length}，结果：{(success ? resultText : $"失败 {resultCode}")}。",
                        success ? LogType.INFO : LogType.ERROR);

                    if (success)
                    {
                        Task.Run(() => OnReceive(resultText, address));
                    }

                    return success;
                }
                catch (Exception ex)
                {
                    readWriteModel.Result = ex.Message;
                    WriteLog($"{LocalName} PLC 读取异常：{ex.Message}", LogType.ERROR);
                    return false;
                }
            }
        }

        private bool EnsureConnected(object plcAddress, out string address)
        {
            address = plcAddress?.ToString()?.Trim() ?? string.Empty;
            return IsConnected == ConnectState.Connected &&
                   _actUtlType is not null &&
                   !string.IsNullOrWhiteSpace(address);
        }

        private void CloseCore()
        {
            try
            {
                _actUtlType?.Close();
            }
            catch
            {
            }
            finally
            {
                if (_actUtlType is not null && Marshal.IsComObject(_actUtlType))
                {
                    Marshal.FinalReleaseComObject(_actUtlType);
                }

                _actUtlType = null;
            }
        }

        private static void TrySetPassword(dynamic actUtlType, string? password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return;
            }

            try
            {
                actUtlType.ActPassword = password.Trim();
            }
            catch
            {
                // Older MX Component versions may not expose ActPassword.
            }
        }

        private static int[] ParseWriteValues(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new InvalidOperationException("写入值不能为空。");
            }

            return message
                .Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseNumber)
                .ToArray();
        }

        private static int ParseNumber(string rawValue)
        {
            string value = rawValue.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToInt32(value[2..], 16);
            }

            return int.Parse(value, CultureInfo.InvariantCulture);
        }

        private static string FormatValues(int[] values, DataType type)
        {
            return type switch
            {
                DataType.Hexadecimal => string.Join(", ", values.Select(value => $"0x{value:X}")),
                DataType.Binary => string.Join(", ", values.Select(value => Convert.ToString(value, 2))),
                DataType.Octal => string.Join(", ", values.Select(value => Convert.ToString(value, 8))),
                DataType.Acsaii or DataType.String => new string(values.Select(value => (char)value).ToArray()),
                _ => string.Join(", ", values)
            };
        }

        private void WriteLog(string message, LogType type)
        {
            Task.Run(() => OnLog(new LogMessageModel { Message = message, Type = type }));
        }
    }
}
