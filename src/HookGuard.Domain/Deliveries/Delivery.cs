namespace HookGuard.Domain.Deliveries;

/// <summary>
/// One event on its way to one destination. This row is the retry state machine: the outbox message
/// and the queue only ever name a delivery, so a redelivered message cannot invent an attempt
/// without going through the row.
/// </summary>
public sealed class Delivery
{
    private Delivery()
    {
        // Materialised by EF Core.
    }

    private Delivery(
        Guid id,
        Guid eventId,
        Guid destinationId,
        DeliveryStatus status,
        int attemptCount,
        DateTimeOffset nextAttemptAt,
        string? lastError)
    {
        Id = id;
        EventId = eventId;
        DestinationId = destinationId;
        Status = status;
        AttemptCount = attemptCount;
        NextAttemptAt = nextAttemptAt;
        LastError = lastError;
    }

    /// <param name="now">
    /// Due immediately, so a healthy destination is reached on the first sweep rather than after a
    /// backoff that has no failure to justify it.
    /// </param>
    public static Delivery Schedule(Guid id, Guid eventId, Guid destinationId, DateTimeOffset now)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Delivery id is required.", nameof(id));
        }

        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Event id is required.", nameof(eventId));
        }

        if (destinationId == Guid.Empty)
        {
            throw new ArgumentException("Destination id is required.", nameof(destinationId));
        }

        return new Delivery(id, eventId, destinationId, DeliveryStatus.Pending, 0, now, lastError: null);
    }

    public Guid Id { get; private set; }

    public Guid EventId { get; private set; }

    public Guid DestinationId { get; private set; }

    public DeliveryStatus Status { get; private set; } = DeliveryStatus.Pending;

    public int AttemptCount { get; private set; }

    /// <summary>When this delivery becomes eligible for another attempt.</summary>
    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>Why the last attempt failed, as the worker saw it. Null until one has failed.</summary>
    public string? LastError { get; private set; }
}
