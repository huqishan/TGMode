using Shared.Abstractions.Enum;
using Shared.Abstractions;
using System.Collections.Concurrent;
using System.Text;
using Shared.Models.Communication;
using Shared.Models.Log;
using System.IO.Ports;
using Shared.Infrastructure.Extensions;

namespace Shared.Infrastructure.Communication
{
    public class SerialPortComm : ICommunication
    {
        #region Propertys
        private SerialPort _SerialPort = new SerialPort();
        private Thread _ReconnectionThread;
        private AutoResetEvent IsWhile = new AutoResetEvent(false);
        private BlockingCollection<string> _RespQueue = new BlockingCollection<string>();
        private bool _lastSendIsHex;
        private ConnectState _IsConnected = ConnectState.DisConnected;
        /// <summary>
        /// TCP 客户端连接状态
        /// </summary>  
        public ConnectState IsConnected
        {
            get
            {
                if (_SerialPort == null)
                    return ConnectState.DisConnected;
                else
                    return _IsConnected;
            }
            private set
            {
                if (_IsConnected != value)
                {
                    _IsConnected = value;
                    SendState(value);
                }
            }
        }
        public string LocalName { get; }
        #endregion

        #region 构造
        public SerialPortComm(CommuniactionConfigModel config)
        {
            LocalName = config.LocalName;
            _SerialPort.PortName = config.PortName;
            _SerialPort.BaudRate = config.BaudRate;
            _SerialPort.Parity = (Parity)config.Parity;
            _SerialPort.DataBits = config.DataBits;
            _SerialPort.StopBits = (StopBits)config.StopBits;
            _SerialPort.DataReceived += SerialPort_DataReceived;
        }
        #endregion

        #region 方法
        public bool Close()
        {
            IsWhile.Set();
            if (_SerialPort.IsOpen)
            {
                _SerialPort.Close();
                _SerialPort.Dispose();
                IsConnected = _SerialPort.IsOpen ? ConnectState.Connected : ConnectState.DisConnected;
            }
            WriteLog(new LogMessageModel() { Message = $"关闭SerialPort通讯{(!_SerialPort.IsOpen ? "成功" : "失败")}！", Type = Abstractions.Enum.LogType.INFO });
            return true;
        }

        public bool Read(ref ReadWriteModel readWriteModel)
        {
            throw new NotImplementedException();
        }

        public bool Start()
        {
            if (!_SerialPort.IsOpen)
                _SerialPort.Open();
            IsWhile.Reset();
            _ReconnectionThread = new Thread(() =>
            {
                while (true)
                {
                    if (!_SerialPort.IsOpen)
                        _SerialPort.Open();
                    if (IsWhile.WaitOne(2000))
                        break;
                }
            })
            { IsBackground = true };
            _ReconnectionThread.Start();
            IsConnected = _SerialPort.IsOpen ? ConnectState.Connected : ConnectState.DisConnected;
            WriteLog(new LogMessageModel() { Message = $"开启SerialPort通讯{(_SerialPort.IsOpen ? "成功" : "失败")}！", Type = Abstractions.Enum.LogType.INFO });
            return _SerialPort.IsOpen;
        }

        public bool Write(ref ReadWriteModel readWriteModel, bool isWait = false)
        {
            if (_SerialPort.IsOpen)
            {
                _RespQueue.TakeWhile(x => x != null);
                byte[] sendData = BuildSendBytes(readWriteModel.Message);
                _SerialPort.Write(sendData, 0, sendData.Length);
                if (isWait)
                {
                    readWriteModel.Result = _RespQueue.Take();
                }
            }
            IsConnected = _SerialPort.IsOpen ? ConnectState.Connected : ConnectState.DisConnected;
            return _SerialPort.IsOpen;
        }
        public Task<bool> WriteAsync(ReadWriteModel readWriteModel)
        {
            return Task.Run(() => { return Write(ref readWriteModel); });
        }

        #endregion

        #region 事件
        public event ReceiveData OnReceive;
        public event Action<LogMessageModel> OnLog;
        public event StateChanged StateChange;

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            byte[] reDatas = new byte[_SerialPort.BytesToRead];
            _SerialPort.Read(reDatas, 0, reDatas.Length);
            _RespQueue.Add(_lastSendIsHex
                ? BitConverter.ToString(reDatas).Replace("-", string.Empty)
                : Encoding.UTF8.GetString(reDatas));
            Task.Run(() => OnReceive?.Invoke(reDatas));
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

        private void WriteLog(LogMessageModel log)
        {
            Task.Run(() => OnLog?.Invoke(log));
        }
        private void SendState(ConnectState connectState)
        {
            Task.Run(() => { StateChange?.Invoke(connectState, LocalName); });
        }
        #endregion
    }
}
