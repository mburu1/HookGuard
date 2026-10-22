using System.Globalization;
using System.Text;
using FluentAssertions;
using HookGuard.Application.Ingest;
using HookGuard.Application.Outbox;
using HookGuard.Domain.Destinations;
using HookGuard.Domain.Outbox;
using HookGuard.Domain.Signatures;
using HookGuard.Domain.Sources;
using HookGuard.Infrastructure.Messaging;
using HookGuard.Infrastructure.Persistence;
using HookGuard.Infrastructure.Persistence.Repositories;
using HookGuard.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace HookGuard.IntegrationTests.Outbox;

/// <summary>
/// M2.1. The relay's two promises are both statements about a broker HookGuard does not control, so
/// they are only worth anything against a real queue and a real database: every unpublished message
/// reaches the queue, and the database is told about it only after the broker said so.
/// </summary>
public sealed class OutboxRelayTests : IAsyncLifetime
{
    private const string Secret = "test-signing-secret-32-characters-long";
    private const string DestinationUrl = "https://merchant.example/hooks";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly RabbitMqContainer _rabbitmq = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .WithUsername("hookguard")
        .WithPassword("hookguard")
        .Build();

    private readonly SystemClock _clock = new();

    private string _connectionString = string.Empty;
    private Source _source = null!;
    private int _ingested;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitmq.StartAsync());

        _connectionString = _postgres.GetConnectionString();

        _source = new Source(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "M-Pesa Paybill",
            Secret,
            SignatureScheme.TimestampBodyHmacSha256,
            TimeSpan.FromMinutes(5));

        var destination = Destination.Configure(
            Guid.CreateVersion7(),
            _source.Id,
            DestinationUrl,
            "destination-signing-secret",
            maxAttempts: 5,
            timeout: TimeSpan.FromSeconds(10));

        await using var db = NewContext();
        await db.Database.MigrateAsync();

        db.Sources.Add(_source);
        db.Destinations.Add(destination);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbitmq.DisposeAsync();
    }

    [Fact]
    public async Task Publishes_the_backlog_once_and_records_every_confirmation()
    {
        await IngestAsync(count: 3);

        await using var db = NewContext();
        await using var dispatcher = new RabbitMqDispatcher(_rabbitmq.GetConnectionString());
        var relay = new OutboxRelay(new OutboxStore(db), dispatcher, _clock);

        var published = await relay.PublishBatchAsync(batchSize: 100, CancellationToken.None);

        published.Should().Be(3);

        // The confirmation is the whole point: published_at is evidence the broker took the message.
        var outbox = await db.OutboxMessages.AsNoTracking().ToListAsync();
        outbox.Should().HaveCount(3);
        outbox.Should().OnlyContain(message => message.PublishedAt != null);

        // What is on the queue names the deliveries the database holds, in the order they were
        // written. A message that named nothing, or named a delivery twice, would be a lost event.
        var expected = await db.Deliveries.AsNoTracking()
            .OrderBy(delivery => delivery.Id)
            .Select(delivery => delivery.Id)
            .ToListAsync();

        var queued = await DrainQueueAsync();
        queued.Select(dispatch => dispatch.DeliveryId).Should().Equal(expected);

        // A second sweep finds nothing to do, because the first one recorded what it published.
        (await relay.PublishBatchAsync(batchSize: 100, CancellationToken.None)).Should().Be(0);
        (await QueueDepthAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Keeps_a_message_unpublished_when_the_broker_cannot_route_it()
    {
        await IngestAsync(count: 1);

        await using var db = NewContext();
        await using var dispatcher = new RabbitMqDispatcher(_rabbitmq.GetConnectionString());
        var relay = new OutboxRelay(new OutboxStore(db), dispatcher, _clock);

        (await relay.PublishBatchAsync(batchSize: 100, CancellationToken.None)).Should().Be(1);

        // The broker has nothing to route to any more. Without confirms and the mandatory flag the
        // publish would look like a success and the event would be lost in silence.
        await DeleteQueueAsync();
        await IngestAsync(count: 1);

        var sweep = async () => await relay.PublishBatchAsync(batchSize: 100, CancellationToken.None);
        await sweep.Should().ThrowAsync<PublishException>();

        var unpublished = await db.OutboxMessages.AsNoTracking()
            .Where(message => message.PublishedAt == null)
            .ToListAsync();

        unpublished.Should().ContainSingle("a message the broker never took is still HookGuard's to publish");
    }

    /// <summary>
    /// Drives the ingest path instead of writing outbox rows by hand, so the backlog under test is
    /// the one HookGuard actually produces.
    /// </summary>
    private async Task IngestAsync(int count)
    {
        for (var index = 0; index < count; index++)
        {
            _ingested++;

            var body = Encoding.UTF8.GetBytes(
                $$"""{"TransID":"QK{{_ingested}}","Amount":1500}""");

            await using var db = NewContext();

            var handler = new IngestEventHandler(
                new SourceRepository(db),
                new DestinationRepository(db),
                new EventRepository(db),
                new SignatureVerifier(_clock),
                _clock);

            var result = await handler
                .HandleAsync(_source.Id, SignedHeaders(body), body, CancellationToken.None);

            result.Status.Should().Be(IngestStatus.Accepted);
        }
    }

    private Dictionary<string, string> SignedHeaders(byte[] body)
    {
        var timestamp = _clock.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SignatureHeaders.Timestamp] = timestamp,
            [SignatureHeaders.Signature] = SignatureVerifier.Sign(Secret, timestamp, body),
        };
    }

    /// <summary>Empties the work queue, which is how many messages the broker actually accepted.</summary>
    private async Task<List<DeliveryDispatch>> DrainQueueAsync()
    {
        await using var connection = await ConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        var queued = new List<DeliveryDispatch>();

        while (await channel.BasicGetAsync(
            queue: MessagingTopology.DeliveryDispatchQueue,
            autoAck: true,
            cancellationToken: CancellationToken.None) is { } message)
        {
            queued.Add(DeliveryDispatch.FromPayload(Encoding.UTF8.GetString(message.Body.Span)));
        }

        return queued;
    }

    private async Task<uint> QueueDepthAsync()
    {
        await using var connection = await ConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        return (await DeclareQueueAsync(channel, passive: true)).MessageCount;
    }

    private async Task DeleteQueueAsync()
    {
        await using var connection = await ConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.QueueDeleteAsync(
            queue: MessagingTopology.DeliveryDispatchQueue,
            ifUnused: false,
            ifEmpty: false,
            noWait: false,
            cancellationToken: CancellationToken.None);
    }

    /// <summary>
    /// Passive so the declaration only reports the queue the relay created; the arguments have to
    /// match how it was declared or the broker rejects the call.
    /// </summary>
    private static Task<QueueDeclareOk> DeclareQueueAsync(IChannel channel, bool passive) =>
        channel.QueueDeclareAsync(
            queue: MessagingTopology.DeliveryDispatchQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            passive: passive,
            noWait: false,
            cancellationToken: CancellationToken.None);

    private async Task<IConnection> ConnectionAsync()
    {
        var factory = new ConnectionFactory { Uri = new Uri(_rabbitmq.GetConnectionString()) };

        return await factory.CreateConnectionAsync(CancellationToken.None);
    }

    private HookGuardDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<HookGuardDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        return new HookGuardDbContext(options);
    }
}
