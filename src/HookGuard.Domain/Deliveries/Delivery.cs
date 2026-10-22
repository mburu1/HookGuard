namespace HookGuard.Domain.Deliveries;

/// <summary>
/// One event on its way to one destination. This row is the retry state machine: the outbox message
/// and the queue only ever name a delivery, so a redelivered message cannot invent an attempt
/// without going through the row.
/// </summary>
public sealed class Delivery
{
    /// <summary>
    /// Longest failure description the row can hold. A description longer than this is cut down to
    /// fit rather than refused, because a talkative destination must not be the reason an attempt
    /// goes unrecorded.
    /// </summary>
    public const int LastErrorMaxLength = 2000;

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

    /// <summary>
    /// Begins an attempt, which is the only way a delivery leaves <see cref="DeliveryStatus.Pending"/>.
    /// The attempt is counted here because claiming is the moment it begins: a worker that dies
    /// mid-attempt still leaves it counted, so a crash loop cannot retry for ever without spending
    /// its budget.
    /// </summary>
    /// <remarks>
    /// In production this transition is part of one guarded <c>UPDATE</c>, so that the database decides
    /// which of two racing workers owns the delivery; this method is that same transition where it is
    /// decided in memory instead.
    /// </remarks>
    public void BeginAttempt()
    {
        if (Status != DeliveryStatus.Pending)
        {
            throw new InvalidOperationException(
                $"A delivery that is {Status} cannot begin another attempt.");
        }

        Status = DeliveryStatus.InFlight;
        AttemptCount++;
    }

    /// <summary>
    /// Records that the destination accepted the payload. Nothing here counts the attempt: it was
    /// counted when the delivery was claimed, which is the moment an attempt began.
    /// </summary>
    public void Succeed()
    {
        Status = DeliveryStatus.Succeeded;
        LastError = null;
    }

    /// <summary>
    /// Records a failed attempt and puts the delivery back in line for another one.
    /// </summary>
    /// <param name="error">Why it failed, as the worker saw it.</param>
    /// <param name="nextAttemptAt">
    /// When the next attempt may start. Choosing that instant is the retry policy's business; the row
    /// only carries the answer.
    /// </param>
    public void Fail(string error, DateTimeOffset nextAttemptAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        Status = DeliveryStatus.Pending;
        LastError = error.Length > LastErrorMaxLength ? error[..LastErrorMaxLength] : error;
        NextAttemptAt = nextAttemptAt;
    }
}
