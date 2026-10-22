using RabbitMQ.Client;

namespace HookGuard.Infrastructure.Messaging;

/// <summary>
/// The shape of the broker, named in one place because the relay publishes to it and the worker
/// consumes from it. Phase 2 keeps it to a single durable work queue; the dead-letter exchange
/// arrives with M2.6.
/// </summary>
public static class MessagingTopology
{
    /// <summary>Work queue holding deliveries that still have to be attempted.</summary>
    public const string DeliveryDispatchQueue = "hookguard.delivery.dispatch";

    /// <summary>
    /// How many deliveries one worker may hold unacknowledged. This is the ceiling that keeps a slow
    /// destination from parking the whole backlog in a single worker: once it is reached the broker
    /// hands the next message to a different consumer instead of waiting.
    /// </summary>
    public const ushort PrefetchCount = 16;

    /// <summary>
    /// Declares the work queue. Both sides declare it so neither has to be started first, and they do
    /// it here so the arguments cannot drift apart — the broker rejects a redeclaration that does not
    /// match, which is louder than quietly getting a differently shaped queue.
    /// </summary>
    public static Task<QueueDeclareOk> DeclareDeliveryQueueAsync(
        IChannel channel,
        bool passive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        return channel.QueueDeclareAsync(
            queue: DeliveryDispatchQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            passive: passive,
            noWait: false,
            cancellationToken: cancellationToken);
    }
}
