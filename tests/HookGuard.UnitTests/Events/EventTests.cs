using System.Text;
using FluentAssertions;
using HookGuard.Domain.Events;

namespace HookGuard.UnitTests.Events;

public class EventTests
{
    private static readonly Guid SourceId = Guid.Parse("0191f0a0-0000-7000-8000-000000000001");
    private static readonly DateTimeOffset ReceivedAt = new(2026, 10, 22, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Records_the_raw_payload_verbatim()
    {
        var body = Encoding.UTF8.GetBytes("""{"a": 1}""");

        var @event = Record(payload: body);

        @event.Payload.ToArray().Should().Equal(body);
        @event.ReceivedAt.Should().Be(ReceivedAt);
        @event.SignatureValid.Should().BeTrue();
    }

    [Fact]
    public void Generates_a_sorted_uuid_version_7_identifier_at_the_edge()
    {
        var id = Guid.CreateVersion7();

        Record(id: id).Id.Should().Be(id);
        id.Version.Should().Be(7);
    }

    [Fact]
    public void Rejects_an_empty_id()
    {
        var act = () => Record(id: Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rejects_an_empty_source()
    {
        var act = () => Record(sourceId: Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_dedupe_key(string dedupeKey)
    {
        var act = () => Record(dedupeKey: dedupeKey);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rejects_a_dedupe_key_longer_than_the_indexable_width()
    {
        var act = () => Record(dedupeKey: new string('x', EventDedupeKey.MaxLength + 1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rejects_a_missing_payload()
    {
        var act = () => Record(payload: ReadOnlyMemory<byte>.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rejects_missing_headers()
    {
        // Called directly rather than through the helper: the helper substitutes a default
        // dictionary for null, which is exactly what this test needs to avoid.
        var act = () => Event.Record(
            Guid.CreateVersion7(),
            SourceId,
            "QK123456",
            null!,
            "payload"u8.ToArray(),
            ReceivedAt,
            signatureValid: true);

        act.Should().Throw<ArgumentNullException>();
    }

    private static Event Record(
        Guid? id = null,
        Guid? sourceId = null,
        string dedupeKey = "QK123456",
        IReadOnlyDictionary<string, string>? headers = null,
        ReadOnlyMemory<byte>? payload = null,
        bool signatureValid = true) =>
        Event.Record(
            id ?? Guid.CreateVersion7(),
            sourceId ?? SourceId,
            dedupeKey,
            headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            payload ?? "payload"u8.ToArray(),
            ReceivedAt,
            signatureValid);
}
