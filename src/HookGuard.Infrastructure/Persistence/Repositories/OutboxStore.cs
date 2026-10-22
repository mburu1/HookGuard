using HookGuard.Application.Abstractions;
using HookGuard.Domain.Outbox;
using Microsoft.EntityFrameworkCore;

namespace HookGuard.Infrastructure.Persistence.Repositories;

public sealed class OutboxStore(HookGuardDbContext context) : IOutboxStore
{
    private readonly HookGuardDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>
    /// Reads the backlog off <c>ix_outbox_messages_unpublished</c>, which only covers rows where
    /// <c>published_at IS NULL</c>, so the query's cost follows the backlog rather than the history.
    /// The id breaks ties between messages written in the same instant, keeping the order stable
    /// when <c>created_at</c> is not enough to decide it.
    /// </summary>
    public async Task<IReadOnlyList<OutboxMessage>> ReadUnpublishedAsync(
        int batchSize,
        CancellationToken cancellationToken) =>
        await _context.OutboxMessages
            .Where(message => message.PublishedAt == null)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// The messages were read through this context, so they are already tracked and the domain's
    /// <c>MarkPublished</c> is all that had to happen to them. Nothing here filters on
    /// <c>published_at</c> being null: the first confirmed publish is the one that counts, and the
    /// domain keeps it that way.
    /// </summary>
    public async Task MarkPublishedAsync(
        IReadOnlyList<OutboxMessage> messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (messages.Count == 0)
        {
            return;
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
