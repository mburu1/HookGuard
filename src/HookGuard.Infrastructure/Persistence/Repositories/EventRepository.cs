using HookGuard.Application.Abstractions;
using HookGuard.Domain.Events;
using HookGuard.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HookGuard.Infrastructure.Persistence.Repositories;

public sealed class EventRepository(HookGuardDbContext context) : IEventRepository
{
    private readonly HookGuardDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>
    /// The dedupe decision is made by the database. EF Core has no upsert operator, and hand-writing
    /// the <c>INSERT ... ON CONFLICT DO NOTHING</c> from the README would duplicate the column
    /// mapping and bypass the value converters, so the unique index does the work and its violation
    /// is translated into a plain <see langword="false"/>.
    /// </summary>
    public async Task<bool> TryAddAsync(Event @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        _context.Events.Add(@event);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            // The rejected insert is still tracked as Added; leaving it would make the next
            // SaveChanges on this scope fail for the same reason.
            _context.Entry(@event).State = EntityState.Detached;
            return false;
        }
    }

    private static bool IsDuplicate(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        } postgres
        && postgres.ConstraintName == EventConfiguration.DedupeIndexName;
}
