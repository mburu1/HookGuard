namespace HookGuard.Domain.Deliveries;

/// <summary>
/// Where a delivery is in its lifecycle. Persisted as the name, so the partial index that finds due
/// work reads the same words as this enumeration.
/// </summary>
public enum DeliveryStatus
{
    /// <summary>Never attempted, or waiting for its next attempt to come due.</summary>
    Pending,

    /// <summary>Claimed by a worker, with an attempt in flight.</summary>
    InFlight,

    /// <summary>The destination answered <c>2xx</c>.</summary>
    Succeeded,

    /// <summary>Stopped after a failure that will not be retried, without exhausting the attempt budget.</summary>
    Failed,

    /// <summary>Exhausted <c>max_attempts</c>; parked for inspection and replay.</summary>
    DeadLettered,
}
