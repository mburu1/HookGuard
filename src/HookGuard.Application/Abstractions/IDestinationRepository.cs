using HookGuard.Domain.Destinations;

namespace HookGuard.Application.Abstractions;

/// <summary>
/// Reads the configured destinations. Ingest fans an accepted event out to exactly the active
/// destinations of its source, so listing them for a source is all it requires.
/// </summary>
public interface IDestinationRepository
{
    /// <returns>
    /// Every destination of the source that is still accepting deliveries; a deactivated one is
    /// excluded so it is never handed new work.
    /// </returns>
    Task<IReadOnlyList<Destination>> ListActiveAsync(Guid sourceId, CancellationToken cancellationToken);
}
