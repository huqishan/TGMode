using ControlLibrary;
using Shared.Infrastructure.Events;

namespace Module.Communication.Services
{
    public class CommunicationService : ModuleService
    {
        public CommunicationService(IEventAggregator eventAggregator)
        {
            _EventAggregator = eventAggregator;
        }
    }
}
