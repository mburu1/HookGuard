using HookGuard.Application.Abstractions;
using HookGuard.Domain.Outbox;

namespace HookGuard.UnitTests.Fakes;

/// <summary>
/// In-memory outbox that keeps the guarantee of the partial index it stands in for: only rows where
/// <c>published_at IS NULL</c> are ever handed to the relay. The messages themselves are shared with
/// the test, so a confirmation recorded by the relay is visible as published state.
/// </summary>
internal sealed class FakeOutboxStore(params OutboxMessage[] messages) : IOutboxStore
{
    public IReadOnlyList<OutboxMessage> Messages { get; } = messages;

    /// <summary>Every call to <see cref="MarkPublishedAsync"/>, in order.</summary>
    public List<IReadOnlyList<OutboxMessage>> SavedBatches { get; } = [];

    public Task<IReadOnlyList<OutboxMessage>> ReadUnpublishedAsync(
        int batchSize,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OutboxMessage>>(
        [
            .. Messages
                .Where(message => message.PublishedAt is null)
                .OrderBy(message => message.CreatedAt)
                .ThenBy(message => message.Id)
                .Take(batchSize),
        ]);

    public Task MarkPublishedAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken cancellationToken)
    {
        SavedBatches.Add(messages);
        return Task.CompletedTask;
    }
}
