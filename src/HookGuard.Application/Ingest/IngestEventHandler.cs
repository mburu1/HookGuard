using HookGuard.Application.Abstractions;
using HookGuard.Domain.Events;
using HookGuard.Domain.Signatures;
using HookGuard.Domain.Time;

namespace HookGuard.Application.Ingest;

/// <summary>
/// The hot path. Verifies authenticity and records the event before the provider is told the
/// callback was received: acknowledging first would create a window where a crash loses the event
/// while the provider believes it was delivered.
/// </summary>
public sealed class IngestEventHandler(
    ISourceRepository sources,
    IEventRepository events,
    SignatureVerifier verifier,
    IClock clock)
{
    private readonly ISourceRepository _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly IEventRepository _events = events ?? throw new ArgumentNullException(nameof(events));
    private readonly SignatureVerifier _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <param name="headers">Case-insensitive, as HTTP headers are.</param>
    /// <param name="body">
    /// The bytes exactly as received. The signature covers them, so anything read back out of this
    /// method must be the same array; re-serialising a parsed payload would break verification for
    /// every provider that signs the wire format.
    /// </param>
    public async Task<IngestResult> HandleAsync(
        Guid sourceId,
        IReadOnlyDictionary<string, string> headers,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var source = await _sources.FindAsync(sourceId, cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            return IngestResult.UnknownSource();
        }

        // Everything after this point is reached only by a request that proved it holds the
        // source's secret, so no database write can be triggered by an unauthenticated caller.
        var verification = _verifier.Verify(source.SigningSecret, headers, body, source.Tolerance);
        if (!verification.IsValid)
        {
            return IngestResult.Rejected(verification.Failure);
        }

        var @event = Event.Record(
            Guid.CreateVersion7(),
            source.Id,
            EventDedupeKey.Resolve(headers, body.Span),
            headers,
            body,
            _clock.UtcNow,
            signatureValid: true);

        return await _events.TryAddAsync(@event, cancellationToken).ConfigureAwait(false)
            ? IngestResult.Accepted(@event.Id)
            : IngestResult.Duplicate();
    }
}
