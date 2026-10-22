using HookGuard.Application.Abstractions;
using HookGuard.Domain.Time;

namespace HookGuard.Application.Outbox;

/// <summary>
/// Moves recorded messages from the database to the broker. The database stays the source of truth
/// until the broker has confirmed receipt, which is the whole point of the outbox: a crash between
/// the two duplicates a message at worst, and loses nothing.
/// </summary>
public sealed class OutboxRelay(IOutboxStore store, IDispatcher dispatcher, IClock clock)
{
    private readonly IOutboxStore _store = store ?? throw new ArgumentNullException(nameof(store));

    private readonly IDispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>
    /// Publishes at most one batch, oldest first, and returns how many messages the broker confirmed.
    /// </summary>
    /// <remarks>
    /// Confirmations are written once per batch, after the last publish. If the broker fails partway
    /// through, the batch is published again from the start on the next sweep: a message may reach
    /// the queue twice, and the delivery worker's atomic claim is what makes the second copy
    /// harmless.
    /// </remarks>
    public async Task<int> PublishBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var unpublished = await _store
            .ReadUnpublishedAsync(batchSize, cancellationToken)
            .ConfigureAwait(false);

        if (unpublished.Count == 0)
        {
            return 0;
        }

        foreach (var message in unpublished)
        {
            // Awaited: the broker's confirm is the evidence that this message is no longer
            // HookGuard's responsibility, and it is the only thing that authorises the write below.
            await _dispatcher.DispatchAsync(message, cancellationToken).ConfigureAwait(false);
            message.MarkPublished(_clock.UtcNow);
        }

        await _store.MarkPublishedAsync(unpublished, cancellationToken).ConfigureAwait(false);

        return unpublished.Count;
    }
}
