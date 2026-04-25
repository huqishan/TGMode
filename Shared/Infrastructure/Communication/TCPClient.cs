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
    public class TCPClient : ICommunication
    {
        private const int BufferSize = 8192;
        private const int ReconnectIntervalMilliseconds = 1000;
        private const int ConnectRetryCount = 10;

        private readonly object _connectionLock = new object();
        private readonly object _writeLock = new object();
        private readonly BlockingCollection<string> _responseQueue = new BlockingCollection<string>();
        private readonly string _localAddress;
        private readonly int _localPort;

        private System.Net.Sockets.TcpClient? _tcpClient;
        private NetworkStream? _networkStream;
        private CancellationTokenSource? _lifetimeCts;
        private Task? _receiveTask;
        private Task? _reconnectTask;
        private bool _lastSendIsHex;
        private ConnectState _isConnected = ConnectState.DisConnected;

        /// <summary>
        /// 远程服务器 IP 地址。
        /// </summary>
        public string RemoteAddress { get; private set; } = "127.0.0.1";

        /// <summary>
        /// 远程服务器端口。
        /// </summary>
        public ushort RemotePort { get; private set; } = 5555;

        /// <summary>
        /// TCP 客户端名称。
        /// </summary>
        public string LocalClientName { get; private set; } = string.Empty;

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

        public string LocalName { get; }

        public event Action<LogMessageModel> OnLog = delegate { };

        public event ReceiveData OnReceive = (_, _) => string.Empty;

        public event StateChanged StateChange = delegate { };

        public TCPClient(CommuniactionConfigModel config)
        {
            LocalName = config.LocalName;
            LocalClientName = config.LocalName;
            RemoteAddress = config.RemoteIPAddress;
            RemotePort = Convert.ToUInt16(config.RemotePort);
            _localAddress = config.LocalIPAddress;
            _localPort = config.LocalPort;
        }

        /// <summary>
        /// 连接服务器；后台会持续重连，避免设备短暂断线后必须手动重启连接。
        /// </summary>
        public bool Start()
        {
            if (!CheckIpAddressAndPort(RemoteAddress, RemotePort.ToString()))
            {
                WriteLog(new LogMessageModel { Message = $"{LocalClientName} TCP Address or Port Error({RemoteAddress}:{RemotePort})", Type = LogType.ERROR });
                return false;
            }

            Close();
            _lifetimeCts = new CancellationTokenSource();
            bool connected = TryConnectOnce(_lifetimeCts.Token);
            _reconnectTask = Task.Run(() => ReconnectLoopAsync(_lifetimeCts.Token));
            return connected;
        }

        public bool Close()
        {
            try
            {
                _lifetimeCts?.Cancel();
                CloseCurrentSocket();
                IsConnected = ConnectState.DisConnected;
                return true;
            }
            catch (Exception ex)
            {
                WriteLog(new LogMessageModel { Message = $"{LocalClientName} TCP Stop Exception:{ex.Message}", Type = LogType.ERROR });
                return false;
            }
        }

        public bool Write(ref ReadWriteModel readWriteModel, bool isWait = false)
        {
            if (string.IsNullOrEmpty(readWriteModel.Message))
            {
                string result = $"{LocalClientName} TCP Command is empty.";
                readWriteModel.Result = result;
                WriteLog(new LogMessageModel { Message = result, Type = LogType.ERROR });
                return false;
            }

            if (!EnsureConnected())
            {
                string result = $"{LocalClientName} TCP Connect Exception Command:{readWriteModel.Message},TcpClientIsConnect:false";
                readWriteModel.Result = result;
                WriteLog(new LogMessageModel { Message = result, Type = LogType.ERROR });
                return false;
            }

            try
            {
                lock (_writeLock)
                {
                    ClearQueue();
                    byte[] data = BuildSendBytes(readWriteModel.Message);
                    NetworkStream? stream = _networkStream;
                    if (stream is null || _tcpClient?.Connected != true)
                    {
                        string result = $"{LocalClientName} TCP Connect Exception Command:{readWriteModel.Message},TcpClientIsConnect:false";
                        readWriteModel.Result = result;
                        WriteLog(new LogMessageModel { Message = result, Type = LogType.ERROR });
                        return false;
                    }

                    stream.Write(data, 0, data.Length);
                    WriteLog(new LogMessageModel { Message = $"{LocalClientName}-->服务器({RemoteAddress}:{RemotePort}) : {OnSendHandler(data)}", Type = LogType.INFO });

                    if (isWait)
                    {
                        int waitTime = readWriteModel.WaitTime > 0 ? readWriteModel.WaitTime : 10000;
                        if (_responseQueue.TryTake(out string? response, waitTime))
                        {
                            readWriteModel.Result = response ?? string.Empty;
                            return true;
                        }

                        string result = $"{LocalClientName},TcpClientIsConnect:{_tcpClient?.Connected == true} 接受数据超时！！！";
                        readWriteModel.Result = result;
                        WriteLog(new LogMessageModel { Message = result, Type = LogType.INFO });
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                IsConnected = ConnectState.DisConnected;
                string result = $"{LocalClientName} TCP Send Exception:{ex.Message}";
                readWriteModel.Result = result;
                WriteLog(new LogMessageModel { Message = result, Type = LogType.ERROR });
                return false;
            }
        }

        public Task<bool> WriteAsync(ReadWriteModel readWriteModel)
        {
            return Task.Run(() => Write(ref readWriteModel));
        }

        public bool Read(ref ReadWriteModel readWriteModel)
        {
            int waitTime = readWriteModel.WaitTime > 0 ? readWriteModel.WaitTime : 10000;
            if (_responseQueue.TryTake(out string? response, waitTime))
            {
                readWriteModel.Result = response ?? string.Empty;
                return true;
            }

            readWriteModel.Result = $"{LocalClientName} TCP Read Timeout.";
            return false;
        }

        public static bool CheckIpAddressAndPort(string ip, string port)
        {
            return !string.IsNullOrWhiteSpace(ip) &&
                   int.TryParse(port, out int portNumber) &&
                   portNumber > 0 &&
                   portNumber <= ushort.MaxValue;
        }

        public virtual string[] OnReceiveHandler(byte[] data)
        {
            if (_lastSendIsHex)
            {
                return new[] { BitConverter.ToString(data).Replace("-", string.Empty) };
            }

            return new[] { Encoding.UTF8.GetString(data) };
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

        private bool EnsureConnected()
        {
            if (IsSocketConnected())
            {
                return true;
            }

            CancellationToken token = _lifetimeCts?.Token ?? CancellationToken.None;
            for (int index = 0; index < ConnectRetryCount && !token.IsCancellationRequested; index++)
            {
                if (TryConnectOnce(token))
                {
                    return true;
                }

                Thread.Sleep(ReconnectIntervalMilliseconds);
            }

            return false;
        }

        private async Task ReconnectLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (!IsSocketConnected())
                {
                    TryConnectOnce(token);
                }

                try
                {
                    await Task.Delay(ReconnectIntervalMilliseconds, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private bool TryConnectOnce(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return false;
            }

            lock (_connectionLock)
            {
                if (IsSocketConnected())
                {
                    return true;
                }

                try
                {
                    WriteLog(new LogMessageModel { Message = $"{LocalClientName} 正在连接服务器({RemoteAddress}:{RemotePort})........", Type = LogType.INFO });
                    CloseCurrentSocket();

                    System.Net.Sockets.TcpClient tcpClient = CreateTcpClient();
                    tcpClient.NoDelay = true;
                    tcpClient.ReceiveBufferSize = 1024 * 1024;
                    tcpClient.SendBufferSize = 1024 * 1024;
                    tcpClient.Connect(RemoteAddress, RemotePort);

                    _tcpClient = tcpClient;
                    _networkStream = tcpClient.GetStream();
                    IsConnected = ConnectState.Connected;
                    WriteLog(new LogMessageModel { Message = $"{LocalClientName} 连接服务器({RemoteAddress}:{RemotePort}) 成功！", Type = LogType.INFO });
                    _receiveTask = Task.Run(() => ReceiveLoopAsync(tcpClient, _networkStream, token), token);
                    return true;
                }
                catch (Exception ex)
                {
                    CloseCurrentSocket();
                    IsConnected = ConnectState.DisConnected;
                    WriteLog(new LogMessageModel { Message = $"{LocalClientName} TCP Connect Exception:{ex.Message}", Type = LogType.ERROR });
                    return false;
                }
            }
        }

        private System.Net.Sockets.TcpClient CreateTcpClient()
        {
            System.Net.Sockets.TcpClient tcpClient = new System.Net.Sockets.TcpClient(AddressFamily.InterNetwork);
            if (!string.IsNullOrWhiteSpace(_localAddress) || _localPort > 0)
            {
                IPAddress address = IPAddress.TryParse(_localAddress, out IPAddress? parsedAddress)
                    ? parsedAddress
                    : IPAddress.Any;
                tcpClient.Client.Bind(new IPEndPoint(address, Math.Max(0, _localPort)));
            }

            return tcpClient;
        }

        private async Task ReceiveLoopAsync(System.Net.Sockets.TcpClient tcpClient, NetworkStream stream, CancellationToken token)
        {
            byte[] buffer = new byte[BufferSize];
            try
            {
                while (!token.IsCancellationRequested && tcpClient.Connected)
                {
                    int length = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (length == 0)
                    {
                        break;
                    }

                    byte[] data = buffer[..length];
                    foreach (string item in OnReceiveHandler(data))
                    {
                        WriteLog(new LogMessageModel { Message = $"服务器{RemoteAddress}:{RemotePort}-->{LocalClientName}:{item}", Type = LogType.INFO });
                        _responseQueue.Add(item);
                        _ = Task.Run(() => OnReceive(item, RemoteAddress, RemoteAddress, RemotePort), token);
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
                WriteLog(new LogMessageModel { Message = $"{LocalClientName} TCP Receive Exception:{ex.Message}", Type = LogType.ERROR });
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    CloseSocketIfCurrent(tcpClient);
                    IsConnected = ConnectState.DisConnected;
                    WriteLog(new LogMessageModel { Message = $"{LocalClientName} 与服务器({RemoteAddress}:{RemotePort}) 断开连接！", Type = LogType.WARN });
                }
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

        private bool IsSocketConnected()
        {
            return IsConnected == ConnectState.Connected &&
                   _tcpClient?.Connected == true &&
                   _networkStream is not null;
        }

        private void CloseCurrentSocket()
        {
            try
            {
                _networkStream?.Close();
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

            _networkStream = null;
            _tcpClient = null;
        }

        private void CloseSocketIfCurrent(System.Net.Sockets.TcpClient tcpClient)
        {
            lock (_connectionLock)
            {
                if (ReferenceEquals(_tcpClient, tcpClient))
                {
                    CloseCurrentSocket();
                }
                else
                {
                    try
                    {
                        tcpClient.Close();
                        tcpClient.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void ClearQueue()
        {
            while (_responseQueue.TryTake(out _))
            {
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
