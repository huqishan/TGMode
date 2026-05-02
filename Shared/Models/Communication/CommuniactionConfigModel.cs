using Shared.Abstractions.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Models.Communication
{
    public class CommuniactionConfigModel
    {
        /// <summary>
        /// TCPServer/UDPServer
        /// </summary>
        /// <param name="localName"></param>
        /// <param name="localIpAddress"></param>
        /// <param name="LocalPort"></param>
        public CommuniactionConfigModel(bool isUdp, string localName, string localIpAddress, ushort localPort)
        {
            this.LocalName = localName;
            this.LocalIPAddress = localIpAddress;
            this.LocalPort = localPort;
            this.Type = isUdp ? CommuniactionType.UDPServer : CommuniactionType.TCPServer;
        }
        /// <summary>
        /// TCPClient/UDPClient
        /// </summary>
        /// <param name="localName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <param name="remotePort"></param>
        /// <param name="localIpAddress"></param>
        /// <param name="localPort"></param>
        public CommuniactionConfigModel(bool isUdp, string localName, string remoteIpAddress, int remotePort, string localIpAddress, int localPort)
        {
            this.LocalName = localName;
            this.RemoteIPAddress = remoteIpAddress;
            this.RemotePort = remotePort;
            this.LocalIPAddress = localIpAddress;
            this.LocalPort = localPort;
            this.Type = isUdp ? CommuniactionType.UDP : CommuniactionType.TCPClient;
        }
        /// <summary>
        /// MX
        /// </summary>
        /// <param name="localName"></param>
        /// <param name="plcActLogicalStationNumber"></param>
        /// <param name="passWord"></param>
        public CommuniactionConfigModel(string localName, int plcActLogicalStationNumber, string passWord = null)
        {
            this.LocalName = localName;
            this.PLCActLogicalStationNumber = plcActLogicalStationNumber;
            this.PassWord = passWord;
            this.Type = CommuniactionType.MX;
        }
        /// <summary>
        /// PLC/MX Component
        /// </summary>
        /// <param name="type"></param>
        /// <param name="localName"></param>
        /// <param name="plcActLogicalStationNumber"></param>
        /// <param name="passWord"></param>
        public CommuniactionConfigModel(CommuniactionType type, string localName, int plcActLogicalStationNumber, string passWord = null)
        {
            this.LocalName = localName;
            this.PLCActLogicalStationNumber = plcActLogicalStationNumber;
            this.PassWord = passWord;
            this.Type = type;
        }
        /// <summary>
        /// RabbitMQ RPC Server/Client
        /// </summary>
        /// <param name="isServer"></param>
        /// <param name="name"></param>
        /// <param name="ip"></param>
        /// <param name="port"></param>
        /// <param name="userName"></param>
        /// <param name="passWord"></param>
        public CommuniactionConfigModel(bool isServer, string name, string ip, ushort port, string userName, string passWord)
        {
            this.RemoteIPAddress = ip;
            this.RemotePort = port;
            this.UserName = userName;
            this.PassWord = passWord;
            this.LocalName = name;
            Type = isServer ? CommuniactionType.RabbitMQRPCServer : CommuniactionType.RabbitMQRPCClient;
        }
        /// <summary>
        /// COM
        /// </summary>
        /// <param name="name"></param>
        /// <param name="portName"></param>
        /// <param name="baudRate"></param>
        /// <param name="parity"></param>
        /// <param name="dataBits"></param>
        /// <param name="stopBits"></param>
        public CommuniactionConfigModel(string name, string portName, int baudRate, int parity, int dataBits, int stopBits)
        {
            this.LocalName = name;
            this.PortName = portName;
            this.BaudRate = baudRate;
            this.Parity = parity;
            this.DataBits = dataBits;
            this.StopBits = stopBits;
            Type = CommuniactionType.COM;
        }
        public CommuniactionType Type { get; private set; }
        public string LocalName { get; private set; } = null;
        public string LocalIPAddress { get; private set; } = null;
        public int LocalPort { get; private set; } = 0;
        public string RemoteIPAddress { get; private set; } = null;
        public int RemotePort { get; private set; } = 0;
        public int PLCActLogicalStationNumber { get; private set; } = 0;
        public string PassWord { get; private set; } = null;
        public int Channel { get; private set; } = 0;
        public string BaudRete { get; private set; }
        public string UserName { get; private set; } = null;
        public string PortName { get; private set; } = null;
        public int BaudRate { get; private set; } = 0;
        public int Parity { get; private set; } = 0;
        public int DataBits { get; private set; } = 0;
        public int StopBits { get; private set; } = 0;
    }
}
