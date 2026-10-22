using System.Security.Cryptography;

namespace HookGuard.Domain.Events;

/// <summary>
/// Derives the key that collapses provider retries of the same callback into one stored event,
/// which is what makes the unique <c>(source_id, dedupe_key)</c> index meaningful.
/// </summary>
public static class EventDedupeKey
{
    /// <summary>Structured providers (M-Pesa <c>TransID</c>, Stripe event id) can send an explicit key here.</summary>
    public const string ExplicitKeyHeader = "X-HookGuard-Event-Id";

    /// <summary>Keeps the key inside the indexable column width.</summary>
    public const int MaxLength = 200;

    /// <param name="headers">Must be case-insensitive, matching HTTP header semantics.</param>
    /// <param name="body">Raw request bytes, exactly as received.</param>
    public static string Resolve(IReadOnlyDictionary<string, string> headers, ReadOnlySpan<byte> body)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (headers.TryGetValue(ExplicitKeyHeader, out var explicitKey)
            && !string.IsNullOrWhiteSpace(explicitKey))
        {
            var trimmed = explicitKey.Trim();
            return trimmed.Length <= MaxLength ? trimmed : trimmed[..MaxLength];
        }

        // No provider-supplied identity, so the payload itself is the identity: an identical retry
        // hashes the same and deduplicates, a genuinely different payload does not.
        return FromBody(body);
    }

    /// <summary>
    /// Identity of a payload for callers that only have the body (tests, the fake merchant).
    /// </summary>
    public static string FromBody(ReadOnlySpan<byte> body) =>
        Convert.ToHexStringLower(SHA256.HashData(body));
}
