using HookGuard.Domain.Time;

namespace HookGuard.UnitTests.Fakes;

/// <summary>Clock under test control, so tolerance and backoff assertions are exact.</summary>
internal sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public static FakeClock At(long unixSeconds) => new(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));

    public void Advance(TimeSpan by) => UtcNow += by;
}
