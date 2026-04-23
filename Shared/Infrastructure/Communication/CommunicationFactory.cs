using HPSocket.Tcp;
using HPSocket.Udp;
using Shared.Abstractions.Enum;
using Shared.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Models.Communication;

namespace Shared.Infrastructure.Communication
{
    public class CommunicationFactory
    {
        private static Dictionary<string, ICommunication> keyValuePairs = new Dictionary<string, ICommunication>();
        public static ICommunication CreateCommuniactionProtocol(CommuniactionConfigModel config)
        {
            ICommunication communiaction = null;
            switch (config.Type)
            {
                case CommuniactionType.TCPClient:
                    communiaction = new TCPClient(config);
                    break;
                case CommuniactionType.TCPServer:
                    communiaction = new TCPServer(config);
                    break;
                case CommuniactionType.UDP:
                    communiaction = new UDPClient(config);
                    break;
                case CommuniactionType.UDPServer:
                    communiaction = new UDPServer(config);
                    break;
                case CommuniactionType.COM:
                    communiaction = new SerialPortComm(config);
                    break;
                case CommuniactionType.RabbitMQRPCServer:
                    communiaction = new RabbitMQRPCServer(config);
                    break;
                case CommuniactionType.RabbitMQRPCClient:
                    communiaction = new RabbitMQRPCClient(config);
                    break;
                default:
                    break;
            }
            if (keyValuePairs.ContainsKey(config.LocalName))
            {
                keyValuePairs[config.LocalName].Close();
                keyValuePairs[config.LocalName] = communiaction;
            }
            else
                keyValuePairs.Add(config.LocalName, communiaction);
            return communiaction;

        }
        public static ICommunication Get(string name)
        {
            ICommunication communication = null;
            if (keyValuePairs.ContainsKey(name))
            {
                communication = keyValuePairs[name];
            }
            return communication;
        }
        public static bool Remove(string name)
        {
            if (keyValuePairs.ContainsKey(name))
            {
                keyValuePairs[name].Close();
                keyValuePairs.Remove(name);
            }
            return true;
        }
    }
}
