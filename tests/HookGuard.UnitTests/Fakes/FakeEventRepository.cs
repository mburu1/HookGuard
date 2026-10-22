using HookGuard.Application.Abstractions;
using HookGuard.Application.Ingest;
using HookGuard.Domain.Deliveries;
using HookGuard.Domain.Events;
using HookGuard.Domain.Outbox;

namespace HookGuard.UnitTests.Fakes;

/// <summary>
/// In-memory stand-in for the ingest commit that keeps the same guarantee as the unique index: the
/// second event with the same <c>(source_id, dedupe_key)</c> is refused, and a refused commit stores
/// none of its rows. <see cref="Attempts"/> counts every call, including refused ones, so a test can
/// prove a rejected request never reached the repository at all.
/// </summary>
internal sealed class FakeEventRepository : IEventRepository
{
    private readonly HashSet<(Guid SourceId, string DedupeKey)> _keys = [];

    public List<Event> Stored { get; } = [];

    public List<Delivery> Deliveries { get; } = [];

    public List<OutboxMessage> Outbox { get; } = [];

    public int Attempts { get; private set; }

    public Task<bool> TryAddAsync(IngestCommit commit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);

        Attempts++;

        if (!_keys.Add((commit.Event.SourceId, commit.Event.DedupeKey)))
        {
            return Task.FromResult(false);
        }

        Stored.Add(commit.Event);
        Deliveries.AddRange(commit.Deliveries);
        Outbox.AddRange(commit.Outbox);

        return Task.FromResult(true);
    }
}
