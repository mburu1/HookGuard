namespace HookGuard.Infrastructure.Messaging;

/// <summary>
/// The shape of the broker, named in one place because the relay publishes to it and the worker
/// consumes from it. Phase 2 keeps it to a single durable work queue; prefetch, manual acknowledgements
/// and the dead-letter exchange arrive with the consumer.
/// </summary>
public static class MessagingTopology
{
    /// <summary>Work queue holding deliveries that still have to be attempted.</summary>
    public const string DeliveryDispatchQueue = "hookguard.delivery.dispatch";
}
