using HookGuard.Domain.Time;

namespace HookGuard.Infrastructure.Time;

/// <summary>
/// Production clock. Tests substitute a fake so tolerance windows and backoff schedules are
/// asserted against an exact instant instead of <see cref="DateTimeOffset.UtcNow"/>.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
