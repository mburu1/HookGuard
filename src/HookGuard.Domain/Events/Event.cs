namespace HookGuard.Domain.Events;

/// <summary>
/// A callback HookGuard accepted and stored. The payload is kept as the raw bytes that were
/// signed, because a re-serialised payload no longer matches the provider's signature.
/// </summary>
public sealed class Event
{
    private Event()
    {
        // Materialised by EF Core.
    }

    private Event(
        Guid id,
        Guid sourceId,
        string dedupeKey,
        IReadOnlyDictionary<string, string> headers,
        ReadOnlyMemory<byte> payload,
        DateTimeOffset receivedAt,
        bool signatureValid)
    {
        Id = id;
        SourceId = sourceId;
        DedupeKey = dedupeKey;
        Headers = headers;
        Payload = payload;
        ReceivedAt = receivedAt;
        SignatureValid = signatureValid;
    }

    /// <param name="id">
    /// Caller-supplied so the id can be a UUIDv7 generated at the edge of the system, giving
    /// index locality on insert without a database round trip.
    /// </param>
    public static Event Record(
        Guid id,
        Guid sourceId,
        string dedupeKey,
        IReadOnlyDictionary<string, string> headers,
        ReadOnlyMemory<byte> payload,
        DateTimeOffset receivedAt,
        bool signatureValid)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Event id is required.", nameof(id));
        }

        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Source id is required.", nameof(sourceId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(dedupeKey);
        ArgumentNullException.ThrowIfNull(headers);

        if (dedupeKey.Length > EventDedupeKey.MaxLength)
        {
            throw new ArgumentException(
                $"Dedupe key must not exceed {EventDedupeKey.MaxLength} characters.",
                nameof(dedupeKey));
        }

        if (payload.IsEmpty)
        {
            throw new ArgumentException("Payload is required.", nameof(payload));
        }

        return new Event(id, sourceId, dedupeKey, headers, payload, receivedAt, signatureValid);
    }

    public Guid Id { get; private set; }

    public Guid SourceId { get; private set; }

    /// <summary>Unique per source; see <see cref="EventDedupeKey"/>.</summary>
    public string DedupeKey { get; private set; } = string.Empty;

    public IReadOnlyDictionary<string, string> Headers { get; private set; } =
        new Dictionary<string, string>();

    /// <summary>Raw request bytes, stored as <c>bytea</c> for signature fidelity.</summary>
    public ReadOnlyMemory<byte> Payload { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>
    /// Outcome of signature verification, persisted for audit. Ingest rejects invalid signatures
    /// before this method is reached, so stored events are always valid.
    /// </summary>
    public bool SignatureValid { get; private set; }
}
