using HookGuard.Application.Deliveries;
using HookGuard.Domain.Deliveries;

namespace HookGuard.Application.Abstractions;

/// <summary>
/// The delivery rows: one claimed and finished by a worker, and the two the sweep moves along.
/// </summary>
public interface IDeliveryStore
{
    /// <summary>
    /// Takes ownership of a delivery for one attempt, counting that attempt on the way through.
    /// </summary>
    /// <returns>
    /// The delivery with everything the attempt needs, or <see langword="null"/> when there was
    /// nothing to claim — already being attempted, already finished, or never there. That null is
    /// what makes a duplicate queue message harmless: the decision is taken by the database rather
    /// than by a prior read.
    /// </returns>
    Task<ClaimedDelivery?> ClaimAsync(Guid deliveryId, CancellationToken cancellationToken);

    /// <summary>Persists the outcome the worker recorded on the delivery.</summary>
    Task RecordOutcomeAsync(Delivery delivery, CancellationToken cancellationToken);

    /// <summary>
    /// Hands back the deliveries that were claimed before <paramref name="claimedBefore"/> and never
    /// finished, which is what a worker that died mid-attempt leaves behind. They are made due again
    /// rather than abandoned, and the attempt they were holding stays counted.
    /// </summary>
    /// <returns>How many rows were handed back.</returns>
    Task<int> ReclaimStuckAsync(DateTimeOffset claimedBefore, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the deliveries whose next attempt has come due, oldest first, so the sweep re-announces
    /// the ones that have been waiting longest.
    /// </summary>
    /// <returns>The ids alone: re-announcing a delivery names it and nothing more.</returns>
    Task<IReadOnlyList<Guid>> ListDueAsync(
        DateTimeOffset due,
        int batchSize,
        CancellationToken cancellationToken);
}
