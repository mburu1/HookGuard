using System.Text.Json;
using HookGuard.Domain.Deliveries;
using HookGuard.Domain.Sources;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HookGuard.Infrastructure.Persistence;

/// <summary>
/// Value conversions for the domain types that Postgres has no native mapping for. Kept together so
/// the shape of a stored row is described once, and so an entity configuration reads as schema
/// rather than as serialisation.
/// </summary>
internal static class Conversions
{
    private static readonly JsonSerializerOptions HeaderJson = new(JsonSerializerDefaults.Web);

    /// <summary>Headers are stored as <c>jsonb</c>, preserving what the provider actually sent.</summary>
    public static ValueConverter<IReadOnlyDictionary<string, string>, string> Headers { get; } = new(
        headers => JsonSerializer.Serialize(headers, HeaderJson),
        json => Rehydrate(JsonSerializer.Deserialize<Dictionary<string, string>>(json, HeaderJson)));

    /// <summary>
    /// Dictionaries compare by value, not by reference, so a mutation in place is still detected.
    /// </summary>
    public static ValueComparer<IReadOnlyDictionary<string, string>> HeadersComparer { get; } = new(
        (left, right) => HeadersEqual(left, right),
        headers => HeadersHash(headers),
        headers => new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Raw request bytes as <c>bytea</c>. The domain holds them as <see cref="ReadOnlyMemory{T}"/>
    /// so a caller cannot mutate a stored payload through the reference it owns.
    /// </summary>
    public static ValueConverter<ReadOnlyMemory<byte>, byte[]> Payload { get; } = new(
        payload => payload.ToArray(),
        bytes => new ReadOnlyMemory<byte>(bytes));

    /// <summary>Durations are stored as whole seconds, matching <c>tolerance_seconds</c>.</summary>
    public static ValueConverter<TimeSpan, int> Seconds { get; } = new(
        duration => (int)duration.TotalSeconds,
        seconds => TimeSpan.FromSeconds(seconds));

    /// <summary>
    /// Per-attempt budget, stored in milliseconds because that is the unit an HTTP timeout is
    /// configured in and sub-second precision matters when a destination is slow, not broken.
    /// </summary>
    public static ValueConverter<TimeSpan, int> Milliseconds { get; } = new(
        duration => (int)duration.TotalMilliseconds,
        milliseconds => TimeSpan.FromMilliseconds(milliseconds));

    /// <summary>
    /// Delivery status travels as its name: the partial index that finds due work filters on the
    /// stored value, so a stored row has to read the same as the enumeration.
    /// </summary>
    public static ValueConverter<DeliveryStatus, string> StatusName { get; } = new(
        status => status.ToString(),
        name => Enum.Parse<DeliveryStatus>(name));

    /// <summary>
    /// The scheme travels as its stable name, so a stored source keeps working when the
    /// implementation behind the name changes.
    /// </summary>
    public static ValueConverter<SignatureScheme, string> SchemeName { get; } = new(
        scheme => scheme.Name,
        name => SignatureScheme.FromName(name));

    /// <summary>
    /// Header lookup is case-insensitive by HTTP semantics, and the domain relies on it, so the
    /// comparer survives the round trip through the database.
    /// </summary>
    private static Dictionary<string, string> Rehydrate(Dictionary<string, string>? headers) =>
        headers is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);

    private static bool HeadersEqual(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right) =>
        ReferenceEquals(left, right)
        || (left is not null
            && right is not null
            && left.Count == right.Count
            && left.All(pair => right.TryGetValue(pair.Key, out var value)
                && string.Equals(value, pair.Value, StringComparison.Ordinal)));

    private static int HeadersHash(IReadOnlyDictionary<string, string> headers)
    {
        var hash = new HashCode();

        foreach (var (key, value) in headers.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
