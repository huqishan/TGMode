using Shared.Abstractions.Enum;
using Shared.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Models.Communication;
using Shared.Models.Log;

namespace Shared.Infrastructure.Communication
{
    public class RabbitMQRPCClient : ICommunication
    {
        #region Propertys
        private IConnection _Connection;
        private IModel _Channel;
        private string _ReplyQueueName;
        private EventingBasicConsumer _Consumer;
        private BlockingCollection<string> _RespQueue = new BlockingCollection<string>();
        private IBasicProperties _Props;
        private ConnectionFactory _Factory;
        public string LocalName { get; }
        private ConnectState _IsConnected = ConnectState.DisConnected;
        public ConnectState IsConnected
        {
            get
            {
                if (_Connection == null)
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
        #endregion
        #region 构造
        public RabbitMQRPCClient(CommuniactionConfigModel config)
        {
            LocalName = config.LocalName;
            _Factory = new ConnectionFactory() { HostName = config.RemoteIPAddress, UserName = config.UserName, Password = config.PassWord };

        }
        #endregion
        #region 方法
        public bool Close()
        {
            _Connection?.Close();
            _Connection?.Dispose();
            IsConnected = ConnectState.DisConnected;
            WriteLog(new LogMessageModel() { Message = $"断开RabbitMQ成功!", Type = Abstractions.Enum.LogType.WARN });
            return true;
        }

        public bool Receive(ref SendReceiveModel readWriteModel)
        {
            return true;
        }

        public bool Start()
        {
            _Connection = _Factory.CreateConnection();
            _Channel = _Connection.CreateModel();
            _ReplyQueueName = _Channel.QueueDeclare().QueueName;
            _Consumer = new EventingBasicConsumer(_Channel);
            _Props = _Channel.CreateBasicProperties();
            var correlationId = Guid.NewGuid().ToString();
            _Props.CorrelationId = correlationId;
            _Props.ReplyTo = _ReplyQueueName;
            _Props.DeliveryMode = 2;//消息持久化
            _Consumer.Received += (model, ea) =>
            {
                if (ea.BasicProperties.CorrelationId == correlationId)
                {
                    var body = ea.Body;
                    var response = Encoding.UTF8.GetString(body.ToArray());
                    WriteLog(new LogMessageModel() { Message = $"接收到Server反馈：{response}", Type = Abstractions.Enum.LogType.INFO });
                    _RespQueue.Add(response);
                }
            };
            _Channel.BasicConsume(consumer: _Consumer, queue: _ReplyQueueName, autoAck: true);
            IsConnected = ConnectState.Connected;
            return true;
        }

        public bool Send(ref SendReceiveModel readWriteModel, bool isWait = false)
        {
            try
            {
                _RespQueue.TakeWhile(x => x != null);
                var messageBytes = Encoding.UTF8.GetBytes(readWriteModel.Message);
                _Channel.BasicPublish(exchange: "", routingKey: "rpc_queue", basicProperties: _Props, body: messageBytes);
                if (isWait)
                {
                    readWriteModel.Result = _RespQueue.Take();
                }
                else
                {
                    OnReceive?.Invoke(_RespQueue.Take());
                }
            }
            catch (Exception ex)
            {
                WriteLog(new LogMessageModel() { Message = $"发送失败：{ex.Message}", Type = Abstractions.Enum.LogType.ERROR });
                readWriteModel.Result = $"发送失败：{ex.Message}";
                IsConnected = ConnectState.DisConnected;
                return false;
            }
            return true;
        }

        public Task<bool> SendAsync(SendReceiveModel readWriteModel)
        {
            return Task.FromResult(Send(ref readWriteModel));
        }
        #endregion
        #region 事件
        public event ReceiveData OnReceive;
        public event Action<LogMessageModel> OnLog;
        public event StateChanged StateChange;

        private void WriteLog(LogMessageModel message)
        {
            Task.Run(() => { OnLog?.Invoke(message); });
        }
        private void SendState(ConnectState connectState)
        {
            Task.Run(() => { StateChange?.Invoke(connectState, LocalName); });
        }
        #endregion
    }
}
