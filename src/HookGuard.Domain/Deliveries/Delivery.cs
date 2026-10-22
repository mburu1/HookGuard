namespace HookGuard.Domain.Deliveries;

/// <summary>
/// One event on its way to one destination. This row is the retry state machine: the outbox message
/// and the queue only ever name a delivery, so a redelivered message cannot invent an attempt
/// without going through the row.
/// </summary>
public sealed class Delivery
{
    /// <summary>
    /// Longest failure description the row can hold. A longer one is cut down rather than refused;
    /// see <see cref="Describe"/>.
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

    /// <summary>
    /// When the attempt in flight was claimed, or <see langword="null"/> while nothing is holding this
    /// row. It is the only evidence that a worker took the delivery and never came back, which is what
    /// the visibility timeout reads to hand the row over again.
    /// </summary>
    public DateTimeOffset? ClaimedAt { get; private set; }

    /// <summary>Why the last attempt failed, as the worker saw it. Null until one has failed.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Begins an attempt, which is the only way a delivery leaves <see cref="DeliveryStatus.Pending"/>.
    /// The attempt is counted here because claiming is the moment it begins: a worker that dies
    /// mid-attempt still leaves it counted, so a crash loop cannot retry for ever without spending
    /// its budget.
    /// </summary>
    /// <param name="at">When the attempt was claimed, for the visibility timeout to measure from.</param>
    /// <remarks>
    /// In production this transition is part of one guarded <c>UPDATE</c>, so that the database decides
    /// which of two racing workers owns the delivery; this method is that same transition where it is
    /// decided in memory instead.
    /// </remarks>
    public void BeginAttempt(DateTimeOffset at)
    {
        if (Status != DeliveryStatus.Pending)
        {
            throw new InvalidOperationException(
                $"A delivery that is {Status} cannot begin another attempt.");
        }

        Status = DeliveryStatus.InFlight;
        AttemptCount++;
        ClaimedAt = at;
    }

    /// <summary>
    /// Records that the destination accepted the payload. Nothing here counts the attempt: it was
    /// counted when the delivery was claimed, which is the moment an attempt began.
    /// </summary>
    public void Succeed()
    {
        Status = DeliveryStatus.Succeeded;
        ClaimedAt = null;
        LastError = null;
    }

    /// <summary>
    /// Whether the destination's attempt budget still allows another attempt.
    /// </summary>
    /// <param name="maxAttempts">How many attempts the destination allows in total.</param>
    /// <remarks>
    /// Attempts are counted when they are claimed rather than when they end, so the budget is spent
    /// the moment the last allowed attempt is taken — including one the worker never came back from.
    /// </remarks>
    public bool HasAttemptsLeft(int maxAttempts) => AttemptCount < maxAttempts;

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
        Status = DeliveryStatus.Pending;
        LastError = Describe(error);
        NextAttemptAt = nextAttemptAt;
        ClaimedAt = null;
    }

    /// <summary>
    /// Parks the delivery: the attempts its destination allows are spent, so it stays exactly as the
    /// last attempt left it and waits for somebody to inspect or replay it rather than being retried
    /// for ever or dropped without trace.
    /// </summary>
    /// <param name="error">Why the last attempt failed, as the worker saw it.</param>
    /// <remarks>
    /// <see cref="NextAttemptAt"/> is deliberately left where it was. Nothing schedules a parked
    /// delivery, and the instant it was last due for is part of what happened to it.
    /// </remarks>
    public void DeadLetter(string error)
    {
        Status = DeliveryStatus.DeadLettered;
        LastError = Describe(error);
        ClaimedAt = null;
    }

    /// <summary>
    /// The reason as the row can hold it. A description longer than the column is cut down to fit
    /// rather than refused, because a talkative destination must not be the reason an attempt goes
    /// unrecorded.
    /// </summary>
    private static string Describe(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        return error.Length > LastErrorMaxLength ? error[..LastErrorMaxLength] : error;
    }
}
