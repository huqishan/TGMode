using Shared.Abstractions;
using Shared.Abstractions.Enum;
using Shared.Models.Communication;
using Shared.Models.Log;
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using S7.Net;
using DisplayDataType = Shared.Abstractions.Enum.DataType;
using PlcDataType = S7.Net.DataType;
using PlcVarType = S7.Net.VarType;

namespace Shared.Infrastructure.Communication
{
    /// <summary>
    /// PLC communication based on Siemens S7 Ethernet.
    /// Address examples:
    /// DB1.DBX0.0, DB1.DBB0, DB1.DBW2, DB1.DBD4, M0.0, MB0, MW2, MD4, I0.0, Q0.0.
    /// </summary>
    public sealed class S7PlcCommunication : ICommunication
    {
        private static readonly Regex DataBlockAddressRegex =
            new(@"^DB(?<db>\d+)\.DB(?<kind>[XBWD])(?<byte>\d+)(?:\.(?<bit>\d+))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AreaAddressRegex =
            new(@"^(?<area>[MIQ])(?:(?<byte>\d+)\.(?<bit>\d+)|(?<kind>[BWD])(?<byteoffset>\d+))$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly object _syncRoot = new object();
        private readonly string _remoteAddress;
        private readonly string _cpuTypeName;
        private readonly int _rack;
        private readonly int _slot;
        private Plc? _plc;
        private ConnectState _isConnected = ConnectState.DisConnected;

        public S7PlcCommunication(CommuniactionConfigModel config)
        {
            LocalName = config.LocalName;
            _remoteAddress = config.RemoteIPAddress;
            _cpuTypeName = S7CpuTypeNames.Normalize(config.S7CpuType);
            _rack = config.S7Rack;
            _slot = config.S7Slot;
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

                try
                {
                    Plc plc = new Plc(ParseCpuType(_cpuTypeName), _remoteAddress, (short)_rack, (short)_slot);
                    plc.Open();
                    bool isConnected = plc.IsConnected;

                    if (!isConnected)
                    {
                        plc.Close();
                        WriteLog($"{LocalName} S7 connection failed.", LogType.ERROR);
                        IsConnected = ConnectState.DisConnected;
                        return false;
                    }

                    _plc = plc;
                    IsConnected = ConnectState.Connected;
                    WriteLog($"{LocalName} S7 connected to {_remoteAddress} (CPU={_cpuTypeName}, Rack={_rack}, Slot={_slot}).", LogType.INFO);
                    return true;
                }
                catch (Exception ex)
                {
                    CloseCore();
                    IsConnected = ConnectState.DisConnected;
                    WriteLog($"{LocalName} S7 connection failed: {ex.Message}", LogType.ERROR);
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
                WriteLog($"{LocalName} S7 disconnected.", LogType.WARN);
                return true;
            }
        }

        public bool Send(ref SendReceiveModel readWriteModel, bool isWait = false)
        {
            lock (_syncRoot)
            {
                try
                {
                    if (!EnsureConnected())
                    {
                        readWriteModel.Result = "S7 is not connected.";
                        return false;
                    }

                    S7Address address = ParseAddress(readWriteModel.PLCAddress);
                    WriteValue(address, readWriteModel);
                    readWriteModel.Result = "OK";
                    WriteLog($"{LocalName} S7 write {address.CanonicalAddress} succeeded.", LogType.INFO);
                    return true;
                }
                catch (Exception ex)
                {
                    readWriteModel.Result = ex.Message;
                    WriteLog($"{LocalName} S7 write failed: {ex.Message}", LogType.ERROR);
                    return false;
                }
            }
        }

        public Task<bool> SendAsync(SendReceiveModel readWriteModel)
        {
            return Task.Run(() => Send(ref readWriteModel));
        }

        public bool Receive(ref SendReceiveModel readWriteModel)
        {
            lock (_syncRoot)
            {
                try
                {
                    if (!EnsureConnected())
                    {
                        readWriteModel.Result = "S7 is not connected.";
                        return false;
                    }

                    S7Address address = ParseAddress(readWriteModel.PLCAddress);
                    int count = Math.Max(1, readWriteModel.Lenght);
                    string resultText = ReadValue(address, count, readWriteModel.Type);
                    readWriteModel.Result = resultText;
                    WriteLog($"{LocalName} S7 read {address.CanonicalAddress}, length {count}, result {resultText}.", LogType.INFO);
                    Task.Run(() => OnReceive(resultText, address.CanonicalAddress));
                    return true;
                }
                catch (Exception ex)
                {
                    readWriteModel.Result = ex.Message;
                    WriteLog($"{LocalName} S7 read failed: {ex.Message}", LogType.ERROR);
                    return false;
                }
            }
        }

        private bool EnsureConnected()
        {
            if (_plc?.IsConnected == true && IsConnected == ConnectState.Connected)
            {
                return true;
            }

            return Start();
        }

        private string ReadValue(S7Address address, int count, DisplayDataType dataType)
        {
            Plc plc = _plc ?? throw new InvalidOperationException("S7 client is not ready.");

            if (address.VarType == PlcVarType.Bit)
            {
                if (count != 1)
                {
                    throw new InvalidOperationException("S7 bit address only supports length 1.");
                }

                bool value = Convert.ToBoolean(plc.Read(address.CanonicalAddress), CultureInfo.InvariantCulture);
                return FormatBoolean(value, dataType);
            }

            byte[] bytes = plc.ReadBytes(address.Area, address.DbNumber, address.StartByte, count * address.ElementSize);
            return address.VarType switch
            {
                PlcVarType.Byte => FormatByteValues(bytes, dataType),
                PlcVarType.Word => FormatWordValues(bytes, dataType),
                PlcVarType.DWord => FormatDWordValues(bytes, dataType),
                _ => throw new InvalidOperationException($"Unsupported S7 variable type: {address.VarType}.")
            };
        }

        private void WriteValue(S7Address address, SendReceiveModel readWriteModel)
        {
            Plc plc = _plc ?? throw new InvalidOperationException("S7 client is not ready.");
            string message = readWriteModel.Message?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new InvalidOperationException("S7 write value cannot be empty.");
            }

            if (address.VarType == PlcVarType.Bit)
            {
                bool boolValue = ParseBoolean(message);
                plc.Write(address.CanonicalAddress, boolValue);
                return;
            }

            byte[] buffer = address.VarType switch
            {
                PlcVarType.Byte => ParseByteWriteBuffer(message, readWriteModel.Type),
                PlcVarType.Word => ParseWordWriteBuffer(message),
                PlcVarType.DWord => ParseDWordWriteBuffer(message),
                _ => throw new InvalidOperationException($"Unsupported S7 variable type: {address.VarType}.")
            };

            plc.WriteBytes(address.Area, address.DbNumber, address.StartByte, buffer);
        }

        private static S7Address ParseAddress(object plcAddress)
        {
            string value = plcAddress?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("S7 address cannot be empty.");
            }

            Match dbMatch = DataBlockAddressRegex.Match(value);
            if (dbMatch.Success)
            {
                int dbNumber = ParseInt(dbMatch.Groups["db"].Value, "DB number");
                string kind = dbMatch.Groups["kind"].Value.ToUpperInvariant();
                int startByte = ParseInt(dbMatch.Groups["byte"].Value, "byte offset");
                int bit = dbMatch.Groups["bit"].Success ? ParseBit(dbMatch.Groups["bit"].Value) : 0;
                return CreateAddress(PlcDataType.DataBlock, dbNumber, kind, startByte, bit, value);
            }

            Match areaMatch = AreaAddressRegex.Match(value);
            if (areaMatch.Success)
            {
                PlcDataType area = areaMatch.Groups["area"].Value.ToUpperInvariant() switch
                {
                    "M" => PlcDataType.Memory,
                    "I" => PlcDataType.Input,
                    "Q" => PlcDataType.Output,
                    _ => throw new InvalidOperationException($"Unsupported S7 area: {value}.")
                };

                if (areaMatch.Groups["bit"].Success)
                {
                    int startByte = ParseInt(areaMatch.Groups["byte"].Value, "byte offset");
                    int bit = ParseBit(areaMatch.Groups["bit"].Value);
                    return CreateAddress(area, 0, "X", startByte, bit, value);
                }

                string kind = areaMatch.Groups["kind"].Value.ToUpperInvariant();
                int byteOffset = ParseInt(areaMatch.Groups["byteoffset"].Value, "byte offset");
                return CreateAddress(area, 0, kind, byteOffset, 0, value);
            }

            throw new InvalidOperationException(
                "Unsupported S7 address format. Examples: DB1.DBX0.0, DB1.DBB0, DB1.DBW2, DB1.DBD4, M0.0, MB0, MW2, MD4.");
        }

