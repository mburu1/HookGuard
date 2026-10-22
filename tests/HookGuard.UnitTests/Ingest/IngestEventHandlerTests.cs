using System.Globalization;
using System.Text;
using FluentAssertions;
using HookGuard.Application.Ingest;
using HookGuard.Domain.Events;
using HookGuard.Domain.Signatures;
using HookGuard.Domain.Sources;
using HookGuard.UnitTests.Fakes;

namespace HookGuard.UnitTests.Ingest;

public class IngestEventHandlerTests
{
    private const string Secret = "test-signing-secret-32-characters-long";
    private const long Now = 1_800_000_000;
    private const string Body = """{"TransID":"QK123456","Amount":1500}""";

    private static readonly Guid SourceId = Guid.Parse("018f9b7a-0000-7000-8000-000000000001");
    private static readonly Guid TenantId = Guid.Parse("018f9b7a-0000-7000-8000-0000000000ff");
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    private readonly FakeEventRepository _events = new();
    private readonly FakeClock _clock = FakeClock.At(Now);

    [Fact]
    public async Task Accepts_a_valid_callback_and_stores_it()
    {
        var result = await HandleAsync(Body, SignedHeaders(Body));

        result.Status.Should().Be(IngestStatus.Accepted);
        result.EventId.Should().NotBeNull().And.NotBe(Guid.Empty);

        var stored = _events.Stored.Should().ContainSingle().Subject;
        stored.Id.Should().Be(result.EventId!.Value);
        stored.SourceId.Should().Be(SourceId);
        stored.Payload.ToArray().Should().Equal(Bytes(Body));
        stored.ReceivedAt.Should().Be(_clock.UtcNow);
        stored.SignatureValid.Should().BeTrue();
    }

    [Fact]
    public async Task Verifies_the_bytes_it_was_handed_and_not_a_parsed_payload()
    {
        // Same JSON document, different bytes. A gateway that re-serialised the body before
        // verifying would accept this; HookGuard must not.
        var headers = SignedHeaders(Body);

        var result = await HandleAsync("""{ "TransID": "QK123456", "Amount": 1500 }""", headers);

        result.Status.Should().Be(IngestStatus.Rejected);
        result.Failure.Should().Be(SignatureFailure.SignatureMismatch);
    }

    [Fact]
    public async Task Rejects_a_tampered_payload_without_reaching_the_repository()
    {
        var headers = SignedHeaders(Body);

        var result = await HandleAsync("""{"TransID":"QK123456","Amount":9999}""", headers);

        result.Status.Should().Be(IngestStatus.Rejected);
        _events.Attempts.Should().Be(0, "an unauthenticated request must not cause a database write");
    }

    [Theory]
    [InlineData(Now - 301)] // a replayed callback from outside the window
    [InlineData(Now + 301)]
    public async Task Rejects_a_timestamp_outside_the_sources_tolerance(long signedAt)
    {
        var result = await HandleAsync(Body, SignedHeaders(Body, signedAt));

        result.Status.Should().Be(IngestStatus.Rejected);
        result.Failure.Should().Be(SignatureFailure.TimestampOutsideTolerance);
        _events.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task Uses_the_tolerance_configured_on_the_source()
    {
        var strict = Source(tolerance: TimeSpan.FromSeconds(10));
        var headers = SignedHeaders(Body, Now - 30);

        var rejected = await HandlerFor(strict).HandleAsync(SourceId, headers, Bytes(Body), CancellationToken.None);
        var accepted = await HandlerFor(Source()).HandleAsync(SourceId, headers, Bytes(Body), CancellationToken.None);

        rejected.Failure.Should().Be(SignatureFailure.TimestampOutsideTolerance);
        accepted.Status.Should().Be(IngestStatus.Accepted);
    }

    [Fact]
    public async Task Reports_an_unknown_source_without_reaching_the_repository()
    {
        var result = await HandleAsync(Body, SignedHeaders(Body), sourceId: Guid.CreateVersion7());

        result.Status.Should().Be(IngestStatus.UnknownSource);
        result.EventId.Should().BeNull();
        _events.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task Rejects_a_callback_with_no_signature_headers()
    {
        var result = await HandleAsync(Body, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        result.Status.Should().Be(IngestStatus.Rejected);
        result.Failure.Should().Be(SignatureFailure.MissingTimestamp);
        _events.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task Acknowledges_a_retry_of_the_same_callback_as_a_duplicate()
    {
        var headers = SignedHeaders(Body);

        var first = await HandleAsync(Body, headers);
        var second = await HandleAsync(Body, headers);

        first.Status.Should().Be(IngestStatus.Accepted);
        second.Status.Should().Be(IngestStatus.Duplicate);
        second.EventId.Should().BeNull();
        _events.Stored.Should().ContainSingle("the retry must not be stored or processed again");
    }

    [Fact]
    public async Task Deduplicates_on_the_provider_event_id_when_one_is_sent()
    {
        // Two genuinely different payloads claiming the same provider event id: the provider is
        // telling us they are the same event, so only the first is stored.
        var first = await HandleAsync(Body, SignedHeaders(Body, eventId: "QK123456"));
        var second = await HandleAsync("""{"TransID":"QK123456","Amount":1500}""", SignedHeaders("""{"TransID":"QK123456","Amount":1500}""", eventId: "QK123456"));

        first.Status.Should().Be(IngestStatus.Accepted);
        second.Status.Should().Be(IngestStatus.Duplicate);
        _events.Stored.Should().ContainSingle();
        _events.Stored[0].DedupeKey.Should().Be("QK123456");
    }

    [Fact]
    public async Task Falls_back_to_the_body_when_the_event_id_header_is_blank()
    {
        var first = await HandleAsync(Body, SignedHeaders(Body, eventId: "   "));
        var second = await HandleAsync("""{"TransID":"QK123457","Amount":1500}""", SignedHeaders("""{"TransID":"QK123457","Amount":1500}""", eventId: "   "));

        first.Status.Should().Be(IngestStatus.Accepted);
        second.Status.Should().Be(IngestStatus.Accepted);
        _events.Stored.Should().HaveCount(2);
        _events.Stored[0].DedupeKey.Should().Be(EventDedupeKey.FromBody(Bytes(Body)));
    }

    private Task<IngestResult> HandleAsync(string body, Dictionary<string, string> headers, Guid? sourceId = null) =>
        HandlerFor(Source()).HandleAsync(sourceId ?? SourceId, headers, Bytes(body), CancellationToken.None);

    private IngestEventHandler HandlerFor(Source source) =>
        new(new FakeSourceRepository(source), _events, new SignatureVerifier(_clock), _clock);

    private static Source Source(TimeSpan? tolerance = null) =>
        new(SourceId, TenantId, "M-Pesa Paybill", Secret, SignatureScheme.TimestampBodyHmacSha256, tolerance ?? Tolerance);

    private static Dictionary<string, string> SignedHeaders(
        string body,
        long signedAt = Now,
        string? eventId = null)
    {
        var timestamp = signedAt.ToString(CultureInfo.InvariantCulture);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SignatureHeaders.Timestamp] = timestamp,
            [SignatureHeaders.Signature] = SignatureVerifier.Sign(Secret, timestamp, Bytes(body)),
        };

        if (eventId is not null)
        {
            headers[EventDedupeKey.ExplicitKeyHeader] = eventId;
        }

        return headers;
    }

    private static byte[] Bytes(string body) => Encoding.UTF8.GetBytes(body);
}
