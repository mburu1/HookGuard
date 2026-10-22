using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using FluentAssertions;
using HookGuard.Domain.Outbox;
using HookGuard.Infrastructure.Messaging;
using RabbitMQ.Client;

namespace HookGuard.IntegrationTests.Messaging;

/// <summary>
/// M2.2. Prefetch and manual acknowledgements are promises about what a broker does with a message
/// HookGuard has not finished with, so an in-memory double can only agree with itself. These run
/// against a real queue: a worker holds no more than it prefetched, a delivery it is still working on
/// is not off the queue, and a delivery the handler refused does not come straight back.
/// </summary>
public sealed class RabbitMqConsumerTests(RabbitMqFixture broker) : IClassFixture<RabbitMqFixture>, IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly string _connectionString = broker.ConnectionString;

    public async Task InitializeAsync() => await CleanQueueAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Acknowledges_each_delivery_only_after_the_handler_returned()
    {
        var expected = await PublishAsync(count: 3);

        var handled = new ConcurrentQueue<Guid>();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await WithConsumerAsync(
            prefetchCount: 1,
            (message, _) =>
            {
                handled.Enqueue(Dispatched(message).DeliveryId);

                if (handled.Count == expected.Count)
                {
                    received.TrySetResult();
                }

                return Task.CompletedTask;
            },
            async _ =>
            {
                await received.Task.WaitAsync(Timeout);

                handled.Should().Equal(
                    expected,
                    "one delivery is in flight at a time, so the backlog is worked in the order it was written");

                // Each acknowledgement means the queue no longer holds that message, which is what
                // "handled" has to mean for the broker.
                await WaitForQueueDepthAsync(expected: 0);
            });
    }

    [Fact]
    public async Task Drops_a_delivery_whose_handler_threw_instead_of_requeueing_it()
    {
        await PublishAsync(count: 1);

        var handled = new int[1];
        var refused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await WithConsumerAsync(
            prefetchCount: 1,
            (_, _) =>
            {
                Interlocked.Increment(ref handled[0]);
                refused.TrySetResult();

                throw new InvalidOperationException("the destination rejected the payload");
            },
            async _ =>
            {
                await refused.Task.WaitAsync(Timeout);

                // Requeueing would put a message the handler cannot process straight back in front of
                // it. It is dropped instead, and the delivery row in the database is what still
                // schedules the retry.
                await WaitForQueueDepthAsync(expected: 0);
                Volatile.Read(ref handled[0]).Should().Be(1);
            });
    }

    [Fact]
    public async Task Holds_no_more_than_the_prefetch_count_deliveries_at_once()
    {
        const int Prefetch = 2;

        await PublishAsync(count: 5);

        var handled = new int[1];
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await WithConsumerAsync(
            Prefetch,
            async (_, cancellationToken) =>
            {
                if (Interlocked.Increment(ref handled[0]) == Prefetch)
                {
                    blocked.TrySetResult();
                }

                await release.Task.WaitAsync(cancellationToken);
            },
            async _ =>
            {
                await blocked.Task.WaitAsync(Timeout);

                // Both handlers are parked, so a third delivery arriving would mean the broker handed
                // this worker more than it said it would hold.
                await Task.Delay(TimeSpan.FromSeconds(2));

                Volatile.Read(ref handled[0]).Should().Be(
                    Prefetch,
                    "a worker may only hold what it prefetched, or one slow destination starves the others");

                // What is being attempted is still the broker's to redeliver, which is what stops a
                // worker that dies mid-flight from taking its work with it.
                (await QueueDepthAsync()).Should().Be(
                    3,
                    "deliveries that are being attempted are not off the queue yet");

                release.SetResult();

                await WaitForAsync(
                    () => Volatile.Read(ref handled[0]) == 5,
                    "the deliveries queued behind the blocked ones still arrive");

                await WaitForQueueDepthAsync(expected: 0);
            });
    }

    /// <summary>
    /// Runs the body against a consuming worker, and stops the worker whatever the body did. The
    /// cancellation matters even when an assertion failed: a consumer's handler is allowed to run
    /// until it observes the token, so a worker nobody stopped never lets go of its channel.
    /// </summary>
    private async Task WithConsumerAsync(
        ushort prefetchCount,
        Func<QueueMessage, CancellationToken, Task> handler,
        Func<CancellationToken, Task> body)
    {
        using var stopping = new CancellationTokenSource();

        await using var consumer = new RabbitMqConsumer(_connectionString, prefetchCount);

        var consuming = consumer.ConsumeAsync(handler, stopping.Token);

        try
        {
            await body(stopping.Token);
        }
        finally
        {
            await stopping.CancelAsync();
            await consuming;
        }
    }

    /// <summary>Publishes through the real dispatcher, so what is consumed is what the relay produces.</summary>
    private async Task<IReadOnlyList<Guid>> PublishAsync(int count)
    {
        var deliveryIds = new List<Guid>();

        await using var dispatcher = new RabbitMqDispatcher(_connectionString);

        for (var index = 0; index < count; index++)
        {
            var deliveryId = Guid.CreateVersion7();
            deliveryIds.Add(deliveryId);

            await dispatcher.DispatchAsync(
                OutboxMessage.AnnounceDelivery(Guid.CreateVersion7(), deliveryId, DateTimeOffset.UtcNow),
                CancellationToken.None);
        }

        return deliveryIds;
    }

    private static DeliveryDispatch Dispatched(QueueMessage message) =>
        DeliveryDispatch.FromPayload(Encoding.UTF8.GetString(message.Body.Span));

    private async Task CleanQueueAsync()
    {
        await using var connection = await ConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await MessagingTopology.DeclareDeliveryQueueAsync(channel, passive: false, CancellationToken.None);

        // A previous test may have left a delivery it never acknowledged; every test starts from an
        // empty queue rather than from whatever ran before it.
        await channel.QueuePurgeAsync(MessagingTopology.DeliveryDispatchQueue, CancellationToken.None);
    }

    private async Task<uint> QueueDepthAsync()
    {
        await using var connection = await ConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        return (await MessagingTopology.DeclareDeliveryQueueAsync(channel, passive: true, CancellationToken.None))
            .MessageCount;
    }

    private async Task WaitForQueueDepthAsync(uint expected)
    {
        await using var connection = await ConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            var depth = (await MessagingTopology
                .DeclareDeliveryQueueAsync(channel, passive: true, CancellationToken.None)).MessageCount;

            if (depth == expected)
            {
                return;
            }

            if (stopwatch.Elapsed > Timeout)
            {
                depth.Should().Be(expected, "the queue settles on what the workers left behind");
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, string because)
    {
        var stopwatch = Stopwatch.StartNew();

        while (!condition())
        {
            if (stopwatch.Elapsed > Timeout)
            {
                condition().Should().BeTrue(because);
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private async Task<IConnection> ConnectionAsync()
    {
        var factory = new ConnectionFactory { Uri = new Uri(_connectionString) };

        return await factory.CreateConnectionAsync(CancellationToken.None);
    }
}
