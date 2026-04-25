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
    public class UDPServer : ICommunication
    {
        private readonly ConcurrentDictionary<string, IPEndPoint> _remoteEndpoints = new ConcurrentDictionary<string, IPEndPoint>();
        private readonly BlockingCollection<string> _responseQueue = new BlockingCollection<string>();
        private readonly object _serverLock = new object();

        private UdpClient? _udpServer;
        private CancellationTokenSource? _lifetimeCts;
        private Task? _receiveTask;
        private bool _lastSendIsHex;
        private ConnectState _isConnected = ConnectState.DisConnected;

        /// <summary>
        /// 服务器 IP。
        /// </summary>
        public string LocalAddress { get; private set; } = "127.0.0.1";

        /// <summary>
        /// 服务器端口。
        /// </summary>
        public ushort LocalPort { get; private set; } = 5555;

        public string LocalClientName { get; private set; } = string.Empty;

        public string LocalName { get; }

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

        public event StateChanged StateChange = delegate { };

        public event Action<LogMessageModel> OnLog = delegate { };

        public UDPServer(CommuniactionConfigModel config)
        {
            LocalAddress = config.LocalIPAddress;
            LocalPort = Convert.ToUInt16(config.LocalPort);
            LocalName = config.LocalName;
            LocalClientName = config.LocalName;
        }

        public bool Start()
        {
            if (!CheckIpAddressAndPort(LocalAddress, LocalPort.ToString()))
            {
                WriteLog(new LogMessageModel { Message = $"{LocalName} UDPServer Address or Port Error({LocalAddress}:{LocalPort})", Type = LogType.ERROR });
                return false;
            }

            lock (_serverLock)
            {
                Close();

                try
                {
                    IPAddress address = IPAddress.Parse(LocalAddress);
                    _udpServer = new UdpClient(AddressFamily.InterNetwork);
                    _udpServer.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    _udpServer.Client.Bind(new IPEndPoint(address, LocalPort));
                    _lifetimeCts = new CancellationTokenSource();
                    IsConnected = ConnectState.Connected;
                    WriteLog(new LogMessageModel { Message = $"服务器 {LocalName} ({LocalAddress}:{LocalPort}) 启动成功！", Type = LogType.INFO });
                    _receiveTask = Task.Run(() => ReceiveLoopAsync(_lifetimeCts.Token));
                    return true;
                }
                catch (Exception ex)
                {
                    IsConnected = ConnectState.DisConnected;
                    WriteLog(new LogMessageModel { Message = $"{LocalName} UDPServer Start Exception:{ex.Message}", Type = LogType.ERROR });
                    return false;
                }
            }
        }

        public bool Close()
        {
            try
            {
                _lifetimeCts?.Cancel();
                _udpServer?.Close();
                _udpServer?.Dispose();
                _udpServer = null;
                _remoteEndpoints.Clear();
                IsConnected = ConnectState.DisConnected;
                return true;
            }
            catch (Exception ex)
            {
                WriteLog(new LogMessageModel { Message = $"{LocalName} UdpServer Close Exception:{ex.Message}", Type = LogType.WARN });
                IsConnected = ConnectState.DisConnected;
                return false;
            }
        }

        public bool Read(ref ReadWriteModel readWriteModel)
        {
            int waitTime = readWriteModel.WaitTime > 0 ? readWriteModel.WaitTime : 10000;
            if (_responseQueue.TryTake(out string? response, waitTime))
            {
                readWriteModel.Result = response ?? string.Empty;
                return true;
            }

            readWriteModel.Result = $"{LocalName} UDPServer Read Timeout.";
            return false;
        }

        public bool Write(ref ReadWriteModel readWriteModel, bool isWait = false)
        {
            if (_udpServer is null || IsConnected != ConnectState.Connected)
            {
                string result = $"{LocalName} UDPServer 未启动。";
                readWriteModel.Result = result;
                WriteLog(new LogMessageModel { Message = result, Type = LogType.ERROR });
                return false;
            }

            if (!TryResolveEndpoint(readWriteModel.ClientId, out IPEndPoint? resolvedEndPoint) || resolvedEndPoint is null)
            {
                string result = $"{LocalName} 客户端错误：{readWriteModel.Message}";
                readWriteModel.Result = result;
                WriteLog(new LogMessageModel { Message = result, Type = LogType.ERROR });
                return false;
            }

            IPEndPoint remoteEndPoint = resolvedEndPoint;
            try
            {
                byte[] data = BuildSendBytes(readWriteModel.Message);
                _udpServer.Send(data, data.Length, remoteEndPoint);
                WriteLog(new LogMessageModel { Message = $"{LocalName}-->UdpClient({remoteEndPoint.Address}:{remoteEndPoint.Port}):{OnSendHandler(data)}", Type = LogType.INFO });
                return true;
            }
            catch (Exception ex)
            {
                string result = $"{LocalName} UDPServer Send Exception:{ex.Message}";
                readWriteModel.Result = result;
                WriteLog(new LogMessageModel { Message = result, Type = LogType.ERROR });
                return false;
            }
        }

        public Task<bool> WriteAsync(ReadWriteModel readWriteModel)
        {
            return Task.Run(() => Write(ref readWriteModel));
        }

        public virtual string OnReceiveHandler(byte[] data)
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

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _udpServer is not null)
                {
                    UdpReceiveResult result = await _udpServer.ReceiveAsync(token).ConfigureAwait(false);
                    string key = $"{result.RemoteEndPoint.Address}:{result.RemoteEndPoint.Port}";
                    bool isNewClient = _remoteEndpoints.TryAdd(key, result.RemoteEndPoint);
                    if (!isNewClient)
                    {
                        _remoteEndpoints[key] = result.RemoteEndPoint;
                    }
                    else
                    {
                        WriteLog(new LogMessageModel { Message = $"{LocalName} UdpClient({key}) 已连接！", Type = LogType.INFO });
                    }

                    string command = OnReceiveHandler(result.Buffer);
                    _responseQueue.Add(command);
                    WriteLog(new LogMessageModel { Message = $"UdpClient({key})-->{LocalName}:{command}", Type = LogType.INFO });
                    _ = Task.Run(() => OnReceive(command, key, result.RemoteEndPoint.Address.ToString(), result.RemoteEndPoint.Port), token);
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
                    WriteLog(new LogMessageModel { Message = $"{LocalName} UDPServer Receive Exception:{ex.Message}", Type = LogType.ERROR });
                }
            }
        }

        private bool TryResolveEndpoint(object? clientId, out IPEndPoint? remoteEndPoint)
        {
            remoteEndPoint = null;
            if (clientId is null)
            {
                return false;
            }

            if (clientId is IPEndPoint endpoint)
            {
                remoteEndPoint = endpoint;
                return true;
            }

            string? key = clientId.ToString();
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (_remoteEndpoints.TryGetValue(key, out remoteEndPoint))
            {
                return true;
            }

            string[] parts = key.Split(':');
            if (parts.Length == 2 &&
                IPAddress.TryParse(parts[0], out IPAddress? address) &&
                int.TryParse(parts[1], out int port) &&
                port > 0 &&
                port <= ushort.MaxValue)
            {
                remoteEndPoint = new IPEndPoint(address, port);
                return true;
            }

            return false;
        }

        private static bool CheckIpAddressAndPort(string ip, string port)
        {
            return IPAddress.TryParse(ip, out _) &&
                   int.TryParse(port, out int portNumber) &&
                   portNumber > 0 &&
                   portNumber <= ushort.MaxValue;
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
