using System;

namespace Shared.Infrastructure.Events
{
    public sealed class EventPublicationExceptionEventArgs : EventArgs
    {
        public EventPublicationExceptionEventArgs(Type eventType, Exception exception, DateTime occurredAt)
        {
            EventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
            OccurredAt = occurredAt;
        }

        public Type EventType { get; }

        public Exception Exception { get; }

        public DateTime OccurredAt { get; }
    }
}
