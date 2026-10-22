using HookGuard.Application.Abstractions;
using HookGuard.Domain.Time;

namespace HookGuard.Application.Deliveries;

/// <summary>
/// Turns a queued nudge into one real attempt: claim the delivery, send the payload, record what came
/// back. Everything about retrying hangs off the row this updates rather than off the message, which
/// is why a message that arrives twice cannot produce two attempts.
/// </summary>
public sealed class DeliveryWorker(IDeliveryStore store, IDeliverySender sender, IClock clock)
{
    /// <summary>
    /// Stands in for a failure that arrived without a reason, so the delivery row always carries one.
    /// </summary>
    private const string UnexplainedFailure = "The attempt failed without a reason.";

    private readonly IDeliveryStore _store = store ?? throw new ArgumentNullException(nameof(store));

    private readonly IDeliverySender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>
    /// <see langword="false"/> when there was no delivery to claim, which is what a duplicate or stale
    /// message looks like from here.
    /// </returns>
    public async Task<bool> ProcessAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        var claimed = await _store.ClaimAsync(deliveryId, cancellationToken).ConfigureAwait(false);

        if (claimed is null)
        {
            return false;
        }

        var result = await _sender.SendAsync(Request(claimed), cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            claimed.Delivery.Succeed();
        }
        else
        {
            // Due immediately, for now: what the delay should actually be is the retry policy's
            // decision (M2.4), and nothing re-enqueues a Pending delivery until the scheduler sweep
            // exists (M2.5), so this cannot turn into a spin.
            claimed.Delivery.Fail(result.Error ?? UnexplainedFailure, _clock.UtcNow);
        }

        await _store.RecordOutcomeAsync(claimed.Delivery, cancellationToken).ConfigureAwait(false);

        return true;
    }

    private static DeliveryRequest Request(ClaimedDelivery claimed) => new(
        // The delivery id is the identity of the intent, so every retry of it presents the same key
        // and the destination can recognise the duplicates that at-least-once delivery allows. A
        // replay is a new delivery, and therefore correctly a new key.
        claimed.Delivery.Id.ToString(),
        claimed.Destination.Url,
        claimed.Destination.SigningSecret,
        claimed.Destination.Timeout,
        claimed.Event.Payload);
}
