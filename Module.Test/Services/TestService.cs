using ControlLibrary;
using Shared.Infrastructure.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Module.Test.Services
{
    public class TestService: ModuleService
    {
        public TestService(IEventAggregator eventAggregator)
        {
            _EventAggregator = eventAggregator;
        }
    }
}
