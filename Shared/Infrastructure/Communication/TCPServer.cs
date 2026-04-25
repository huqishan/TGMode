using Shared.Abstractions;
using Shared.Abstractions.Enum;
using Shared.Infrastructure.Extensions;
using Shared.Models.Communication;
using Shared.Models.Log;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace Shared.Infrastructure.Communication
{
    public class TCPServer : ICommunication, ICommunicationClientSource
    {
        private const int BufferSize = 8192;

        private readonly ConcurrentDictionary<string, TcpClientSession> _clients = new ConcurrentDictionary<string, TcpClientSession>();
        private readonly object _serverLock = new object();

        private TcpListener? _listener;
        private CancellationTokenSource? _lifetimeCts;
        private Task? _acceptTask;
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

        public event Action<LogMessageModel> OnLog = delegate { };

        public event ReceiveData OnReceive = (_, _) => string.Empty;

        public event StateChanged StateChange = delegate { };

        public event CommunicationClientsChanged ClientsChanged = delegate { };

        public TCPServer(CommuniactionConfigModel config)
        {
            LocalAddress = config.LocalIPAddress;
            LocalPort = Convert.ToUInt16(config.LocalPort);
            LocalName = config.LocalName;
        }

        public bool Start()
        {
            if (!CheckIpAddressAndPort(LocalAddress, LocalPort.ToString()))
            {
                WriteLog(new LogMessageModel { Message = $"{LocalName} TCP Address or Port Error({LocalAddress}:{LocalPort})", Type = LogType.ERROR });
                return false;
            }

            lock (_serverLock)
            {
                Close();

                try
                {
                    IPAddress address = IPAddress.Parse(LocalAddress);
                    _listener = new TcpListener(address, LocalPort);
                    _listener.Start();
                    _lifetimeCts = new CancellationTokenSource();
                    IsConnected = ConnectState.Connected;
                    WriteLog(new LogMessageModel { Message = $"服务器 {LocalName} ({LocalAddress}:{LocalPort}) 启动成功！", Type = LogType.INFO });
                    _acceptTask = Task.Run(() => AcceptLoopAsync(_lifetimeCts.Token));
                    return true;
                }
                catch (Exception ex)
                {
                    IsConnected = ConnectState.DisConnected;
                    WriteLog(new LogMessageModel { Message = $"{LocalName} TCP Start Exception:{ex.Message}", Type = LogType.ERROR });
                    return false;
                }
            }
        }

        public bool Close()
        {
            try
            {
                _lifetimeCts?.Cancel();
                _listener?.Stop();

                foreach (TcpClientSession session in _clients.Values)
                {
                    session.Close();
                }

                _clients.Clear();
                NotifyClientsChanged();
                _listener = null;
                IsConnected = ConnectState.DisConnected;
                return true;
            }
            catch (Exception ex)
            {
                WriteLog(new LogMessageModel { Message = $"{LocalName} TCP Close Exception:{ex.Message}", Type = LogType.WARN });
                IsConnected = ConnectState.DisConnected;
                return false;
            }
        }

        public bool Read(ref ReadWriteModel readWriteModel)
        {
            return true;
        }

        public bool Write(ref ReadWriteModel readWriteModel, bool isWait = false)
        {
            if (string.IsNullOrEmpty(readWriteModel.Message))
            {
                string result = $"{LocalName} 发送内容不能为空。";
                readWriteModel.Result = result;
                WriteLog(new LogMessageModel { Message = result, Type = LogType.ERROR });
                return false;
            }

            if (!TryResolveClient(readWriteModel.ClientId, out TcpClientSession? resolvedSession) || resolvedSession is null)
            {
                string result = $"{LocalName} 客户端错误：{readWriteModel.Message}";
                readWriteModel.Result = result;
                WriteLog(new LogMessageModel { Message = result, Type = LogType.ERROR });
                return false;
            }

            TcpClientSession session = resolvedSession;
            try
            {
                byte[] data = BuildSendBytes(readWriteModel.Message);
                session.Send(data);
                WriteLog(new LogMessageModel { Message = $"{LocalName}-->TcpClient({session.Key}):{OnSendHandler(data)}", Type = LogType.INFO });
                return true;
            }
            catch (Exception ex)
            {
                string result = $"{LocalName} TCP Send Exception:{ex.Message}";
                readWriteModel.Result = result;
                WriteLog(new LogMessageModel { Message = result, Type = LogType.ERROR });
                RemoveClient(session);
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

        public IReadOnlyList<CommunicationClientInfo> GetConnectedClients()
        {
            return _clients.Values
                .OrderBy(session => session.Key, StringComparer.OrdinalIgnoreCase)
                .Select(session => new CommunicationClientInfo(
                    session.Key,
                    $"{session.RemoteEndPoint.Address}:{session.RemoteEndPoint.Port}",
                    session.RemoteEndPoint.Address.ToString(),
                    session.RemoteEndPoint.Port))
                .ToList();
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _listener is not null)
                {
                    System.Net.Sockets.TcpClient tcpClient = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                    RegisterClient(tcpClient, token);
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
                    WriteLog(new LogMessageModel { Message = $"{LocalName} TCP Accept Exception:{ex.Message}", Type = LogType.ERROR });
                    IsConnected = ConnectState.DisConnected;
                }
            }
        }

        private void RegisterClient(System.Net.Sockets.TcpClient tcpClient, CancellationToken token)
        {
            if (tcpClient.Client.RemoteEndPoint is not IPEndPoint remoteEndPoint)
            {
                tcpClient.Close();
                WriteLog(new LogMessageModel { Message = $"{LocalName} Get TcpClient RemoteEndPoint Fail！", Type = LogType.ERROR });
                return;
            }

            tcpClient.NoDelay = true;
            string key = $"{remoteEndPoint.Address}:{remoteEndPoint.Port}";
            TcpClientSession session = new TcpClientSession(key, remoteEndPoint, tcpClient);
            if (_clients.TryGetValue(key, out TcpClientSession? oldSession))
            {
                oldSession.Close();
            }

            _clients[key] = session;
            WriteLog(new LogMessageModel { Message = $"{LocalName} TcpClient({key}) 已连接！", Type = LogType.INFO });
            NotifyClientsChanged();
            _ = Task.Run(() => ClientReceiveLoopAsync(session, token), token);
        }

        private async Task ClientReceiveLoopAsync(TcpClientSession session, CancellationToken token)
        {
            byte[] buffer = new byte[BufferSize];
            try
            {
                while (!token.IsCancellationRequested && session.Client.Connected)
                {
                    int length = await session.Stream.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (length == 0)
                    {
                        break;
                    }

                    byte[] data = buffer[..length];
                    string command = OnReceiveHandler(data);
                    WriteLog(new LogMessageModel { Message = $"TcpClient({session.Key})-->{LocalName}:{command}", Type = LogType.INFO });
                    _ = Task.Run(() => OnReceive(command, session.Key, session.RemoteEndPoint.Address.ToString(), session.RemoteEndPoint.Port), token);
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
                    WriteLog(new LogMessageModel { Message = $"{LocalName} TcpClient({session.Key}) Receive Exception:{ex.Message}", Type = LogType.ERROR });
                }
            }
            finally
            {
                RemoveClient(session);
            }
        }

        private void RemoveClient(TcpClientSession session)
        {
            if (_clients.TryGetValue(session.Key, out TcpClientSession? current) && ReferenceEquals(current, session))
            {
                _clients.TryRemove(session.Key, out _);
                session.Close();
                WriteLog(new LogMessageModel { Message = $"{LocalName} TcpClient({session.Key}) 断开连接！", Type = LogType.WARN });
                NotifyClientsChanged();
            }
        }

        private bool TryResolveClient(object? clientId, out TcpClientSession? session)
        {
            session = null;
            if (clientId is null)
            {
                return false;
            }

            if (clientId is TcpClientSession tcpClientSession)
            {
                session = tcpClientSession;
                return true;
            }

            string? key = clientId.ToString();
            return !string.IsNullOrWhiteSpace(key) && _clients.TryGetValue(key, out session);
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

        private void NotifyClientsChanged()
        {
            IReadOnlyList<CommunicationClientInfo> clients = GetConnectedClients();
            Task.Run(() => ClientsChanged(clients));
        }

        private sealed class TcpClientSession
        {
            private readonly object _writeLock = new object();

            public TcpClientSession(string key, IPEndPoint remoteEndPoint, System.Net.Sockets.TcpClient client)
            {
                Key = key;
                RemoteEndPoint = remoteEndPoint;
                Client = client;
                Stream = client.GetStream();
            }

            public string Key { get; }

            public IPEndPoint RemoteEndPoint { get; }

            public System.Net.Sockets.TcpClient Client { get; }

            public NetworkStream Stream { get; }

            public void Send(byte[] data)
            {
                lock (_writeLock)
                {
                    Stream.Write(data, 0, data.Length);
                }
            }

            public void Close()
            {
                try
                {
                    Stream.Close();
                }
                catch
                {
                }

                try
                {
                    Client.Close();
                    Client.Dispose();
                }
                catch
                {
                }
            }
        }
    }
}
