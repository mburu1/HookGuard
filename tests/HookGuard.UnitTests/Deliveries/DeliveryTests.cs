using FluentAssertions;
using HookGuard.Domain.Deliveries;

namespace HookGuard.UnitTests.Deliveries;

/// <summary>
/// The row's own transitions. This row is the retry state machine, so what it records, and what it
/// refuses to record, is what a later sweep acts on.
/// </summary>
public class DeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Starts_pending_with_no_attempts_and_no_failure()
    {
        var delivery = Schedule();

        delivery.Status.Should().Be(DeliveryStatus.Pending);
        delivery.AttemptCount.Should().Be(0);
        delivery.NextAttemptAt.Should().Be(Now, "a delivery with no failure behind it is due at once");
        delivery.LastError.Should().BeNull();
        delivery.ClaimedAt.Should().BeNull("nobody holds a delivery that has not been attempted");
    }

    [Fact]
    public void Dates_the_claim_so_a_lost_attempt_can_be_told_from_a_slow_one()
    {
        var delivery = Schedule();
        var claimedAt = Now.AddSeconds(7);

        delivery.BeginAttempt(claimedAt);

        delivery.ClaimedAt.Should().Be(claimedAt, "the visibility timeout measures the attempt from this instant");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Releases_the_claim_when_the_attempt_is_recorded(bool succeeded)
    {
        var delivery = Schedule();
        delivery.BeginAttempt(Now);

        if (succeeded)
        {
            delivery.Succeed();
        }
        else
        {
            delivery.Fail("The destination answered HTTP 503.", Now.AddSeconds(30));
        }

        delivery.ClaimedAt.Should().BeNull("nothing is holding a delivery whose attempt has ended");
    }

    [Fact]
    public void Counts_an_attempt_the_moment_the_delivery_is_claimed()
    {
        var delivery = Schedule();

        delivery.BeginAttempt(Now);

        delivery.Status.Should().Be(DeliveryStatus.InFlight, "a claimed delivery is one somebody is working on");
        delivery.AttemptCount.Should().Be(1, "a worker that dies mid-attempt has still spent the attempt");
    }

    [Fact]
    public void Refuses_a_second_attempt_on_a_delivery_somebody_already_holds()
    {
        var delivery = Schedule();
        delivery.BeginAttempt(Now);

        var act = () => delivery.BeginAttempt(Now);

        act.Should().Throw<InvalidOperationException>("two workers must not both count themselves as the attempt");
    }

    [Fact]
    public void Records_acceptance_and_clears_whatever_failed_before_it()
    {
        var delivery = Schedule();
        delivery.Fail("The destination answered HTTP 503.", Now.AddSeconds(30));

        delivery.Succeed();

        delivery.Status.Should().Be(DeliveryStatus.Succeeded);
        delivery.LastError.Should().BeNull("a delivery that eventually got through has no failure left to report");
    }

    [Fact]
    public void Records_a_failure_and_puts_the_delivery_back_in_line()
    {
        var delivery = Schedule();
        var retryAt = Now.AddSeconds(45);

        delivery.Fail("The destination could not be reached.", retryAt);

        delivery.Status.Should().Be(DeliveryStatus.Pending, "a failed attempt is retried, not given up on");
        delivery.LastError.Should().Be("The destination could not be reached.");
        delivery.NextAttemptAt.Should().Be(retryAt);
    }

    [Fact]
    public void Cuts_a_failure_description_down_to_what_the_row_holds()
    {
        var delivery = Schedule();

        delivery.Fail(new string('x', Delivery.LastErrorMaxLength + 500), Now);

        // Truncated rather than refused: a talkative destination must not be the reason an attempt
        // goes unrecorded.
        delivery.LastError.Should().HaveLength(Delivery.LastErrorMaxLength);
    }

    [Fact]
    public void Knows_when_the_destination_has_attempts_left()
    {
        var delivery = Schedule();

        delivery.HasAttemptsLeft(maxAttempts: 2).Should().BeTrue("nothing has been attempted yet");

        delivery.BeginAttempt(Now);
        delivery.Fail("The destination answered HTTP 503.", Now);
        delivery.BeginAttempt(Now);

        // Two attempts allowed and two taken, so a third would be one the destination never agreed to
        // serve. The second attempt was counted when it was claimed, not when it ended.
        delivery.HasAttemptsLeft(maxAttempts: 2).Should().BeFalse();
        delivery.AttemptCount.Should().Be(2);
    }

    [Fact]
    public void Parks_a_delivery_whose_attempts_are_spent()
    {
        var delivery = Schedule();

        delivery.DeadLetter("The destination answered HTTP 503.");

        delivery.Status.Should().Be(
            DeliveryStatus.DeadLettered,
            "a spent attempt budget is a decision to stop rather than another wait");
        delivery.LastError.Should().Be("The destination answered HTTP 503.", "the row keeps why it gave up");
        delivery.ClaimedAt.Should().BeNull("a parked delivery is nobody's to work on");
    }

    [Fact]
    public void Leaves_the_instant_it_was_last_due_for_alone_when_it_is_parked()
    {
        var delivery = Schedule();
        var retryAt = Now.AddSeconds(30);
        delivery.Fail("The destination answered HTTP 503.", retryAt);

        delivery.DeadLetter("The destination answered HTTP 503.");

        // Nothing schedules a parked delivery, and where it stood when it was still being retried is
        // part of what happened to it.
        delivery.NextAttemptAt.Should().Be(retryAt);
    }

    [Fact]
    public void Rejects_parking_a_delivery_with_no_reason()
    {
        var delivery = Schedule();

        var act = () => delivery.DeadLetter("   ");

        act.Should().Throw<ArgumentException>("a parked delivery still has to say why it was given up on");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_failure_with_no_reason(string error)
    {
        var delivery = Schedule();

        var act = () => delivery.Fail(error, Now);

        act.Should().Throw<ArgumentException>();
    }

    private static Delivery Schedule() =>
        Delivery.Schedule(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Now);
}
