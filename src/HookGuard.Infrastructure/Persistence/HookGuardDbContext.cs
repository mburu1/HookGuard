using HookGuard.Domain.Events;
using HookGuard.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace HookGuard.Infrastructure.Persistence;

/// <summary>
/// The only place that knows how domain types look as rows. Entity configuration lives in
/// <see cref="Configurations"/> so the DbContext stays a list of what is mapped, not how.
/// </summary>
public sealed class HookGuardDbContext(DbContextOptions<HookGuardDbContext> options) : DbContext(options)
{
    public DbSet<Source> Sources => Set<Source>();

    public DbSet<Event> Events => Set<Event>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HookGuardDbContext).Assembly);
    }
}
