namespace HookGuard.Domain.Outbox;

/// <summary>
/// A side effect recorded inside the transaction that caused it, and only later handed to the
/// broker. Writing to the database and the queue separately can diverge on a crash; committing this
/// row with the rows it describes makes the database the single source of truth for what must be
/// published.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
        // Materialised by EF Core.
    }

    private OutboxMessage(Guid id, string type, string payload, DateTimeOffset createdAt)
    {
        Id = id;
        Type = type;
        Payload = payload;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Announces that a delivery is ready to be attempted. Written in the same commit as the
    /// delivery itself, so a delivery that exists always has a message that will reach the queue.
    /// </summary>
    public static OutboxMessage AnnounceDelivery(Guid id, Guid deliveryId, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Outbox message id is required.", nameof(id));
        }

        if (deliveryId == Guid.Empty)
        {
            throw new ArgumentException("Delivery id is required.", nameof(deliveryId));
        }

        return new OutboxMessage(
            id,
            DeliveryDispatch.MessageType,
            DeliveryDispatch.For(deliveryId).ToPayload(),
            createdAt);
    }

    public Guid Id { get; private set; }

    /// <summary>Tells the relay and the worker how to read <see cref="Payload"/>.</summary>
    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Null until the broker confirmed the publish; the relay's only search criterion.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>
    /// Records the first confirmed publish. Publishing may happen more than once (the relay is
    /// at-least-once), but the timestamp is the moment the message first left HookGuard, which is
    /// what the outbox lag metric measures.
    /// </summary>
    public void MarkPublished(DateTimeOffset publishedAt)
    {
        PublishedAt ??= publishedAt;
    }
}
