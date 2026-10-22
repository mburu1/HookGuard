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
