using HookGuard.Domain.Events;

namespace HookGuard.Application.Abstractions;

public interface IEventRepository
{
    /// <summary>
    /// Stores the event, or reports that an event with the same dedupe key already exists for the
    /// source.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the event was stored; <see langword="false"/> when it was a
    /// duplicate and nothing changed. The decision is taken by the database, not by a prior read, so
    /// concurrent deliveries of the same event cannot both return <see langword="true"/>.
    /// </returns>
    Task<bool> TryAddAsync(Event @event, CancellationToken cancellationToken);
}
