using ControlLibrary;
using ControlLibrary.Models.TestBusiness;
using Shared.Infrastructure.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Module.Business.Services
{
    public class BusinessService: ModuleService
    {
        public BusinessService(IEventAggregator eventAggregator)
        {
            _EventAggregator = eventAggregator;
            _EventAggregator.GetEvent<ShemeExecutionMessage>().Subscribe(ShemeExecutionHandle);
        }
        private void ShemeExecutionHandle(ShemeExecutionMessage obj)
        {
            _ = SchemeExecutionService.ExecuteAsync(obj.StationName, obj.SchemeName);
        }
    }
}
