using Shared.Abstractions;
using Shared.Abstractions.Enum;
using Shared.Models.Communication;
using Shared.Models.Log;
using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Shared.Infrastructure.Communication
{
    public sealed class ModbusTcpPlcCommunication : ICommunication
    {
        private const byte UnitId = 1;
        private const int TimeoutMilliseconds = 5000;

        private readonly object _syncRoot = new object();
        private readonly string _remoteAddress;
        private readonly int _remotePort;
        private readonly string _localAddress;
        private readonly int _localPort;
        private ushort _transactionId;
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private ConnectState _isConnected = ConnectState.DisConnected;

        public ModbusTcpPlcCommunication(CommuniactionConfigModel config)
        {
            LocalName = config.LocalName;
            _remoteAddress = config.RemoteIPAddress;
            _remotePort = config.RemotePort;
            _localAddress = config.LocalIPAddress;
            _localPort = config.LocalPort;
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
                    TcpClient tcpClient = new TcpClient(AddressFamily.InterNetwork)
                    {
                        NoDelay = true,
                        ReceiveTimeout = TimeoutMilliseconds,
                        SendTimeout = TimeoutMilliseconds
                    };

                    if (!string.IsNullOrWhiteSpace(_localAddress) || _localPort > 0)
                    {
                        IPAddress localIpAddress = IPAddress.TryParse(_localAddress, out IPAddress? parsedAddress)
                            ? parsedAddress
                            : IPAddress.Any;
                        tcpClient.Client.Bind(new IPEndPoint(localIpAddress, Math.Max(0, _localPort)));
                    }

                    tcpClient.Connect(_remoteAddress, _remotePort);
                    NetworkStream stream = tcpClient.GetStream();
                    stream.ReadTimeout = TimeoutMilliseconds;
                    stream.WriteTimeout = TimeoutMilliseconds;

                    _tcpClient = tcpClient;
                    _stream = stream;
                    IsConnected = ConnectState.Connected;
                    WriteLog($"{LocalName} Modbus TCP connected to {_remoteAddress}:{_remotePort}.", LogType.INFO);
                    return true;
                }
                catch (Exception ex)
                {
                    CloseCore();
                    IsConnected = ConnectState.DisConnected;
                    WriteLog($"{LocalName} Modbus TCP connection failed: {ex.Message}", LogType.ERROR);
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
                WriteLog($"{LocalName} Modbus TCP disconnected.", LogType.WARN);
                return true;
            }
        }

        public bool Write(ref ReadWriteModel readWriteModel, bool isWait = false)
        {
            lock (_syncRoot)
            {
                try
                {
                    if (!EnsureConnected())
                    {
                        readWriteModel.Result = "Modbus TCP is not connected.";
                        return false;
                    }

                    ushort address = ParseRegisterAddress(readWriteModel.PLCAddress);
                    ushort[] values = ParseWriteValues(readWriteModel.Message);
                    byte functionCode = values.Length == 1 ? (byte)0x06 : (byte)0x10;
                    byte[] response = SendRequest(BuildWriteRequest(functionCode, address, values));
                    ValidateResponse(response, functionCode);

                    readWriteModel.Result = "OK";
                    WriteLog(
                        $"{LocalName} Modbus TCP write {address}, values {string.Join(", ", values)} succeeded.",
                        LogType.INFO);
                    return true;
                }
                catch (Exception ex)
                {
                    readWriteModel.Result = ex.Message;
                    WriteLog($"{LocalName} Modbus TCP write failed: {ex.Message}", LogType.ERROR);
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
                try
                {
                    if (!EnsureConnected())
                    {
                        readWriteModel.Result = "Modbus TCP is not connected.";
                        return false;
                    }

                    ushort address = ParseRegisterAddress(readWriteModel.PLCAddress);
                    ushort quantity = Convert.ToUInt16(Math.Clamp(readWriteModel.Lenght, 1, 125));
                    byte[] response = SendRequest(BuildReadHoldingRegistersRequest(address, quantity));
                    ValidateResponse(response, 0x03);

                    int byteCount = response.Length > 1 ? response[1] : 0;
                    if (byteCount < quantity * 2 || response.Length < 2 + byteCount)
                    {
                        throw new InvalidOperationException("Invalid Modbus read response length.");
                    }

                    ushort[] values = new ushort[quantity];
                    for (int index = 0; index < values.Length; index++)
                    {
                        int offset = 2 + index * 2;
                        values[index] = ReadUInt16(response, offset);
                    }

                    string resultText = FormatValues(values, readWriteModel.Type);
                    readWriteModel.Result = resultText;
                    WriteLog($"{LocalName} Modbus TCP read {address}, length {quantity}, result {resultText}.", LogType.INFO);
                    Task.Run(() => OnReceive(resultText, address));
                    return true;
                }
                catch (Exception ex)
                {
                    readWriteModel.Result = ex.Message;
                    WriteLog($"{LocalName} Modbus TCP read failed: {ex.Message}", LogType.ERROR);
                    return false;
                }
            }
        }

        private bool EnsureConnected()
        {
            if (IsConnected == ConnectState.Connected &&
                _tcpClient?.Connected == true &&
                _stream is not null)
            {
                return true;
            }

            return Start();
        }

        private byte[] SendRequest(byte[] pdu)
        {
            NetworkStream stream = _stream ?? throw new InvalidOperationException("Modbus TCP stream is not ready.");
            ushort transactionId = NextTransactionId();
            byte[] frame = BuildFrame(transactionId, pdu);

            stream.Write(frame, 0, frame.Length);
            byte[] header = ReadExact(stream, 7);
            ushort responseTransactionId = ReadUInt16(header, 0);
            ushort protocolId = ReadUInt16(header, 2);
            ushort length = ReadUInt16(header, 4);

            if (responseTransactionId != transactionId || protocolId != 0 || length == 0)
            {
                throw new InvalidOperationException("Invalid Modbus TCP response header.");
            }

            return ReadExact(stream, length - 1);
        }

        private ushort NextTransactionId()
        {
            unchecked
            {
                _transactionId++;
                if (_transactionId == 0)
                {
                    _transactionId = 1;
                }

                return _transactionId;
            }
        }

        private static byte[] BuildFrame(ushort transactionId, byte[] pdu)
        {
            ushort length = Convert.ToUInt16(pdu.Length + 1);
            byte[] frame = new byte[7 + pdu.Length];
            WriteUInt16(frame, 0, transactionId);
            WriteUInt16(frame, 2, 0);
            WriteUInt16(frame, 4, length);
            frame[6] = UnitId;
            Buffer.BlockCopy(pdu, 0, frame, 7, pdu.Length);
            return frame;
        }

        private static byte[] BuildReadHoldingRegistersRequest(ushort address, ushort quantity)
        {
            byte[] pdu = new byte[5];
            pdu[0] = 0x03;
            WriteUInt16(pdu, 1, address);
            WriteUInt16(pdu, 3, quantity);
            return pdu;
        }

        private static byte[] BuildWriteRequest(byte functionCode, ushort address, ushort[] values)
        {
            if (functionCode == 0x06)
            {
                byte[] pdu = new byte[5];
                pdu[0] = functionCode;
                WriteUInt16(pdu, 1, address);
                WriteUInt16(pdu, 3, values[0]);
                return pdu;
            }

            if (values.Length > 123)
            {
                throw new InvalidOperationException("Modbus write length cannot exceed 123 registers.");
            }

            byte byteCount = Convert.ToByte(values.Length * 2);
            byte[] multiplePdu = new byte[6 + byteCount];
            multiplePdu[0] = functionCode;
            WriteUInt16(multiplePdu, 1, address);
            WriteUInt16(multiplePdu, 3, Convert.ToUInt16(values.Length));
            multiplePdu[5] = byteCount;
            for (int index = 0; index < values.Length; index++)
            {
                WriteUInt16(multiplePdu, 6 + index * 2, values[index]);
            }

            return multiplePdu;
        }

        private static void ValidateResponse(byte[] response, byte functionCode)
        {
            if (response.Length == 0)
            {
                throw new InvalidOperationException("Empty Modbus response.");
            }

            if (response[0] == (byte)(functionCode | 0x80))
            {
                byte exceptionCode = response.Length > 1 ? response[1] : (byte)0;
                throw new InvalidOperationException($"Modbus exception code {exceptionCode}.");
            }

            if (response[0] != functionCode)
            {
                throw new InvalidOperationException($"Unexpected Modbus function code {response[0]}.");
            }
        }

        private static byte[] ReadExact(NetworkStream stream, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int readLength = stream.Read(buffer, offset, count - offset);
                if (readLength == 0)
                {
                    throw new InvalidOperationException("Modbus TCP connection closed.");
                }

                offset += readLength;
            }

            return buffer;
        }

        private static ushort ParseRegisterAddress(object plcAddress)
        {
            string value = plcAddress?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("Modbus register address cannot be empty.");
            }

            if (value.StartsWith("D", StringComparison.OrdinalIgnoreCase) && value.Length > 1)
            {
                value = value[1..];
            }

            int address = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : int.Parse(value, CultureInfo.InvariantCulture);

            if (address is >= 400001 and <= 465536)
            {
                address -= 400001;
            }
            else if (address is >= 40001 and <= 105536)
            {
                address -= 40001;
            }

            if (address < 0 || address > ushort.MaxValue)
            {
                throw new InvalidOperationException("Modbus register address must be between 0 and 65535.");
            }

            return Convert.ToUInt16(address);
        }

        private static ushort[] ParseWriteValues(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new InvalidOperationException("Modbus write value cannot be empty.");
            }

            return message
                .Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseUInt16)
                .ToArray();
        }

        private static ushort ParseUInt16(string rawValue)
        {
            string value = rawValue.Trim();
            int number = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.Parse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : int.Parse(value, CultureInfo.InvariantCulture);

            if (number < 0 || number > ushort.MaxValue)
            {
                throw new InvalidOperationException("Modbus write value must be between 0 and 65535.");
            }

            return Convert.ToUInt16(number);
        }

        private static string FormatValues(ushort[] values, DataType type)
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

        private static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return Convert.ToUInt16((buffer[offset] << 8) | buffer[offset + 1]);
        }

        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = Convert.ToByte(value >> 8);
            buffer[offset + 1] = Convert.ToByte(value & 0xFF);
        }

        private void CloseCore()
        {
            try
            {
                _stream?.Close();
            }
            catch
            {
            }

            try
            {
                _tcpClient?.Close();
                _tcpClient?.Dispose();
            }
            catch
            {
            }

            _stream = null;
            _tcpClient = null;
        }

        private void WriteLog(string message, LogType type)
        {
            Task.Run(() => OnLog(new LogMessageModel { Message = message, Type = type }));
        }
    }
}
