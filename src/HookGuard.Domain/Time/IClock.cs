namespace HookGuard.Domain.Time;

/// <summary>
/// Abstracts the current instant so that timestamp tolerance, backoff schedules and
/// visibility-timeout recovery are deterministic in tests.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
