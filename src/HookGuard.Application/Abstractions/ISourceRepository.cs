using HookGuard.Domain.Sources;

namespace HookGuard.Application.Abstractions;

/// <summary>
/// Reads the configured sources. Ingest needs the signing secret and tolerance of exactly one
/// source per request, so lookup by id is the only operation it requires.
/// </summary>
public interface ISourceRepository
{
    Task<Source?> FindAsync(Guid id, CancellationToken cancellationToken);
}
