using System.Globalization;
using System.Text;
using FluentAssertions;
using HookGuard.Application.Ingest;
using HookGuard.Domain.Signatures;
using HookGuard.Domain.Sources;
using HookGuard.Infrastructure.Persistence;
using HookGuard.Infrastructure.Persistence.Repositories;
using HookGuard.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace HookGuard.IntegrationTests.Ingest;

/// <summary>
/// M1.6. Deduplication is enforced by the unique <c>(source_id, dedupe_key)</c> index rather than by
/// application code, so the guarantee can only be proven against a real database. One hundred
/// identical deliveries are fired simultaneously and exactly one of them may be stored.
/// </summary>
public sealed class ConcurrentIngestTests : IAsyncLifetime
{
    private const string Secret = "test-signing-secret-32-characters-long";
    private const string Body = """{"TransID":"QK123456","Amount":1500}""";

    /// <summary>Deliveries in flight at once. The real thing this milestone claims.</summary>
    private const int ConcurrentDeliveries = 100;

    /// <summary>
    /// The Postgres image allows 100 connections in total; keeping the pool below that leaves room
    /// for the migration and assertion connections without weakening the concurrency under test.
    /// </summary>
    private const int MaximumPoolSize = 20;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly SystemClock _clock = new();

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _connectionString = $"{_postgres.GetConnectionString()};Maximum Pool Size={MaximumPoolSize}";

        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task One_hundred_identical_deliveries_store_exactly_one_event()
    {
        var source = new Source(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "M-Pesa Paybill",
            Secret,
            SignatureScheme.TimestampBodyHmacSha256,
            TimeSpan.FromMinutes(5));

        await using (var db = NewContext())
        {
            db.Sources.Add(source);
            await db.SaveChangesAsync();
        }

        var body = Encoding.UTF8.GetBytes(Body);
        var headers = SignedHeaders();

        var results = await Task.WhenAll(
            Enumerable.Range(0, ConcurrentDeliveries)
                .Select(_ => DeliverAsync(source.Id, headers, body)));

        // Every delivery is answered, and the answers add up: no delivery is lost, and none of them
        // is stored twice.
        results.Count(result => result.Status == IngestStatus.Accepted).Should().Be(1);
        results.Count(result => result.Status == IngestStatus.Duplicate).Should().Be(ConcurrentDeliveries - 1);

        await using var check = NewContext();

        (await check.Events.CountAsync()).Should().Be(1);
    }

    private async Task<IngestResult> DeliverAsync(
        Guid sourceId,
        IReadOnlyDictionary<string, string> headers,
        ReadOnlyMemory<byte> body)
    {
        // One context per delivery, as a request scope would supply: a DbContext is not thread
        // safe, and sharing one would make this test pass for the wrong reason.
        await using var db = NewContext();

        var handler = new IngestEventHandler(
            new SourceRepository(db),
            new EventRepository(db),
            new SignatureVerifier(_clock),
            _clock);

        return await handler.HandleAsync(sourceId, headers, body, CancellationToken.None);
    }

    private Dictionary<string, string> SignedHeaders()
    {
        var timestamp = _clock.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SignatureHeaders.Timestamp] = timestamp,
            [SignatureHeaders.Signature] =
                SignatureVerifier.Sign(Secret, timestamp, Encoding.UTF8.GetBytes(Body)),
        };
    }

    private HookGuardDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<HookGuardDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        return new HookGuardDbContext(options);
    }
}
