using Shared.Infrastructure.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControlLibrary.Models.TestBusiness
{
    public class ShemeExecutionMessage:PubSubEvent<ShemeExecutionMessage>
    {
        public string? StationName { get; set; }
        public string? SchemeName { get; set; }
    }
}
