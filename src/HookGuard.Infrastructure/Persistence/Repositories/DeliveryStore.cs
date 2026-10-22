using HookGuard.Application.Abstractions;
using HookGuard.Application.Deliveries;
using HookGuard.Domain.Deliveries;
using HookGuard.Domain.Time;
using Microsoft.EntityFrameworkCore;

namespace HookGuard.Infrastructure.Persistence.Repositories;

public sealed class DeliveryStore(HookGuardDbContext context, IClock clock) : IDeliveryStore
{
    private readonly HookGuardDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>
    /// One guarded <c>UPDATE</c> does the claiming, so the database decides who wins: two workers
    /// handed the same message race here and exactly one of them changes a row.
    /// </summary>
    /// <remarks>
    /// The attempt is counted and the claim is timestamped in the same statement that takes the row,
    /// because claiming is the moment the attempt begins. A worker that dies mid-attempt therefore
    /// still leaves both behind, which is what stops a crash loop from retrying for ever without
    /// spending its budget, and what lets the sweep tell the row apart from one that is being worked on
    /// right now.
    /// </remarks>
    public async Task<ClaimedDelivery?> ClaimAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        var claimed = await _context.Deliveries
            .Where(delivery => delivery.Id == deliveryId && delivery.Status == DeliveryStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(delivery => delivery.Status, DeliveryStatus.InFlight)
                    .SetProperty(delivery => delivery.AttemptCount, delivery => delivery.AttemptCount + 1)
                    .SetProperty(delivery => delivery.ClaimedAt, _clock.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);

        if (claimed == 0)
        {
            return null;
        }

        // Read back after the update rather than before it: the row the attempt works from is the
        // claimed one, so the attempt count it carries is already this attempt's.
        var row = await (from delivery in _context.Deliveries
                         join @event in _context.Events on delivery.EventId equals @event.Id
                         join destination in _context.Destinations
                             on delivery.DestinationId equals destination.Id
                         where delivery.Id == deliveryId
                         select new
                         {
                             Delivery = delivery,
                             Event = @event,
                             Destination = destination,
                         })
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : new ClaimedDelivery(row.Delivery, row.Event, row.Destination);
    }

    /// <summary>
    /// The delivery was read without tracking, so it is attached as modified. That is deliberate: the
    /// row is small, every column on it has just been read, and writing them all back is easier to
    /// reason about than a per-property diff that has to be kept in step with the domain.
    /// </summary>
    public async Task RecordOutcomeAsync(Delivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        _context.Deliveries.Update(delivery);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One guarded <c>UPDATE</c> again, and for the same reason: the row is only handed back while it
    /// is still <c>InFlight</c> and still carries an old claim, so a worker that finished in the
    /// meantime is never raced by the sweep and the delivery is never attempted twice at once.
    /// </summary>
    /// <remarks>
    /// The attempt it was holding stays counted, so a delivery that keeps killing its worker still
    /// runs out of attempts instead of being retried for ever. Being due at once is the point of the
    /// new <c>next_attempt_at</c>: the wait that was scheduled belonged to an attempt nobody saw the
    /// end of.
    /// </remarks>
    public async Task<int> ReclaimStuckAsync(DateTimeOffset claimedBefore, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        return await _context.Deliveries
            .Where(delivery =>
                delivery.Status == DeliveryStatus.InFlight
                && delivery.ClaimedAt < claimedBefore)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(delivery => delivery.Status, DeliveryStatus.Pending)
                    .SetProperty(delivery => delivery.NextAttemptAt, now)
                    .SetProperty(delivery => delivery.ClaimedAt, (DateTimeOffset?)null),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads off <c>ix_deliveries_due</c>, which covers only <c>Pending</c> rows: those are the only
    /// ones that can be waiting for a time to pass, so the scan stays in proportion to the backlog
    /// rather than to the history. The id breaks ties between deliveries that came due in the same
    /// instant, keeping the order stable from one sweep to the next.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ListDueAsync(
        DateTimeOffset due,
        int batchSize,
        CancellationToken cancellationToken) =>
        await _context.Deliveries
            .Where(delivery => delivery.Status == DeliveryStatus.Pending && delivery.NextAttemptAt <= due)
            .OrderBy(delivery => delivery.NextAttemptAt)
            .ThenBy(delivery => delivery.Id)
            .Take(batchSize)
            .Select(delivery => delivery.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
