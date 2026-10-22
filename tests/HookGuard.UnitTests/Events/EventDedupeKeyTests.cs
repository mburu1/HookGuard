using System.Text;
using FluentAssertions;
using HookGuard.Domain.Events;

namespace HookGuard.UnitTests.Events;

public class EventDedupeKeyTests
{
    private const string Body = """{"TransID":"QK123456"}""";

    [Fact]
    public void Prefers_a_provider_supplied_event_id()
    {
        var headers = Headers((EventDedupeKey.ExplicitKeyHeader, "QK123456"));

        EventDedupeKey.Resolve(headers, Bytes(Body)).Should().Be("QK123456");
    }

    [Fact]
    public void Finds_the_event_id_header_regardless_of_case()
    {
        var headers = Headers(("x-hookguard-event-id", "QK123456"));

        EventDedupeKey.Resolve(headers, Bytes(Body)).Should().Be("QK123456");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Falls_back_to_the_payload_hash_when_the_event_id_is_blank(string eventId)
    {
        var headers = Headers((EventDedupeKey.ExplicitKeyHeader, eventId));

        EventDedupeKey.Resolve(headers, Bytes(Body)).Should().Be(EventDedupeKey.FromBody(Bytes(Body)));
    }

    [Fact]
    public void Falls_back_to_the_payload_hash_when_no_event_id_is_sent()
    {
        EventDedupeKey.Resolve(Headers(), Bytes(Body)).Should().Be(EventDedupeKey.FromBody(Bytes(Body)));
    }

    [Fact]
    public void Gives_a_retry_of_the_same_payload_the_same_key()
    {
        var first = EventDedupeKey.Resolve(Headers(), Bytes(Body));
        var retry = EventDedupeKey.Resolve(Headers(), Bytes(Body));

        retry.Should().Be(first);
    }

    [Fact]
    public void Gives_a_different_payload_a_different_key()
    {
        var first = EventDedupeKey.Resolve(Headers(), Bytes("""{"TransID":"QK123456","Amount":1}"""));
        var second = EventDedupeKey.Resolve(Headers(), Bytes("""{"TransID":"QK123456","Amount":2}"""));

        second.Should().NotBe(first);
    }

    [Fact]
    public void Hashes_the_raw_bytes_so_whitespace_differences_are_distinct_events()
    {
        var compact = EventDedupeKey.Resolve(Headers(), Bytes("""{"a":1}"""));
        var spaced = EventDedupeKey.Resolve(Headers(), Bytes("""{"a": 1}"""));

        spaced.Should().NotBe(compact);
    }

    [Fact]
    public void Trims_and_truncates_an_oversized_provider_event_id()
    {
        var oversized = new string('x', EventDedupeKey.MaxLength + 50);

        var resolved = EventDedupeKey.Resolve(
            Headers((EventDedupeKey.ExplicitKeyHeader, $"  {oversized}  ")),
            Bytes(Body));

        resolved.Should().HaveLength(EventDedupeKey.MaxLength);
    }

    [Fact]
    public void Produces_a_lowercase_hex_hash()
    {
        EventDedupeKey.FromBody(Bytes(Body)).Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
    }

    private static IReadOnlyDictionary<string, string> Headers(params (string Key, string Value)[] entries)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in entries)
        {
            headers[key] = value;
        }

        return headers;
    }

    private static byte[] Bytes(string body) => Encoding.UTF8.GetBytes(body);
}
