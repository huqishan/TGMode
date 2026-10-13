using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Global
{
    public static class Global_Event
    {
        #region 打印Log
        public delegate void WriteLogDelegate(string message, string title);
        public static event WriteLogDelegate WriteLogEvent;
        public static void WriteLog(string message, string title)
        {
            WriteLogEvent?.Invoke(message, title);
        }
        #endregion
    }
}
