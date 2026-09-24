using Shared.Infrastructure.Events;
using Shared.Abstractions.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Module.Business.Business
{
    [DeviceBusiness("System", "系统")]
    public static class System
    {
        private static IEventAggregator? _eventAggregator;

        public static void ConfigureEventAggregator(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        }

        /// <summary>
        /// 获取当前用户名
        /// </summary>
        /// <returns></returns>
        [BusinessOperation("GetCurrentUserName", "获取当前用户名")]
        public static string GetCurrentUserName()
        {
            return Environment.UserName;
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
        public sealed class MessageShowView : PubSubEvent<MessageShowView>
        {
            public string Name { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }
    }

}
