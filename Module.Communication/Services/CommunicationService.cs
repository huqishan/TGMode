using ControlLibrary;
using ControlLibrary.Models.TestToBusiness;
using Shared.Infrastructure.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Module.Communication.Services
{
    public class CommunicationService: ModuleService
    {
        public CommunicationService(IEventAggregator eventAggregator)
        {
            _EventAggregator = eventAggregator;
        }
    }
}
