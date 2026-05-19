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

        [Test]
        public void Publish_Continues_WhenSubscriberThrows()
        {
            int handledCount = 0;
            int exceptionCount = 0;
            MyMessage messageEvent = _eventAggregator.GetEvent<MyMessage>();
            messageEvent.PublicationException += (_, args) =>
            {
                exceptionCount++;
                Assert.That(args.Exception, Is.TypeOf<InvalidOperationException>());
            };

            messageEvent.Subscribe(_ => throw new InvalidOperationException("subscriber failed"), true);
            messageEvent.Subscribe(_ => handledCount++, true);

            Assert.DoesNotThrow(() => messageEvent.Publish(new MyMessage()));
            Assert.That(handledCount, Is.EqualTo(1));
            Assert.That(exceptionCount, Is.EqualTo(1));
        }
    }
}
