using Shared.Abstractions.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Models.Log
{
    public class LogMessageModel
    {
        public DateTime LogTime { get; set; } = DateTime.Now;
        public string Message { get; set; }
        public LogType Type { get; set; }
        public override string ToString()
        {
            return $"时间：{LogTime} 消息类型：{Type}\r\n 内容：{Message}";
        }
    }
}
