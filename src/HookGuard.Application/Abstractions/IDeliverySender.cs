using HookGuard.Application.Deliveries;

namespace HookGuard.Application.Abstractions;

/// <summary>
/// Performs one attempt to hand a payload to a destination. What is sent, and what the answer means,
/// are the application's decisions; which protocol carries it is the outer layer's.
/// </summary>
public interface IDeliverySender
{
    /// <summary>
    /// Sends the payload and reports what the destination answered.
    /// </summary>
    /// <remarks>
    /// Only a caller that has asked for the operation to stop should see this throw. Anything the
    /// destination does — refusing, taking too long, not being there — is reported as a result, so
    /// that it can be written to the delivery row and retried.
    /// </remarks>
    Task<DeliveryResult> SendAsync(DeliveryRequest request, CancellationToken cancellationToken);
}
