using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Abstractions.Enum
{
    public enum CommuniactionType
    {
        TCPClient,
        TCPServer,
        MX,
        UDP,
        UDPServer,
        COM,
        RabbitMQRPCServer,
        RabbitMQRPCClient,
        PLC
    }
}
