using Shared.Infrastructure.Events;
using Shared.Abstractions.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Module.Business.Services;

namespace Module.Business.Business
{
    [DeviceBusiness("System", "系统")]
    public static class System
    {
        private static IEventAggregator? _eventAggregator;

        #region 初始化

        /// <summary>配置用于发布视图消息的事件聚合器。</summary>
        public static void ConfigureEventAggregator(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        }

        #endregion

        #region 业务操作

        /// <summary>
        /// 获取当前用户名
        /// </summary>
        /// <returns></returns>
        [BusinessOperation("GetCurrentUserName", "获取当前用户名")]
        public static string GetCurrentUserName()
        {
            return Environment.UserName;
        }

        /// <summary>根据工步名称执行工步配置中的全部操作。</summary>
        /// <param name="workStepName">工步配置名称。</param>
        [BusinessOperation("ExecuteWorkStep", "执行工步")]
        public static Task ExecuteWorkStep([BusinessParam("工步名称")] string workStepName)
        {
            return SchemeExecutionService.ExecuteWorkStepByNameAsync(workStepName);
        }

        /// <summary>
        /// 发送数据到视图层
        /// </summary>
        /// <param name="name">显示到界面的数据名称</param>
        /// <param name="value">数据值</param>
        [BusinessOperation("SendDataToView", "发送数据到视图层")]
        public static void SendDataToView(
            [BusinessParam("显示名称")] string name,
            [BusinessParam("数据值")] string value)
        {
            _eventAggregator?.GetEvent<MessageShowView>().Publish(new MessageShowView
            {
                Name = name,
                Value = value
            });
        }
        #endregion

        #region 视图消息事件

        /// <summary>承载发送到视图层的名称和值。</summary>
        public sealed class MessageShowView : PubSubEvent<MessageShowView>
        {
            public string Name { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }

        #endregion
    }

}
