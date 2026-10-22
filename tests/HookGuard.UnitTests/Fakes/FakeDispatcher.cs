using HookGuard.Application.Abstractions;
using HookGuard.Domain.Outbox;

namespace HookGuard.UnitTests.Fakes;

/// <summary>
/// Stands in for the broker. <see cref="Confirm"/> decides whether a publish is confirmed, so a test
/// can make one message fail the way an unroutable publish does; a refused message is not recorded,
/// because the real dispatcher only returns once the broker has taken the message.
/// </summary>
internal sealed class FakeDispatcher : IDispatcher
{
    public List<OutboxMessage> Dispatched { get; } = [];

    public Func<OutboxMessage, bool> Confirm { get; set; } = _ => true;

    public Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!Confirm(message))
        {
            throw new InvalidOperationException($"The broker did not confirm message {message.Id}.");
        }

        Dispatched.Add(message);

        return Task.CompletedTask;
    }
}
