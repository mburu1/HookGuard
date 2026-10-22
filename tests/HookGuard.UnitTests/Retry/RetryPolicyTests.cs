using FluentAssertions;
using HookGuard.Domain.Retry;
using HookGuard.UnitTests.Fakes;

namespace HookGuard.UnitTests.Retry;

/// <summary>
/// M2.4. The backoff is a promise about how hard HookGuard leans on a destination that is already in
/// trouble, so its bounds are asserted exactly: the window doubles, stops at the cap, and the delay
/// taken from it never falls outside it.
/// </summary>
public class RetryPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private readonly FakeClock _clock = new(Now);

    [Theory]
    [InlineData(1, 1_000)]
    [InlineData(2, 2_000)]
    [InlineData(3, 4_000)]
    [InlineData(8, 128_000)]
    public void Doubles_the_window_with_each_attempt(int completedAttempts, int expectedMilliseconds)
    {
        Policy().WindowFor(completedAttempts).Should().Be(TimeSpan.FromMilliseconds(expectedMilliseconds));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(100)]
    public void Stops_growing_the_window_at_the_cap(int completedAttempts)
    {
        // The eleventh attempt is the first whose doubling would pass the cap, and every later one
        // stays there: a destination that has been failing for hours is not retried hours apart.
        Policy().WindowFor(completedAttempts).Should().Be(RetryPolicy.MaximumDelay);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(999, 999)]
    [InlineData(1_000, 1_000)]
    public void Takes_the_delay_from_anywhere_inside_the_window(int draw, int expectedMilliseconds)
    {
        // The first attempt's window is one second wide, and the delay may be any part of it -- the
        // bottom and the top included. That is what "full" jitter means, and it is what stops a fleet
        // of failures from coming back at the same moment.
        new RetryPolicy(new FakeRandom(draw), _clock)
            .DelayFor(completedAttempts: 1)
            .Should()
            .Be(TimeSpan.FromMilliseconds(expectedMilliseconds));
    }

    [Fact]
    public void Asks_for_a_draw_that_can_reach_the_top_of_the_window()
    {
        var random = new FakeRandom();
        var policy = new RetryPolicy(random, _clock);

        policy.DelayFor(completedAttempts: 3);

        // The bound is exclusive, so the top of the window needs it one millisecond higher: four
        // thousand and one possible delays, the longest of them a whole four seconds.
        random.LastMaxExclusive.Should().Be(4_001);
    }

    [Fact]
    public void Schedules_the_next_attempt_after_the_jittered_delay()
    {
        var policy = new RetryPolicy(new FakeRandom(1_500), _clock);

        // The second attempt's window is two seconds wide.
        policy.NextAttemptAt(completedAttempts: 2).Should().Be(Now.AddMilliseconds(1_500));
    }

    [Fact]
    public void Never_schedules_an_attempt_before_now()
    {
        // The window includes its own start, so a delay of zero is legitimate and means the delivery
        // is due at once rather than never.
        Policy().NextAttemptAt(completedAttempts: 1).Should().Be(Now);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_a_delay_before_any_attempt_has_been_made(int completedAttempts)
    {
        var act = () => Policy().DelayFor(completedAttempts);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private RetryPolicy Policy(int draw = 0) => new(new FakeRandom(draw), _clock);
}
