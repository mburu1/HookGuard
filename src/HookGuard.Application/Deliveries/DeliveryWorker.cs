using HookGuard.Application.Abstractions;
using HookGuard.Domain.Retry;

namespace HookGuard.Application.Deliveries;

/// <summary>
/// Turns a queued nudge into one real attempt: claim the delivery, send the payload, record what came
/// back. Everything about retrying hangs off the row this updates rather than off the message, which
/// is why a message that arrives twice cannot produce two attempts.
/// </summary>
public sealed class DeliveryWorker(IDeliveryStore store, IDeliverySender sender, RetryPolicy retry)
{
    /// <summary>
    /// Stands in for a failure that arrived without a reason, so the delivery row always carries one.
    /// </summary>
    private const string UnexplainedFailure = "The attempt failed without a reason.";

    private readonly IDeliveryStore _store = store ?? throw new ArgumentNullException(nameof(store));

    private readonly IDeliverySender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    private readonly RetryPolicy _retry = retry ?? throw new ArgumentNullException(nameof(retry));

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
            var error = result.Error ?? UnexplainedFailure;

            // The budget is the destination's and the wait is the retry policy's, so the worker is
            // where the two meet: it either puts the delivery back in line for when the policy says,
            // or parks it once the last attempt the destination allows has been spent. The wait is
            // what gives a struggling destination room to recover and keeps a backlog of failures
            // from arriving at it all at once.
            if (claimed.Delivery.HasAttemptsLeft(claimed.Destination.MaxAttempts))
            {
                claimed.Delivery.Fail(error, _retry.NextAttemptAt(claimed.Delivery.AttemptCount));
            }
            else
            {
                claimed.Delivery.DeadLetter(error);
            }
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
