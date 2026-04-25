using Shared.Abstractions;
using Shared.Abstractions.Enum;
using Shared.Infrastructure.Extensions;
using Shared.Models.Communication;
using Shared.Models.Log;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shared.Infrastructure.Communication
{
    public class UDPClient : ICommunication
    {
        private readonly BlockingCollection<byte[]> _responseQueue = new BlockingCollection<byte[]>();
        private readonly object _clientLock = new object();
        private readonly string _localAddress;
        private readonly int _localPort;

        private UdpClient? _udpClient;
        private CancellationTokenSource? _lifetimeCts;
        private Task? _receiveTask;
        private bool _lastSendIsHex;
        private ConnectState _isConnected = ConnectState.DisConnected;

        /// <summary>
        /// 远程连接 IP。
        /// </summary>
        public string RemoteAddress { get; private set; } = "127.0.0.1";

        /// <summary>
        /// 远程端口。
        /// </summary>
        public ushort RemotePort { get; private set; } = 5555;

        public string LocalName { get; private set; } = string.Empty;

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
                SendState(value);
            }
        }

        public event ReceiveData OnReceive = (_, _) => string.Empty;

        public event Action<LogMessageModel> OnLog = delegate { };

        public event StateChanged StateChange = delegate { };

        public UDPClient(CommuniactionConfigModel config)
        {
            _localAddress = config.LocalIPAddress;
            _localPort = config.LocalPort;
            RemoteAddress = config.RemoteIPAddress;
            RemotePort = Convert.ToUInt16(config.RemotePort);
            LocalName = config.LocalName;
        }

        public bool Start()
        {
            if (!CheckIpAddressAndPort(RemoteAddress, RemotePort.ToString()))
            {
                WriteLog(new LogMessageModel { Message = $"{LocalName} UDP Address or Port Error({RemoteAddress}:{RemotePort})", Type = LogType.ERROR });
                return false;
            }

            lock (_clientLock)
            {
                Close();

                try
                {
                    _udpClient = CreateUdpClient();
                    _udpClient.Connect(RemoteAddress, RemotePort);
                    _lifetimeCts = new CancellationTokenSource();
                    IsConnected = ConnectState.Connected;
                    WriteLog(new LogMessageModel { Message = $"{LocalName} 连接服务器({RemoteAddress}:{RemotePort}) 成功！", Type = LogType.INFO });
                    _receiveTask = Task.Run(() => ReceiveLoopAsync(_lifetimeCts.Token));
                    return true;
                }
                catch (Exception ex)
                {
                    IsConnected = ConnectState.DisConnected;
                    WriteLog(new LogMessageModel { Message = $"{LocalName} UDP Connect Exception:{ex.Message}", Type = LogType.ERROR });
                    return false;
                }
            }
        }

        public bool Close()
        {
            try
            {
                _lifetimeCts?.Cancel();
                _udpClient?.Close();
                _udpClient?.Dispose();
                _udpClient = null;
                IsConnected = ConnectState.DisConnected;
                return true;
            }
            catch (Exception ex)
            {
                WriteLog(new LogMessageModel { Message = $"{LocalName} UDP Stop Exception:{ex.Message}", Type = LogType.ERROR });
                IsConnected = ConnectState.DisConnected;
                return false;
            }
        }

        public bool Read(ref ReadWriteModel readWriteModel)
        {
            int waitTime = readWriteModel.WaitTime > 0 ? readWriteModel.WaitTime : 10000;
            if (_responseQueue.TryTake(out byte[]? data, waitTime))
            {
                readWriteModel.Result = data is null ? string.Empty : Encoding.UTF8.GetString(data);
                return true;
            }

            readWriteModel.Result = $"{LocalName} UDP Read Timeout.";
            return false;
        }

        public bool Write(ref ReadWriteModel readWriteModel, bool isWait = false)
        {
            if (_udpClient is null || IsConnected != ConnectState.Connected)
            {
                string result = $"{LocalName} UDP 未连接。";
                readWriteModel.Result = result;
                WriteLog(new LogMessageModel { Message = result, Type = LogType.ERROR });
                return false;
            }

            try
            {
                byte[] data = BuildSendBytes(readWriteModel.Message);
                _udpClient.Send(data, data.Length);
                WriteLog(new LogMessageModel { Message = $"{LocalName}-->服务器({RemoteAddress}:{RemotePort}) : {OnSendHandler(data)}", Type = LogType.INFO });

                if (isWait)
                {
                    int waitTime = readWriteModel.WaitTime > 0 ? readWriteModel.WaitTime : 10000;
                    if (_responseQueue.TryTake(out byte[]? response, waitTime))
                    {
                        readWriteModel.Result = response is null ? string.Empty : Encoding.UTF8.GetString(response);
                        return true;
                    }

                    string result = $"{LocalName} UDP 接受数据超时！！！";
                    readWriteModel.Result = result;
                    WriteLog(new LogMessageModel { Message = result, Type = LogType.INFO });
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                string result = $"{LocalName} UDP Send Exception:{ex.Message}";
                readWriteModel.Result = result;
                WriteLog(new LogMessageModel { Message = result, Type = LogType.ERROR });
                return false;
            }
        }

        public Task<bool> WriteAsync(ReadWriteModel readWriteModel)
        {
            return Task.Run(() => Write(ref readWriteModel));
        }

        public virtual string[] OnReceiveHandler(byte[] data)
        {
            return new[]
            {
                _lastSendIsHex
                    ? BitConverter.ToString(data).Replace("-", string.Empty)
                    : Encoding.UTF8.GetString(data)
            };
        }

        public virtual string OnSendHandler(byte[] data)
        {
            try
            {
                return _lastSendIsHex
                    ? BitConverter.ToString(data).Replace("-", string.Empty)
                    : Encoding.UTF8.GetString(data);
            }
            catch
            {
                return string.Empty;
            }
        }

        private byte[] BuildSendBytes(string message)
        {
            if (message.TrimStart().StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                _lastSendIsHex = true;
                return NormalizeHexCommand(message).HexStringToByteArray();
            }

            _lastSendIsHex = false;
            return Encoding.UTF8.GetBytes(message);
        }

        private static string NormalizeHexCommand(string message)
        {
            string normalized = message.Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase);
            normalized = normalized.Replace(" ", string.Empty, StringComparison.Ordinal);
            normalized = normalized.Replace("-", string.Empty, StringComparison.Ordinal);
            normalized = normalized.Replace(",", string.Empty, StringComparison.Ordinal);
            normalized = normalized.Replace("_", string.Empty, StringComparison.Ordinal);
            normalized = normalized.Replace("\r", string.Empty, StringComparison.Ordinal);
            normalized = normalized.Replace("\n", string.Empty, StringComparison.Ordinal);
            normalized = normalized.Replace("\t", string.Empty, StringComparison.Ordinal);
            return normalized.Trim();
        }

        public static bool CheckIpAddressAndPort(string ip, string port)
        {
            return !string.IsNullOrWhiteSpace(ip) &&
                   int.TryParse(port, out int portNumber) &&
                   portNumber > 0 &&
                   portNumber <= ushort.MaxValue;
        }

        private UdpClient CreateUdpClient()
        {
            if (string.IsNullOrWhiteSpace(_localAddress) && _localPort <= 0)
            {
                return new UdpClient(AddressFamily.InterNetwork);
            }

            IPAddress address = IPAddress.TryParse(_localAddress, out IPAddress? parsedAddress)
                ? parsedAddress
                : IPAddress.Any;
            UdpClient udpClient = new UdpClient(AddressFamily.InterNetwork);
            udpClient.Client.Bind(new IPEndPoint(address, Math.Max(0, _localPort)));
            return udpClient;
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _udpClient is not null)
                {
                    UdpReceiveResult result = await _udpClient.ReceiveAsync(token).ConfigureAwait(false);
                    byte[] data = result.Buffer;
                    string[] commands = OnReceiveHandler(data);
                    string endpointText = $"{result.RemoteEndPoint.Address}:{result.RemoteEndPoint.Port}";

                    foreach (string command in commands)
                    {
                        WriteLog(new LogMessageModel { Message = $"服务器({endpointText})-->{LocalName}:{command}", Type = LogType.INFO });
                        _responseQueue.Add(data);
                        _ = Task.Run(() => OnReceive(command, endpointText, result.RemoteEndPoint.Address.ToString(), result.RemoteEndPoint.Port), token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    IsConnected = ConnectState.DisConnected;
                    WriteLog(new LogMessageModel { Message = $"{LocalName} UDP Receive Exception:{ex.Message}", Type = LogType.ERROR });
                }
            }
        }

        private void WriteLog(LogMessageModel message)
        {
            Task.Run(() => OnLog(message));
        }

        private void SendState(ConnectState connectState)
        {
            Task.Run(() => StateChange(connectState, LocalName));
        }
    }
}
