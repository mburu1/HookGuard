using HookGuard.Application.Abstractions;
using HookGuard.Domain.Destinations;
using Microsoft.EntityFrameworkCore;

namespace HookGuard.Infrastructure.Persistence.Repositories;

public sealed class DestinationRepository(HookGuardDbContext context) : IDestinationRepository
{
    private readonly HookGuardDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <remarks>
    /// Ordered by id, which is a UUIDv7 and therefore by configuration order. The fan-out takes the
    /// order it is given, so a deterministic order here makes a test of the fan-out deterministic
    /// too.
    /// </remarks>
    public async Task<IReadOnlyList<Destination>> ListActiveAsync(
        Guid sourceId, CancellationToken cancellationToken) =>
        await _context.Destinations
            .Where(destination => destination.SourceId == sourceId && destination.IsActive)
            .OrderBy(destination => destination.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
