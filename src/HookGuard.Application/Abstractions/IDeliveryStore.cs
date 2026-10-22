using HookGuard.Application.Deliveries;
using HookGuard.Domain.Deliveries;

namespace HookGuard.Application.Abstractions;

/// <summary>
/// The delivery row as a worker sees it: one claim and one recorded outcome.
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
    /// than by a prior read, so two workers handed the same message cannot both send it.
    /// </returns>
    Task<ClaimedDelivery?> ClaimAsync(Guid deliveryId, CancellationToken cancellationToken);

    /// <summary>Persists the outcome the worker recorded on the delivery.</summary>
    Task RecordOutcomeAsync(Delivery delivery, CancellationToken cancellationToken);
}
