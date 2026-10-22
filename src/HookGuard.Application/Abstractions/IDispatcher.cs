using HookGuard.Domain.Outbox;

namespace HookGuard.Application.Abstractions;

/// <summary>
/// Hands a recorded outbox message to the message broker. The only place the application layer is
/// allowed to think about a broker at all, and it is the outer layer that decides which one.
/// </summary>
public interface IDispatcher
{
    /// <summary>
    /// Publishes the message and completes only once the broker has confirmed it is on the queue.
    /// Completing means "the queue owns this now"; throwing means "HookGuard still owns it", and the
    /// message is published again later.
    /// </summary>
    Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken);
}
