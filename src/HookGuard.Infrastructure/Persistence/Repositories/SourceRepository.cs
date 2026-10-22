using HookGuard.Application.Abstractions;
using HookGuard.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace HookGuard.Infrastructure.Persistence.Repositories;

public sealed class SourceRepository(HookGuardDbContext context) : ISourceRepository
{
    private readonly HookGuardDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public Task<Source?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        _context.Sources.FirstOrDefaultAsync(source => source.Id == id, cancellationToken);
}
