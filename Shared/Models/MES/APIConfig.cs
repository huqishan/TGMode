using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Models.MES
{
    public class APIConfig
    {
        public string ApiName { get; set; }
        public string SelectMESType { get; set; }
        public string ResultCheck { get; set; }
        public string DataStruct { get; set; }
        public bool isEnabledAPI { get; set; }
        public bool IsCommunicationQueryVisible { get; set; } = true;
        public string Remarks { get; set; }
        public string Lua { get; set; }
        #region TCP
        /// <summary>
        /// 数据结尾是否回车
        /// </summary>
        public bool IsEnter { get; set; } = false;
        /// <summary>
        /// tcp是否保持长连接
        /// </summary>
        public bool IsEnabledTCPKeepAlive { get; set; } = false;
        public string TCPLocalIpAddress { get; set; } = "127.0.0.1";
        public ushort TCPLocalPort { get; set; } = 0;
        public string TCPRemoteIpAddress { get; set; } = "127.0.0.1";
        public ushort TCPRemotePort { get; set; } = 0;
        #endregion
        public string Url { get; set; }
        #region WebServices
        public string UserName { get; set; }
        public string Password { get; set; }
        public string Action { get; set; }
        #endregion
        #region WebApi
        public string TokenUrl { get; set; }
        public string TokenName { get; set; }
        public string WebApiType { get; set; } = "POST";
        public List<WebApiHeader> Heads { get; set; } = new List<WebApiHeader>();
        #endregion
        #region FTP
        public string DownPath { get; set; }
        public bool IsDown { get; set; } = false;
        
        #endregion
    }
}
