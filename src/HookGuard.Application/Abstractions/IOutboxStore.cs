using HookGuard.Domain.Outbox;

namespace HookGuard.Application.Abstractions;

/// <summary>
/// The read side of the transactional outbox: what the relay has yet to publish, and where it
/// records what the broker has already taken.
/// </summary>
/// <remarks>
/// A message returned by <see cref="ReadUnpublishedAsync"/> is marked through the same store
/// instance that read it, so the confirmation is written back to the row it came from.
/// </remarks>
public interface IOutboxStore
{
    /// <summary>
    /// Reads the oldest unpublished messages up to <paramref name="batchSize"/>, oldest first, so a
    /// backlog drains in the order it was written.
    /// </summary>
    Task<IReadOnlyList<OutboxMessage>> ReadUnpublishedAsync(int batchSize, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the publish confirmations recorded on the given messages. A confirmation the broker
    /// never gave is never written, so an unpublished message stays in the next batch.
    /// </summary>
    Task MarkPublishedAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken cancellationToken);
}
