using System.Text;
using HookGuard.Application.Abstractions;
using HookGuard.Domain.Outbox;
using RabbitMQ.Client;

namespace HookGuard.Infrastructure.Messaging;

/// <summary>
/// Publishes outbox messages to RabbitMQ with publisher confirms turned on, so
/// <see cref="DispatchAsync"/> completing is a statement about what the broker holds rather than
/// about what this process handed to a socket.
/// </summary>
/// <remarks>
/// One connection and one channel are kept open for the life of the process: connecting per publish
/// would cost far more than the publish itself, and the channel is serialised because a channel is
/// not safe to publish on concurrently.
/// </remarks>
public sealed class RabbitMqDispatcher : IDispatcher, IAsyncDisposable
{
    /// <summary>
    /// Confirms on, and confirmation <em>tracking</em> on as well: the library then stamps each
    /// publish with its sequence number, which is what lets it match the broker's <c>basic.return</c>
    /// to the message that failed to route instead of giving up on it.
    /// </summary>
    private static readonly CreateChannelOptions ConfirmingChannel = new(
        publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true);

    private readonly ConnectionFactory _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqDispatcher(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString),

            // The broker may be restarted under us; recovery reopens the connection and restores the
            // channel without the relay having to notice or intervene.
            AutomaticRecoveryEnabled = true,
        };
    }

    public async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var channel = await ReadyChannelAsync(cancellationToken).ConfigureAwait(false);

            var properties = new BasicProperties
            {
                // A durable queue only survives a restart for messages that are themselves persistent.
                Persistent = true,
                ContentType = "application/json",
                Type = message.Type,
                MessageId = message.Id.ToString(),
                Timestamp = new AmqpTimestamp(message.CreatedAt.ToUnixTimeSeconds()),
            };

            // mandatory: with nothing bound to route to, the broker returns the message and the
            // pending confirm fails, which surfaces as a PublishException instead of a silent drop.
            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: MessagingTopology.DeliveryDispatchQueue,
                mandatory: true,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(message.Payload),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
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

    /// <summary>
    /// Returns an open channel, connecting on first use and again after the broker closed the
    /// previous one. A channel that dies mid-publish throws out of <see cref="DispatchAsync"/>; the
    /// next attempt finds it closed and reconnects rather than publishing into a dead channel
    /// forever.
    /// </summary>
    private async Task<IChannel> ReadyChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await CloseAsync().ConfigureAwait(false);

        _connection = await _factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        _channel = await _connection.CreateChannelAsync(ConfirmingChannel, cancellationToken).ConfigureAwait(false);

        // Declared before the first publish rather than in a separate topology step, so publishing
        // can never race the queue's existence and be returned as unroutable.
        await _channel.QueueDeclareAsync(
            queue: MessagingTopology.DeliveryDispatchQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            passive: false,
            noWait: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

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