        private static S7Address CreateAddress(
            PlcDataType area,
            int dbNumber,
            string kind,
            int startByte,
            int bit,
            string canonicalAddress)
        {
            return kind switch
            {
                "X" => new S7Address(area, dbNumber, startByte, bit, PlcVarType.Bit, 1, canonicalAddress),
                "B" => new S7Address(area, dbNumber, startByte, 0, PlcVarType.Byte, 1, canonicalAddress),
                "W" => new S7Address(area, dbNumber, startByte, 0, PlcVarType.Word, 2, canonicalAddress),
                "D" => new S7Address(area, dbNumber, startByte, 0, PlcVarType.DWord, 4, canonicalAddress),
                _ => throw new InvalidOperationException($"Unsupported S7 address kind: {kind}.")
            };
        }

        private static int ParseInt(string value, string fieldName)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) || result < 0)
            {
                throw new InvalidOperationException($"{fieldName} must be a non-negative integer.");
            }

            return result;
        }

        private static int ParseBit(string value)
        {
            int bit = ParseInt(value, "bit offset");
            if (bit is < 0 or > 7)
            {
                throw new InvalidOperationException("S7 bit offset must be between 0 and 7.");
            }

            return bit;
        }

        private static string FormatBoolean(bool value, DisplayDataType dataType)
        {
            return dataType switch
            {
                DisplayDataType.Binary => value ? "1" : "0",
                DisplayDataType.Hexadecimal => value ? "0x1" : "0x0",
                _ => value ? "True" : "False"
            };
        }

        private static string FormatByteValues(byte[] values, DisplayDataType dataType)
        {
            return dataType switch
            {
                DisplayDataType.Hexadecimal => string.Join(", ", values.Select(value => $"0x{value:X2}")),
                DisplayDataType.Binary => string.Join(", ", values.Select(value => Convert.ToString(value, 2).PadLeft(8, '0'))),
                DisplayDataType.Octal => string.Join(", ", values.Select(value => Convert.ToString(value, 8))),
                DisplayDataType.Acsaii or DisplayDataType.String => Encoding.ASCII.GetString(values),
                _ => string.Join(", ", values)
            };
        }

        private static string FormatWordValues(byte[] bytes, DisplayDataType dataType)
        {
            ushort[] values = bytes
                .Chunk(2)
                .Select(chunk => (ushort)((chunk[0] << 8) | chunk[1]))
                .ToArray();

            return dataType switch
            {
                DisplayDataType.Hexadecimal => string.Join(", ", values.Select(value => $"0x{value:X4}")),
                DisplayDataType.Binary => string.Join(", ", values.Select(value => Convert.ToString(value, 2).PadLeft(16, '0'))),
                DisplayDataType.Octal => string.Join(", ", values.Select(value => Convert.ToString(value, 8))),
                DisplayDataType.Acsaii or DisplayDataType.String => new string(values.Select(value => (char)value).ToArray()),
                _ => string.Join(", ", values)
            };
        }

        private static string FormatDWordValues(byte[] bytes, DisplayDataType dataType)
        {
            uint[] values = bytes
                .Chunk(4)
                .Select(chunk =>
                    ((uint)chunk[0] << 24) |
                    ((uint)chunk[1] << 16) |
                    ((uint)chunk[2] << 8) |
                    chunk[3])
                .ToArray();

            return dataType switch
            {
                DisplayDataType.Hexadecimal => string.Join(", ", values.Select(value => $"0x{value:X8}")),
                DisplayDataType.Binary => string.Join(", ", values.Select(value => Convert.ToString((long)value, 2).PadLeft(32, '0'))),
                DisplayDataType.Octal => string.Join(", ", values.Select(value => Convert.ToString((long)value, 8))),
                DisplayDataType.Acsaii or DisplayDataType.String => new string(values.Select(value => (char)value).ToArray()),
                _ => string.Join(", ", values)
            };
        }

        private static byte[] ParseByteWriteBuffer(string message, DisplayDataType dataType)
        {
            if (dataType is DisplayDataType.Acsaii or DisplayDataType.String && !LooksLikeNumericSequence(message))
            {
                return Encoding.ASCII.GetBytes(message);
            }

            return SplitValues(message)
                .Select(ParseByte)
                .ToArray();
        }

        private static byte[] ParseWordWriteBuffer(string message)
        {
            ushort[] values = SplitValues(message)
                .Select(ParseUInt16)
                .ToArray();

            byte[] buffer = new byte[values.Length * 2];
            for (int index = 0; index < values.Length; index++)
            {
                buffer[index * 2] = Convert.ToByte(values[index] >> 8);
                buffer[index * 2 + 1] = Convert.ToByte(values[index] & 0xFF);
            }

            return buffer;
        }

        private static byte[] ParseDWordWriteBuffer(string message)
        {
            uint[] values = SplitValues(message)
                .Select(ParseUInt32)
                .ToArray();

            byte[] buffer = new byte[values.Length * 4];
            for (int index = 0; index < values.Length; index++)
            {
                buffer[index * 4] = Convert.ToByte((values[index] >> 24) & 0xFF);
                buffer[index * 4 + 1] = Convert.ToByte((values[index] >> 16) & 0xFF);
                buffer[index * 4 + 2] = Convert.ToByte((values[index] >> 8) & 0xFF);
                buffer[index * 4 + 3] = Convert.ToByte(values[index] & 0xFF);
            }

            return buffer;
        }

        private static string[] SplitValues(string message)
        {
            return message
                .Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();
        }

        private static bool LooksLikeNumericSequence(string message)
        {
            return SplitValues(message).All(token =>
                bool.TryParse(token, out _) ||
                token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _));
        }

        private static bool ParseBoolean(string value)
        {
            return value.Trim().ToUpperInvariant() switch
            {
                "1" or "TRUE" or "ON" => true,
                "0" or "FALSE" or "OFF" => false,
                _ => throw new InvalidOperationException("S7 bit write value must be 0/1/true/false/on/off.")
            };
        }

        private static byte ParseByte(string value)
        {
            int number = ParseNumber(value);
            if (number < byte.MinValue || number > byte.MaxValue)
            {
                throw new InvalidOperationException("S7 byte write value must be between 0 and 255.");
            }

            return Convert.ToByte(number);
        }

        private static ushort ParseUInt16(string value)
        {
            int number = ParseNumber(value);
            if (number < ushort.MinValue || number > ushort.MaxValue)
            {
                throw new InvalidOperationException("S7 word write value must be between 0 and 65535.");
            }

            return Convert.ToUInt16(number);
        }

        private static uint ParseUInt32(string value)
        {
            long number = value.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt64(value.Trim()[2..], 16)
                : long.Parse(value.Trim(), CultureInfo.InvariantCulture);

            if (number < uint.MinValue || number > uint.MaxValue)
            {
                throw new InvalidOperationException("S7 dword write value must be between 0 and 4294967295.");
            }

            return Convert.ToUInt32(number);
        }

        private static int ParseNumber(string value)
        {
            string normalized = value.Trim();
            return normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt32(normalized[2..], 16)
                : int.Parse(normalized, CultureInfo.InvariantCulture);
        }

        private static CpuType ParseCpuType(string cpuTypeName)
        {
            return S7CpuTypeNames.Normalize(cpuTypeName) switch
            {
                S7CpuTypeNames.S7200 => CpuType.S7200,
                S7CpuTypeNames.S7300 => CpuType.S7300,
                S7CpuTypeNames.S7400 => CpuType.S7400,
                S7CpuTypeNames.S71500 => CpuType.S71500,
                _ => CpuType.S71200
            };
        }

        private void CloseCore()
        {
            try
            {
                _plc?.Close();
            }
            catch
            {
            }
            finally
            {
                _plc = null;
            }
        }

        private void WriteLog(string message, LogType type)
        {
            Task.Run(() => OnLog(new LogMessageModel { Message = message, Type = type }));
        }

        private readonly record struct S7Address(
            PlcDataType Area,
            int DbNumber,
            int StartByte,
            int Bit,
            PlcVarType VarType,
            int ElementSize,
            string CanonicalAddress);
    }
}
