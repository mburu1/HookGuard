using HookGuard.Domain.Time;

namespace HookGuard.Domain.Retry;

/// <summary>
/// Decides when a delivery that just failed may be attempted again: an exponentially wider window,
/// jittered across its whole width. Pure arithmetic over an injected <see cref="IClock"/> and
/// <see cref="IRandom"/>, because how long to wait is a policy rather than something to discover by
/// waiting.
/// </summary>
public sealed class RetryPolicy(IRandom random, IClock clock)
{
    /// <summary>
    /// What the first retry waits before any jitter. Every later window doubles from here.
    /// </summary>
    public static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Where the windows stop doubling. A destination that has been failing for a long time should
    /// not be leaning further and further apart, or the schedule stops meaning anything.
    /// </summary>
    public static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(10);

    private readonly IRandom _random = random ?? throw new ArgumentNullException(nameof(random));

    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <param name="completedAttempts">
    /// How many attempts the delivery has used, the one that just failed included. At least one,
    /// because a delivery nothing has been tried against has nothing to back off from.
    /// </param>
    /// <returns>When that delivery may next be attempted. Never in the past.</returns>
    public DateTimeOffset NextAttemptAt(int completedAttempts) =>
        _clock.UtcNow + DelayFor(completedAttempts);

    /// <param name="completedAttempts">As <see cref="NextAttemptAt"/>.</param>
    /// <returns>The width of the window the delay is drawn from: <c>min(cap, base * 2^(attempts - 1))</c>.</returns>
    public TimeSpan WindowFor(int completedAttempts)
    {
        if (completedAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedAttempts),
                completedAttempts,
                "A delay only makes sense once an attempt has been made.");
        }

        // Doubling from the first retry, which waits the base delay: 2^0, 2^1, 2^2 ... Clamped before
        // it is narrowed to a whole number of milliseconds, so that a long-lived delivery cannot
        // overflow the cast.
        var window = BaseDelay.TotalMilliseconds * Math.Pow(2, completedAttempts - 1);

        return TimeSpan.FromMilliseconds(Math.Min(window, MaximumDelay.TotalMilliseconds));
    }

    /// <param name="completedAttempts">As <see cref="NextAttemptAt"/>.</param>
    /// <returns>
    /// A delay somewhere in <c>[0, window]</c>, the window included at both ends.
    /// </returns>
    /// <remarks>
    /// Full jitter rather than a fixed fraction of the window: a fleet of deliveries that failed
    /// together should come back spread across the whole window instead of clustering at a point
    /// inside it. The upper bound is exclusive, hence the extra millisecond.
    /// </remarks>
    public TimeSpan DelayFor(int completedAttempts) =>
        TimeSpan.FromMilliseconds(
            _random.Next((int)WindowFor(completedAttempts).TotalMilliseconds + 1));
}
