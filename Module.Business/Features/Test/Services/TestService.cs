using ControlLibrary;
using Shared.Infrastructure.Events;
using System;

namespace Module.Business.Test.Services
{
    /// <summary>
    /// 业务模块测试服务。
    /// 继承模块服务基类，用于在模块启动流程中占位注册，当前不承载具体业务逻辑。
    /// </summary>
    public class TestService : ModuleService
    {
        /// <summary>
        /// 构造函数，保存事件聚合器供模块服务基类使用。
        /// </summary>
        /// <param name="eventAggregator">事件聚合器，用于模块间松耦合通信。</param>
        public TestService(IEventAggregator eventAggregator)
        {
            _EventAggregator = eventAggregator;
        }
    }
}
