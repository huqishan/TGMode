using Shared.Infrastructure.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestProject.Models.EventModels
{
    public sealed class MyMessage : PubSubEvent<MyMessage>
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public string Judge { get; set; }
    }
}
