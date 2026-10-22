using HookGuard.Application.Ingest;

namespace HookGuard.Application.Abstractions;

public interface IEventRepository
{
    /// <summary>
    /// Commits the event together with the deliveries it fans out to and the outbox messages that
    /// announce them — all of it or none of it. Storing the event and queueing its deliveries in
    /// separate transactions is the dual-write problem: a crash between them loses the event
    /// silently.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the event was stored; <see langword="false"/> when it was a
    /// duplicate and nothing was committed. The decision is taken by the database, not by a prior
    /// read, so concurrent deliveries of the same event cannot both return <see langword="true"/>.
    /// </returns>
    Task<bool> TryAddAsync(IngestCommit commit, CancellationToken cancellationToken);
}
