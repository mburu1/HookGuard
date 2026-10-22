using FluentAssertions;
using HookGuard.Application.Deliveries;
using HookGuard.Domain.Deliveries;
using HookGuard.Domain.Destinations;
using HookGuard.Domain.Events;
using HookGuard.Domain.Signatures;
using HookGuard.Domain.Sources;
using HookGuard.Infrastructure.Persistence;
using HookGuard.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace HookGuard.IntegrationTests.Deliveries;

/// <summary>
/// M2.3. Claiming is what makes a duplicate queue message harmless, and that decision is taken by the
/// database rather than by this process, so it is only worth anything against a real one. The same
/// goes for the outcome being readable back off the row.
/// </summary>
public sealed class DeliveryStoreTests : IAsyncLifetime
{
    private const string Secret = "test-signing-secret-32-characters-long";
    private const string DestinationUrl = "https://merchant.example/hooks";
    private static readonly byte[] Payload = """{"TransID":"QK123456","Amount":1500}"""u8.ToArray();

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();

        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Claims_a_pending_delivery_once_and_counts_the_attempt()
    {
        var deliveryId = await SeedDeliveryAsync();

        await using var db = NewContext();
        var store = new DeliveryStore(db);

        var claimed = await store.ClaimAsync(deliveryId, CancellationToken.None);

        claimed.Should().NotBeNull();
        claimed!.Delivery.Status.Should().Be(DeliveryStatus.InFlight);
        claimed.Delivery.AttemptCount.Should().Be(1, "claiming is the moment an attempt begins");

        // Everything the attempt needs arrives with the claim, because a delivery means nothing
        // without the payload it carries and the destination it carries it to.
        claimed.Event.Payload.ToArray().Should().Equal(Payload);
        claimed.Destination.Url.Should().Be(DestinationUrl);

        // The second claim is what a duplicate message looks like from here: the row is no longer
        // Pending, so there is nothing to take.
        (await store.ClaimAsync(deliveryId, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Claims_nothing_for_a_delivery_that_is_not_there()
    {
        await using var db = NewContext();

        var claimed = await new DeliveryStore(db).ClaimAsync(Guid.CreateVersion7(), CancellationToken.None);

        claimed.Should().BeNull();
    }

    [Fact]
    public async Task Only_one_of_two_racing_workers_claims_the_delivery()
    {
        var deliveryId = await SeedDeliveryAsync();

        await using var first = NewContext();
        await using var second = NewContext();

        // Two workers handed the same message at the same instant. The guard lives in the UPDATE, so
        // the loser is decided by the database rather than by whichever worker read the row first.
        var claims = await Task.WhenAll(
            new DeliveryStore(first).ClaimAsync(deliveryId, CancellationToken.None),
            new DeliveryStore(second).ClaimAsync(deliveryId, CancellationToken.None));

        claims.Where(claimed => claimed is not null).Should()
            .ContainSingle("exactly one of the two may take the delivery");
    }

    [Fact]
    public async Task Persists_an_acceptance()
    {
        var deliveryId = await SeedDeliveryAsync();

        await using var db = NewContext();
        var store = new DeliveryStore(db);

        var claimed = await store.ClaimAsync(deliveryId, CancellationToken.None);
        claimed!.Delivery.Succeed();

        await store.RecordOutcomeAsync(claimed.Delivery, CancellationToken.None);

        var row = await ReadAsync(deliveryId);
        row.Status.Should().Be(DeliveryStatus.Succeeded);
        row.AttemptCount.Should().Be(1);
        row.LastError.Should().BeNull();
    }

    [Fact]
    public async Task Persists_a_failure_with_its_reason_and_the_time_to_try_again()
    {
        var deliveryId = await SeedDeliveryAsync();
        var retryAt = new DateTimeOffset(2026, 10, 22, 9, 30, 30, TimeSpan.Zero);

        await using var db = NewContext();
        var store = new DeliveryStore(db);

        var claimed = await store.ClaimAsync(deliveryId, CancellationToken.None);
        claimed!.Delivery.Fail("The destination answered HTTP 503.", retryAt);

        await store.RecordOutcomeAsync(claimed.Delivery, CancellationToken.None);

        var row = await ReadAsync(deliveryId);
        row.Status.Should().Be(DeliveryStatus.Pending, "a failed attempt waits to be swept up again");
        row.AttemptCount.Should().Be(1);
        row.LastError.Should().Be("The destination answered HTTP 503.");
        row.NextAttemptAt.Should().Be(retryAt);
    }

    /// <summary>
    /// Seeds a source, destination, event and delivery the way ingest would, so what is claimed is a
    /// row HookGuard actually produces rather than one shaped for the test.
    /// </summary>
    private async Task<Guid> SeedDeliveryAsync()
    {
        var source = new Source(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "M-Pesa Paybill",
            Secret,
            SignatureScheme.TimestampBodyHmacSha256,
            TimeSpan.FromMinutes(5));

        var destination = Destination.Configure(
            Guid.CreateVersion7(),
            source.Id,
            DestinationUrl,
            "destination-signing-secret",
            maxAttempts: 5,
            timeout: TimeSpan.FromSeconds(10));

        var @event = Event.Record(
            Guid.CreateVersion7(),
            source.Id,
            "QK123456",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Payload,
            DateTimeOffset.UtcNow,
            signatureValid: true);

        var delivery = Delivery.Schedule(
            Guid.CreateVersion7(),
            @event.Id,
            destination.Id,
            DateTimeOffset.UtcNow);

        await using var db = NewContext();
        db.Sources.Add(source);
        db.Destinations.Add(destination);
        db.Events.Add(@event);
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();

        return delivery.Id;
    }

    private async Task<Delivery> ReadAsync(Guid deliveryId)
    {
        await using var db = NewContext();

        return await db.Deliveries.AsNoTracking().SingleAsync(delivery => delivery.Id == deliveryId);
    }

    private HookGuardDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<HookGuardDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        return new HookGuardDbContext(options);
    }
}
