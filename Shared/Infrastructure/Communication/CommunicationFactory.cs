using Shared.Abstractions;
using Shared.Abstractions.Enum;
using Shared.Models.Communication;
using System;
using System.Collections.Generic;

namespace Shared.Infrastructure.Communication
{
    public class CommunicationFactory
    {
        private static readonly Dictionary<string, ICommunication> keyValuePairs = new Dictionary<string, ICommunication>();

        public static ICommunication CreateCommuniactionProtocol(CommuniactionConfigModel config)
        {
            ICommunication communiaction = config.Type switch
            {
                CommuniactionType.TCPClient => new TCPClient(config),
                CommuniactionType.TCPServer => new TCPServer(config),
                CommuniactionType.UDP => new UDPClient(config),
                CommuniactionType.UDPServer => new UDPServer(config),
                CommuniactionType.COM => new SerialPortComm(config),
                CommuniactionType.MX => new MxPlcCommunication(config),
                CommuniactionType.PLC => PlcCommunicationTypeNames.IsModbus(config.PLCType)
                    ? new ModbusTcpPlcCommunication(config)
                    : new MxPlcCommunication(config),
                CommuniactionType.RabbitMQRPCServer => new RabbitMQRPCServer(config),
                CommuniactionType.RabbitMQRPCClient => new RabbitMQRPCClient(config),
                _ => throw new NotSupportedException($"Unsupported communication type: {config.Type}")
            };

            if (keyValuePairs.ContainsKey(config.LocalName))
            {
                keyValuePairs[config.LocalName].Close();
                keyValuePairs[config.LocalName] = communiaction;
            }
            else
            {
                keyValuePairs.Add(config.LocalName, communiaction);
            }

            return communiaction;
        }

        public static ICommunication Get(string name)
        {
            return keyValuePairs.TryGetValue(name, out ICommunication? communication)
                ? communication
                : null!;
        }

        public static bool Remove(string name)
        {
            if (keyValuePairs.TryGetValue(name, out ICommunication? communication))
            {
                communication.Close();
                keyValuePairs.Remove(name);
            }

            return true;
        }
    }
}
