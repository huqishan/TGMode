using Shared.Models.Test;

namespace Shared.Infrastructure.Events;

public sealed class TestExecutionStatusChangedEvent : PubSubEvent<TestExecutionStatusMessage>
{
}
