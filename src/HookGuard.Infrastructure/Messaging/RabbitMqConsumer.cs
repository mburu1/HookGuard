using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace HookGuard.Infrastructure.Messaging;

/// <summary>
/// Consumes the work queue with manual acknowledgements and a bounded prefetch.
/// </summary>
/// <remarks>
/// <para>
/// Acknowledgements are manual because a message may only be considered handled once the handler has
/// returned. A worker that dies mid-flight leaves its message unacknowledged, and the broker hands it
/// to another worker — the queue is a nudge, so the delivery is not lost by the worker holding it.
/// </para>
/// <para>
/// A message whose handler threw is dead-lettered rather than requeued. Requeueing would spin the
/// same undeliverable message through the queue at full speed, and it is not needed: the delivery row
/// in the database is the record of what still has to happen, and the scheduler sweep re-enqueues it
/// when its retry comes due. Dead-lettering it rather than dropping it keeps the message itself
/// inspectable too, so a message that could not even be turned into an attempt leaves evidence.
/// </para>
/// </remarks>
public sealed class RabbitMqConsumer : IAsyncDisposable
{
    private readonly ConnectionFactory _factory;
    private readonly ushort _prefetchCount;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqConsumer(string connectionString, ushort prefetchCount = MessagingTopology.PrefetchCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentOutOfRangeException.ThrowIfZero(prefetchCount);

        _factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString),
            AutomaticRecoveryEnabled = true,
        };

        _prefetchCount = prefetchCount;
    }

    /// <summary>
    /// Consumes until <paramref name="cancellationToken"/> is cancelled, handing each delivery to
    /// <paramref name="handler"/> and acknowledging it only after the handler returned without
    /// throwing. A handler that throws sends the message to the dead-letter queue, which is safe
    /// because the delivery row is what actually schedules the retry.
    /// </summary>
    public async Task ConsumeAsync(
        Func<QueueMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var channel = await ReadyChannelAsync(cancellationToken).ConfigureAwait(false);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, delivery) => DeliverAsync(channel, handler, delivery, cancellationToken);

            await channel.BasicConsumeAsync(
                queue: MessagingTopology.DeliveryDispatchQueue,
                autoAck: false,
                consumerTag: string.Empty,
                noLocal: false,
                exclusive: false,
                arguments: null,
                consumer: consumer,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await WaitForCancellationAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Closing the channel lets the delivery being handled finish first, so a worker that is
            // asked to stop settles the message it is holding instead of dropping it half-done.
            await CloseAsync().ConfigureAwait(false);
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private static async Task DeliverAsync(
        IChannel channel,
        Func<QueueMessage, CancellationToken, Task> handler,
        BasicDeliverEventArgs delivery,
        CancellationToken cancellationToken)
    {
        var message = new QueueMessage(delivery.BasicProperties?.MessageId, delivery.Body.ToArray());

        try
        {
            await handler(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Requeue false, and the queue itself names a dead-letter exchange, so the broker parks
            // this rather than handing it straight back to a worker that has just refused it.
            await SettleAsync(() => channel.BasicNackAsync(
                delivery.DeliveryTag,
                multiple: false,
                requeue: false,
                cancellationToken: CancellationToken.None)).ConfigureAwait(false);

            return;
        }

        await SettleAsync(() => channel.BasicAckAsync(
            delivery.DeliveryTag,
            multiple: false,
            cancellationToken: CancellationToken.None)).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends an acknowledgement and treats a channel that is already gone as success: the broker never
    /// saw the acknowledgement, so it still owns the message and will hand it to another worker. That
    /// is the same outcome as an acknowledgement that arrived, and it must not surface as an
    /// unhandled exception on the consumer's dispatch loop when a worker stops with a handler still
    /// running.
    /// </summary>
    private static async Task SettleAsync(Func<ValueTask> acknowledgement)
    {
        try
        {
            await acknowledgement().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is AlreadyClosedException or ObjectDisposedException)
        {
        }
    }

    private static async Task WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var registration = cancellationToken.Register(() => stopped.TrySetResult());

        await stopped.Task.ConfigureAwait(false);
    }

    private async Task<IChannel> ReadyChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await CloseAsync().ConfigureAwait(false);

        _connection = await _factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

        // The dispatch concurrency has to match the prefetch, or the prefetch buys nothing: a channel
        // hands deliveries to the handler one at a time by default, so asking the broker to send more
        // than one would only park messages that no handler is looking at.
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: _prefetchCount),
            cancellationToken).ConfigureAwait(false);

        // Per consumer, not per channel: the ceiling is what spreads a backlog across workers instead
        // of letting the first one to connect take all of it.
        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: _prefetchCount,
            global: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await MessagingTopology
            .DeclareDeliveryQueueAsync(_channel, passive: false, cancellationToken)
            .ConfigureAwait(false);

        return _channel;
    }

    private async Task CloseAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
            _channel = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }
}
