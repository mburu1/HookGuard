using FluentAssertions;
using HookGuard.Application.Deliveries;
using HookGuard.Domain.Deliveries;
using HookGuard.Domain.Destinations;
using HookGuard.Domain.Events;
using HookGuard.Domain.Signatures;
using HookGuard.Domain.Sources;
using HookGuard.Domain.Time;
using HookGuard.Infrastructure.Persistence;
using HookGuard.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace HookGuard.IntegrationTests.Deliveries;

/// <summary>
/// M2.3 and M2.5. Claiming is what makes a duplicate queue message harmless, and the sweep is what
/// keeps a delivery moving after that: both decisions are taken by the database rather than by this
/// process, so they are only worth anything against a real one. The same goes for the outcome being
/// readable back off the row.
/// </summary>
public sealed class DeliveryStoreTests : IAsyncLifetime
{
    private const string Secret = "test-signing-secret-32-characters-long";
    private const string DestinationUrl = "https://merchant.example/hooks";

    /// <summary>
    /// A fixed instant, so the claim the store records and the window the sweep applies can be
    /// compared against something that does not move.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 30, 0, TimeSpan.Zero);

    private static readonly byte[] Payload = """{"TransID":"QK123456","Amount":1500}"""u8.ToArray();

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly FixedClock _clock = new(Now);

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
        var store = new DeliveryStore(db, _clock);

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

        var claimed = await new DeliveryStore(db, _clock)
            .ClaimAsync(Guid.CreateVersion7(), CancellationToken.None);

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
            new DeliveryStore(first, _clock).ClaimAsync(deliveryId, CancellationToken.None),
            new DeliveryStore(second, _clock).ClaimAsync(deliveryId, CancellationToken.None));

        claims.Where(claimed => claimed is not null).Should()
            .ContainSingle("exactly one of the two may take the delivery");
    }

    [Fact]
    public async Task Persists_an_acceptance()
    {
        var deliveryId = await SeedDeliveryAsync();

        await using var db = NewContext();
        var store = new DeliveryStore(db, _clock);

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
        var store = new DeliveryStore(db, _clock);

        var claimed = await store.ClaimAsync(deliveryId, CancellationToken.None);
        claimed!.Delivery.Fail("The destination answered HTTP 503.", retryAt);

        await store.RecordOutcomeAsync(claimed.Delivery, CancellationToken.None);

        var row = await ReadAsync(deliveryId);
        row.Status.Should().Be(DeliveryStatus.Pending, "a failed attempt waits to be swept up again");
        row.AttemptCount.Should().Be(1);
        row.LastError.Should().Be("The destination answered HTTP 503.");
        row.NextAttemptAt.Should().Be(retryAt);
    }

    [Fact]
    public async Task Records_when_the_attempt_was_claimed()
    {
        var deliveryId = await SeedDeliveryAsync();

        await using var db = NewContext();
        await new DeliveryStore(db, _clock).ClaimAsync(deliveryId, CancellationToken.None);

        // The claim is what the visibility timeout measures from, so an attempt that is never heard
        // from again has to be dated at the moment it began.
        (await ReadAsync(deliveryId)).ClaimedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Hands_back_a_delivery_whose_worker_never_came_back()
    {
        var deliveryId = await SeedDeliveryAsync();

        await using var db = NewContext();
        var store = new DeliveryStore(db, _clock);
        await store.ClaimAsync(deliveryId, CancellationToken.None);

        var reclaimed = await store.ReclaimStuckAsync(Now.AddSeconds(1), CancellationToken.None);

        reclaimed.Should().Be(1);

        var row = await ReadAsync(deliveryId);
        row.Status.Should().Be(DeliveryStatus.Pending, "a lost attempt is retried rather than abandoned");
        row.NextAttemptAt.Should().Be(Now, "the wait that was scheduled belonged to an attempt nobody finished");
        row.ClaimedAt.Should().BeNull("nothing holds the row once the attempt that held it is gone");
        row.AttemptCount.Should().Be(1, "the attempt happened even though nobody saw how it ended");
    }

    [Fact]
    public async Task Leaves_an_attempt_that_is_still_inside_its_visibility_window()
    {
        var deliveryId = await SeedDeliveryAsync();

        await using var db = NewContext();
        var store = new DeliveryStore(db, _clock);
        await store.ClaimAsync(deliveryId, CancellationToken.None);

        // A worker that is still trying must keep the row: taking it back would send the same payload
        // twice, which is the one thing the visibility window exists to prevent.
        (await store.ReclaimStuckAsync(Now.AddSeconds(-1), CancellationToken.None)).Should().Be(0);

        var row = await ReadAsync(deliveryId);
        row.Status.Should().Be(DeliveryStatus.InFlight);
        row.ClaimedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Lists_the_deliveries_that_are_due_and_no_others()
    {
        var overdue = await SeedDeliveryAsync(dueAt: Now.AddSeconds(-1));
        var later = await SeedDeliveryAsync(dueAt: Now.AddMinutes(5));
        var inFlight = await SeedDeliveryAsync(dueAt: Now.AddSeconds(-1));

        await using var db = NewContext();
        var store = new DeliveryStore(db, _clock);
        await store.ClaimAsync(inFlight, CancellationToken.None);

        var due = await store.ListDueAsync(Now, batchSize: 100, CancellationToken.None);

        // A retry due in the future has not come due, and an attempt in flight is somebody else's work
        // right now: announcing either would put a message on the queue that the claim would refuse.
        due.Should().Equal([overdue]);
        later.Should().NotBe(overdue);
    }

    /// <summary>
    /// Seeds a source, destination, event and delivery the way ingest would, so what is claimed is a
    /// row HookGuard actually produces rather than one shaped for the test.
    /// </summary>
    /// <param name="dueAt">When the delivery becomes eligible for an attempt; now, unless a test needs otherwise.</param>
    private async Task<Guid> SeedDeliveryAsync(DateTimeOffset? dueAt = null)
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
            _clock.UtcNow,
            signatureValid: true);

        var delivery = Delivery.Schedule(
            Guid.CreateVersion7(),
            @event.Id,
            destination.Id,
            dueAt ?? _clock.UtcNow);

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

    /// <summary>
    /// A clock that stands still, so the instant a claim is dated with and the instant the sweep calls
    /// "now" can be compared exactly instead of to within a test's runtime.
    /// </summary>
    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
