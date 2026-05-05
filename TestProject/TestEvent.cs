using Shared.Infrastructure.Events;
using TestProject.Models.EventModels;

namespace TestProject
{
    public class Tests
    {
        IEventAggregator _eventAggregator;
        [SetUp]
        public void Setup()
        {
            _eventAggregator = new EventAggregator();
            _eventAggregator.GetEvent<MyMessage>().Subscribe(HandleMessage);
        }

        private void HandleMessage(MyMessage obj)
        {
            Console.WriteLine($"Name: {obj.Name}, Type: {obj.Type}, Value: {obj.Value}, Judge: {obj.Judge}");
        }

        [Test]
        public void TestMessageEvent()
        {
            _eventAggregator.GetEvent<MyMessage>().Publish(new MyMessage() { Name = "Test", Type = "TestType", Value = "TestValue", Judge = "TestJudge" });
            Assert.Pass();
        }
    }
}