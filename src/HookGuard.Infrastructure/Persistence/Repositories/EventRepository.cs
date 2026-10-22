using HookGuard.Application.Abstractions;
using HookGuard.Application.Ingest;
using HookGuard.Domain.Events;
using HookGuard.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HookGuard.Infrastructure.Persistence.Repositories;

public sealed class EventRepository(HookGuardDbContext context) : IEventRepository
{
    private readonly HookGuardDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>
    /// The event, its deliveries and their outbox messages go in as one <c>SaveChanges</c>, which EF
    /// Core wraps in a transaction. That makes the dedupe decision and the fan-out atomic: there is
    /// no moment where an event exists without the work to deliver it.
    /// </summary>
    /// <remarks>
    /// The dedupe decision is made by the database. EF Core has no upsert operator, and hand-writing
    /// the <c>INSERT ... ON CONFLICT DO NOTHING</c> from the README would duplicate the column
    /// mapping and bypass the value converters, so the unique index does the work and its violation
    /// is translated into a plain <see langword="false"/>.
    /// </remarks>
    public async Task<bool> TryAddAsync(IngestCommit commit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);

        _context.Events.Add(commit.Event);
        _context.Deliveries.AddRange(commit.Deliveries);
        _context.OutboxMessages.AddRange(commit.Outbox);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            Detach(commit);
            return false;
        }
    }

    /// <summary>
    /// The rejected rows are still tracked as Added; leaving them would make the next
    /// <c>SaveChanges</c> on this scope insert a partial version of the same commit.
    /// </summary>
    private void Detach(IngestCommit commit)
    {
        _context.Entry(commit.Event).State = EntityState.Detached;

        foreach (var delivery in commit.Deliveries)
        {
            _context.Entry(delivery).State = EntityState.Detached;
        }

        foreach (var message in commit.Outbox)
        {
            _context.Entry(message).State = EntityState.Detached;
        }
    }

    private static bool IsDuplicate(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        } postgres
        && postgres.ConstraintName == EventConfiguration.DedupeIndexName;
}
