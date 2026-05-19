using Shared.Abstractions.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Models.Communication
{
    public class SendReceiveModel
    {
        /// <summary>
        /// TCP Client/UDP Client
        /// </summary>
        /// <param name="message"></param>
        public SendReceiveModel(string message, int waitTime = 10000)
        {
            this.Message = message;
            WaitTime=waitTime;
        }
        /// <summary>
        /// TCP Server/UDP Server
        /// </summary>
        /// <param name="message"></param>
        /// <param name="recciveObj"></param>
        public SendReceiveModel(string message, object recciveObj)
        {
            this.Message = message;
            this.ClientId = recciveObj;
        }
        /// <summary>
        /// MX
        /// </summary>
        /// <param name="message"></param>
        /// <param name="plcAddress"></param>
        /// <param name="lenght"></param>
        /// <param name="type"></param>
        public SendReceiveModel(string message, object plcAddress, int lenght, DataType type = DataType.Decimal)
        {
            this.Message = message;
            this.PLCAddress = plcAddress;
            this.Lenght = lenght;
            this.Type = type;
        }
        public int WaitTime = 10000;
        public string Message { get; private set; }
        public object ClientId { get; private set; }
        public object PLCAddress { get; private set; }
        public int Lenght { get; private set; }
        public DataType Type { get; private set; }
        /// <summary>
        /// 反馈结果
        /// </summary>
        public object Result { get; set; }
    }
}
