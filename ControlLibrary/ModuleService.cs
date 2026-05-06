using Shared.Infrastructure.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ControlLibrary
{
    public abstract class ModuleService
    {
        protected IEventAggregator _EventAggregator;
    }
}
