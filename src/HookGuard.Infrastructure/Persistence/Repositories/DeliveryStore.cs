using HookGuard.Application.Abstractions;
using HookGuard.Application.Deliveries;
using HookGuard.Domain.Deliveries;
using Microsoft.EntityFrameworkCore;

namespace HookGuard.Infrastructure.Persistence.Repositories;

public sealed class DeliveryStore(HookGuardDbContext context) : IDeliveryStore
{
    private readonly HookGuardDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>
    /// One guarded <c>UPDATE</c> does the claiming, so the database decides who wins: two workers
    /// handed the same message race here and exactly one of them changes a row.
    /// </summary>
    /// <remarks>
    /// The attempt is counted in the same statement that claims it, because claiming is the moment the
    /// attempt begins. A worker that dies mid-attempt therefore still leaves the attempt counted,
    /// which is what stops a crash loop from retrying for ever without spending its budget — and it is
    /// the visibility timeout, not this, that hands the row back for another try.
    /// </remarks>
    public async Task<ClaimedDelivery?> ClaimAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        var claimed = await _context.Deliveries
            .Where(delivery => delivery.Id == deliveryId && delivery.Status == DeliveryStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(delivery => delivery.Status, DeliveryStatus.InFlight)
                    .SetProperty(delivery => delivery.AttemptCount, delivery => delivery.AttemptCount + 1),
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
}
