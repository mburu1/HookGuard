using HookGuard.Application.Abstractions;
using HookGuard.Domain.Events;

namespace HookGuard.UnitTests.Fakes;

/// <summary>
/// In-memory stand-in for the events table that keeps the same guarantee as the unique index: the
/// second event with the same <c>(source_id, dedupe_key)</c> is refused. <see cref="Attempts"/>
/// counts every call, including refused ones, so a test can prove a rejected request never reached
/// the repository at all.
/// </summary>
internal sealed class FakeEventRepository : IEventRepository
{
    private readonly HashSet<(Guid SourceId, string DedupeKey)> _keys = [];

    public List<Event> Stored { get; } = [];

    public int Attempts { get; private set; }

    public Task<bool> TryAddAsync(Event @event, CancellationToken cancellationToken)
    {
        Attempts++;

        if (!_keys.Add((@event.SourceId, @event.DedupeKey)))
        {
            return Task.FromResult(false);
        }

        Stored.Add(@event);
        return Task.FromResult(true);
    }
}
