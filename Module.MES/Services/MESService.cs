using ControlLibrary;
using Shared.Infrastructure.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Module.MES.Services
{
    public class MESService: ModuleService
    {
        public MESService(IEventAggregator eventAggregator)
        {
            _EventAggregator = eventAggregator;
        }
    }
}
