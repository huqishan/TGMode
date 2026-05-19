using Shared.Abstractions.Enum;
using Shared.Models.Communication;
using Shared.Models.Log;
using System;
using System.Threading.Tasks;

namespace Shared.Abstractions
{
    public delegate void StateChanged(ConnectState connectState, string localName);
    public delegate string ReceiveData(object message, params object[] param);
    /// <summary>
    /// 通讯协议对象（如果是通讯协议则需要继承此接口）
    /// </summary>
    public interface ICommunication
    {
        /// <summary>
        /// 自定义消息处理事件
        /// </summary>
        event ReceiveData OnReceive;
        /// <summary>
        /// 自定义状态改变事件
        /// </summary>
        event StateChanged StateChange;
        /// <summary>
        /// Log打印
        /// </summary>
        event Action<LogMessageModel> OnLog;
        /// <summary>
        /// 连接状态
        /// </summary>
        ConnectState IsConnected { get; }
        /// <summary>
        /// 名称
        /// </summary>
        string LocalName { get; }
        /// <summary>
        /// 开启
        /// </summary>
        /// <returns></returns>
        bool Start();
        /// <summary>
        /// 发送
        /// </summary>
        /// <param name="message">消息</param>
        /// <param name="receiveObj">接收者对象</param>
        /// <returns></returns>
        bool Send(ref SendReceiveModel readWriteModel, bool isWait = false);
        /// <summary>
        /// 异步发送
        /// </summary>
        /// <param name="message">消息</param>
        /// <param name="receiveObj">接收者对象</param>
        /// <returns></returns>
        Task<bool> SendAsync(SendReceiveModel readWriteModel);
        /// <summary>
        /// 读取
        /// </summary>
        /// <param name="readObj">读取对象（string/string[]）</param>
        /// <param name="lenght">长度</param>
        /// <param name="type">数据格式</param>
        /// <returns>string/string[]</returns>
        bool Receive(ref SendReceiveModel readWriteModel);
        /// <summary>
        /// 关闭
        /// </summary>
        /// <returns></returns>
        bool Close();
    }
}
