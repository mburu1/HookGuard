using HookGuard.Application.Abstractions;
using HookGuard.Domain.Destinations;
using HookGuard.Domain.Outbox;
using HookGuard.Domain.Time;

namespace HookGuard.Application.Deliveries;

/// <summary>
/// Keeps the queue in step with the delivery rows, which is what makes a retry happen at all: the
/// queue message that started an attempt is spent, so a delivery waiting for its next one is waiting
/// for something to announce it again.
/// </summary>
/// <remarks>
/// <para>
/// It announces a retry by publishing the message ingest would have written, without recording it in
/// the outbox. That is not the dual write the outbox exists to prevent: there is no transaction to
/// make atomic here, because the delivery row is already durable and is what the sweep reads on every
/// pass. A publish that is lost costs the retry one sweep, and a publish that arrives twice is
/// harmless — the worker's claim is what decides, as it does for every other redelivery.
/// </para>
/// <para>
/// The same pass hands back deliveries whose attempt was claimed so long ago that the worker holding
/// them cannot still be working. That is the only reason a crashed worker does not strand a delivery:
/// no worker holds state, so nobody else knows the attempt is over.
/// </para>
/// </remarks>
public sealed class DeliveryScheduler(IDeliveryStore store, IDispatcher dispatcher, IClock clock)
{
    /// <summary>
    /// How long an attempt may go unaccounted for before it is assumed lost. Comfortably longer than
    /// the longest an attempt is allowed to take, so a destination that is merely slow is never
    /// mistaken for a worker that died — taking the row back mid-attempt would send the payload twice.
    /// </summary>
    public static readonly TimeSpan VisibilityTimeout = Destination.MaximumTimeout * 2;

    private readonly IDeliveryStore _store = store ?? throw new ArgumentNullException(nameof(store));

    private readonly IDispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <param name="batchSize">
    /// Most retries to announce in one pass, so a backlog is worked through in bounded pieces rather
    /// than one long publish.
    /// </param>
    public async Task<SweepResult> SweepAsync(int batchSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var now = _clock.UtcNow;

        // Recovered rows become due with this pass, so the announcement below picks them up in the
        // same pass rather than a tick later.
        var recovered = await _store
            .ReclaimStuckAsync(now - VisibilityTimeout, cancellationToken)
            .ConfigureAwait(false);

        var due = await _store.ListDueAsync(now, batchSize, cancellationToken).ConfigureAwait(false);

        foreach (var deliveryId in due)
        {
            await _dispatcher
                .DispatchAsync(OutboxMessage.AnnounceDelivery(Guid.CreateVersion7(), deliveryId, now), cancellationToken)
                .ConfigureAwait(false);
        }

        return new SweepResult(recovered, due.Count);
    }
}
