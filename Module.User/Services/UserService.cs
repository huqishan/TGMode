using ControlLibrary;
using Shared.Infrastructure.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Module.User.Services
{
    public class UserService : ModuleService
    {
        public UserService(IEventAggregator eventAggregator)
        {
            _EventAggregator = eventAggregator;
        }
    }
}
