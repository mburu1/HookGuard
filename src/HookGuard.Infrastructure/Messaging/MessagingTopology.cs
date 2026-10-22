using RabbitMQ.Client;

namespace HookGuard.Infrastructure.Messaging;

/// <summary>
/// The shape of the broker, named in one place because the relay publishes to it and the worker
/// consumes from it. Both sides declare it before use so neither has to be started first, and they
/// do it here so the arguments cannot drift apart — the broker rejects a redeclaration that does not
/// match, which is louder than quietly getting a differently shaped queue.
/// </summary>
public static class MessagingTopology
{
    /// <summary>Work queue holding deliveries that still have to be attempted.</summary>
    public const string DeliveryDispatchQueue = "hookguard.delivery.dispatch";

    /// <summary>
    /// Where the work queue sends a message its worker refused. Without it a message the handler
    /// threw on would leave the broker with nothing to show for it, and a failure that nobody can
    /// look at is the silent loss this project exists to rule out.
    /// </summary>
    public const string DeadLetterExchange = "hookguard.dlx";

    /// <summary>Holds what the work queue dead-lettered, until somebody looks at it.</summary>
    public const string DeadLetterQueue = "hookguard.delivery.dispatch.dead";

    /// <summary>
    /// How many deliveries one worker may hold unacknowledged. This is the ceiling that keeps a slow
    /// destination from parking the whole backlog in a single worker: once it is reached the broker
    /// hands the next message to a different consumer instead of waiting.
    /// </summary>
    public const ushort PrefetchCount = 16;

    /// <summary>
    /// Declares the work queue together with the dead-letter route it names in its own arguments:
    /// the exchange first, then the queue behind it, then the binding between them. The order
    /// matters — the broker refuses a queue that points at an exchange it has never heard of.
    /// </summary>
    /// <remarks>
    /// Declaring all of it every time is deliberate rather than wasteful: the arguments of a queue
    /// are fixed when it is created, so this is the only moment a difference between what the code
    /// expects and what the broker holds can be reported.
    /// </remarks>
    public static async Task<QueueDeclareOk> DeclareDeliveryQueueAsync(
        IChannel channel,
        bool passive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        await channel.ExchangeDeclareAsync(
            exchange: DeadLetterExchange,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            arguments: null,
            passive: passive,
            noWait: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await channel.QueueDeclareAsync(
            queue: DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            passive: passive,
            noWait: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await channel.QueueBindAsync(
            queue: DeadLetterQueue,
            exchange: DeadLetterExchange,
            routingKey: string.Empty,
            arguments: null,
            noWait: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return await channel.QueueDeclareAsync(
            queue: DeliveryDispatchQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: DeadLetterArguments,
            passive: passive,
            noWait: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// What makes the work queue dead-letter rather than drop. Set here rather than as a broker policy
    /// because the topology belongs to the code that uses it: the worker's nack and the queue's
    /// argument have to agree, and a policy configured elsewhere could not be held to that.
    /// </summary>
    private static Dictionary<string, object?> DeadLetterArguments => new()
    {
        ["x-dead-letter-exchange"] = DeadLetterExchange,
    };
}
