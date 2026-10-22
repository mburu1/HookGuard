using FluentAssertions;
using HookGuard.Application.Outbox;
using HookGuard.Domain.Outbox;
using HookGuard.UnitTests.Fakes;

namespace HookGuard.UnitTests.Outbox;

/// <summary>
/// The relay's contract: a message is only marked published once the broker has confirmed it, and a
/// message that was never confirmed stays in the next batch.
/// </summary>
public class OutboxRelayTests
{
    private const long Now = 1_800_000_000;

    private readonly FakeClock _clock = FakeClock.At(Now);

    [Fact]
    public async Task Publishes_every_unpublished_message_and_marks_it_published()
    {
        var store = new FakeOutboxStore(Message(Now), Message(Now + 1), Message(Now + 2));
        var dispatcher = new FakeDispatcher();
        var relay = Relay(store, dispatcher);

        var published = await relay.PublishBatchAsync(batchSize: 10, CancellationToken.None);

        published.Should().Be(3);
        dispatcher.Dispatched.Should().HaveCount(3);
        store.Messages.Should().OnlyContain(message => message.PublishedAt == _clock.UtcNow);

        // One write per batch: publishing is not a database round trip per message.
        store.SavedBatches.Should().ContainSingle();
        store.SavedBatches[0].Should().HaveCount(3);
    }

    [Fact]
    public async Task Publishes_in_the_order_the_messages_were_written()
    {
        // Given out of order, because the backlog's order is a property of the query, not of the
        // order the rows happen to be handed over in.
        var newest = Message(Now + 2);
        var oldest = Message(Now);
        var middle = Message(Now + 1);

        var dispatcher = new FakeDispatcher();
        var relay = Relay(new FakeOutboxStore(newest, oldest, middle), dispatcher);

        await relay.PublishBatchAsync(batchSize: 10, CancellationToken.None);

        dispatcher.Dispatched.Should().ContainInOrder(oldest, middle, newest);
    }

    [Fact]
    public async Task Publishes_nothing_when_the_outbox_has_nothing_unpublished()
    {
        var store = new FakeOutboxStore();
        var dispatcher = new FakeDispatcher();

        var published = await Relay(store, dispatcher).PublishBatchAsync(10, CancellationToken.None);

        published.Should().Be(0);
        dispatcher.Dispatched.Should().BeEmpty();
        store.SavedBatches.Should().BeEmpty("there is no confirmation to write");
    }

    [Fact]
    public async Task Publishes_nothing_twice()
    {
        var store = new FakeOutboxStore(Message(Now), Message(Now + 1));
        var dispatcher = new FakeDispatcher();
        var relay = Relay(store, dispatcher);

        var first = await relay.PublishBatchAsync(batchSize: 10, CancellationToken.None);
        var second = await relay.PublishBatchAsync(batchSize: 10, CancellationToken.None);

        first.Should().Be(2);
        second.Should().Be(0);
        dispatcher.Dispatched.Should().HaveCount(2);
    }

    [Fact]
    public async Task Stops_at_the_message_the_broker_did_not_confirm()
    {
        var first = Message(Now);
        var refused = Message(Now + 1);
        var last = Message(Now + 2);

        var dispatcher = new FakeDispatcher { Confirm = message => message.Id != refused.Id };
        var store = new FakeOutboxStore(first, refused, last);

        var publish = async () => await Relay(store, dispatcher).PublishBatchAsync(10, CancellationToken.None);

        await publish.Should().ThrowAsync<InvalidOperationException>();

        // Order is kept on purpose: the queue sees the backlog in the order it was written, so the
        // messages behind a failure wait for the next sweep rather than overtaking it.
        dispatcher.Dispatched.Should().ContainSingle().Which.Should().BeSameAs(first);
        store.SavedBatches.Should().BeEmpty("nothing in this batch was confirmed for the batch to record");
        refused.PublishedAt.Should().BeNull();
        last.PublishedAt.Should().BeNull("it was never published, so it must be published later");
    }

    [Fact]
    public async Task Refuses_a_batch_size_that_cannot_make_progress()
    {
        var relay = Relay(new FakeOutboxStore(Message(Now)), new FakeDispatcher());

        var publish = async () => await relay.PublishBatchAsync(batchSize: 0, CancellationToken.None);

        await publish.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private OutboxRelay Relay(FakeOutboxStore store, FakeDispatcher dispatcher) =>
        new(store, dispatcher, _clock);

    private static OutboxMessage Message(long createdAt) =>
        OutboxMessage.AnnounceDelivery(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DateTimeOffset.FromUnixTimeSeconds(createdAt));
}
