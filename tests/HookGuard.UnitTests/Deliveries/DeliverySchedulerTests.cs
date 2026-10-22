using FluentAssertions;
using HookGuard.Application.Abstractions;
using HookGuard.Application.Deliveries;
using HookGuard.Domain.Destinations;
using HookGuard.Domain.Outbox;
using HookGuard.UnitTests.Fakes;

namespace HookGuard.UnitTests.Deliveries;

/// <summary>
/// M2.5. The sweep is what makes a retry happen at all once a queue message has been spent, and what
/// stops a delivery from being stranded by a worker that never came back. Both are asserted against
/// exact instants, because "roughly a few minutes" is not a rule anybody can reason about.
/// </summary>
public class DeliverySchedulerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private readonly FakeClock _clock = new(Now);

    [Fact]
    public async Task Announces_every_delivery_whose_retry_has_come_due()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var store = new FakeDeliveryStore();
        var dispatcher = new FakeDispatcher();
        store.Due.AddRange([first, second]);

        var swept = await Scheduler(store, dispatcher).SweepAsync(batchSize: 100, CancellationToken.None);

        swept.Announced.Should().Be(2);
        dispatcher.Dispatched.Should().HaveCount(2);

        // The message names the delivery and nothing else, which is what the worker reads back: a
        // redelivered message must not be able to carry stale content to a destination.
        dispatcher.Dispatched
            .Select(message => DeliveryDispatch.FromPayload(message.Payload).DeliveryId)
            .Should()
            .Equal(first, second);

        store.DueCutoffs.Should().ContainSingle().Which.Should().Be(Now, "due is decided by the injected clock");
    }

    [Fact]
    public async Task Announces_nothing_when_no_retry_is_due()
    {
        var store = new FakeDeliveryStore();
        var dispatcher = new FakeDispatcher();

        var swept = await Scheduler(store, dispatcher).SweepAsync(batchSize: 100, CancellationToken.None);

        swept.Announced.Should().Be(0);
        dispatcher.Dispatched.Should().BeEmpty("a sweep with nothing to do must not put empty work on the queue");
    }

    [Fact]
    public async Task Hands_back_the_attempts_that_outlived_their_worker()
    {
        var store = new FakeDeliveryStore { Stuck = 3 };
        var dispatcher = new FakeDispatcher();

        var swept = await Scheduler(store, dispatcher).SweepAsync(batchSize: 100, CancellationToken.None);

        swept.Recovered.Should().Be(3);
        store.ReclaimCutoffs.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                Now - DeliveryScheduler.VisibilityTimeout,
                "an attempt is only abandoned once it has been unaccounted for longer than the window");
    }

    [Fact]
    public void Holds_a_visibility_window_wider_than_the_longest_attempt()
    {
        // A destination is allowed to take its whole configured timeout to answer. Reclaiming inside
        // that would take the row off a worker that is still waiting and send the payload twice, so the
        // window has to clear it.
        DeliveryScheduler.VisibilityTimeout.Should().BeGreaterThan(Destination.MaximumTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Rejects_a_sweep_that_was_asked_for_no_work(int batchSize)
    {
        var act = async () => await Scheduler(new FakeDeliveryStore(), new FakeDispatcher())
            .SweepAsync(batchSize, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private DeliveryScheduler Scheduler(IDeliveryStore store, IDispatcher dispatcher) =>
        new(store, dispatcher, _clock);
}
