using HookGuard.Application.Abstractions;
using HookGuard.Application.Deliveries;
using HookGuard.Domain.Deliveries;

namespace HookGuard.UnitTests.Fakes;

/// <summary>
/// The delivery rows as an in-memory double, keeping the guarantees the real store gets from one
/// guarded <c>UPDATE</c>: a row is only ever claimed while it is <c>Pending</c> and while nobody
/// already holds it, and the sweep only ever sees rows the store is told are due or stuck. The
/// claimed objects are shared with the test, so an outcome the worker wrote back is visible on the
/// same instance the test holds.
/// </summary>
internal sealed class FakeDeliveryStore(ClaimedDelivery? claimable = null) : IDeliveryStore
{
    private readonly HashSet<Guid> _inFlight = [];

    /// <summary>What the store can hand out, or <see langword="null"/> for a store with nothing to claim.</summary>
    public ClaimedDelivery? Claimable { get; set; } = claimable;

    /// <summary>Every id a claim was attempted for, in order.</summary>
    public List<Guid> Claimed { get; } = [];

    /// <summary>Every outcome the worker recorded, in order.</summary>
    public List<Delivery> Recorded { get; } = [];

    /// <summary>The rows the sweep will find due, in the order it will get them.</summary>
    public List<Guid> Due { get; } = [];

    /// <summary>How many stuck rows the next reclaim reports, standing in for what the database updated.</summary>
    public int Stuck { get; set; }

    /// <summary>Every cutoff the sweep asked for, so the visibility window it applied can be asserted.</summary>
    public List<DateTimeOffset> ReclaimCutoffs { get; } = [];

    /// <summary>Every instant the sweep considered "now", so its clock can be checked against the test's.</summary>
    public List<DateTimeOffset> DueCutoffs { get; } = [];

    public Task<ClaimedDelivery?> ClaimAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        Claimed.Add(deliveryId);

        var claimed = Claimable;

        if (claimed is null
            || claimed.Delivery.Id != deliveryId
            || claimed.Delivery.Status != DeliveryStatus.Pending
            || !_inFlight.Add(deliveryId))
        {
            return Task.FromResult<ClaimedDelivery?>(null);
        }

        // The real store counts the attempt and stamps the claim in the same statement that claims the
        // row, so the delivery handed back already carries both.
        claimed.Delivery.BeginAttempt(DateTimeOffset.UtcNow);

        return Task.FromResult<ClaimedDelivery?>(claimed);
    }

    /// <summary>
    /// Writing the outcome is what releases the row, so a later claim is decided by the status the
    /// attempt left behind: a retry is claimable again, a success is not.
    /// </summary>
    public Task RecordOutcomeAsync(Delivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        _inFlight.Remove(delivery.Id);
        Recorded.Add(delivery);

        return Task.CompletedTask;
    }

    public Task<int> ReclaimStuckAsync(DateTimeOffset claimedBefore, CancellationToken cancellationToken)
    {
        ReclaimCutoffs.Add(claimedBefore);

        return Task.FromResult(Stuck);
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(
        DateTimeOffset due,
        int batchSize,
        CancellationToken cancellationToken)
    {
        DueCutoffs.Add(due);

        return Task.FromResult<IReadOnlyList<Guid>>([.. Due.Take(batchSize)]);
    }
}
