namespace HookGuard.Infrastructure.Messaging;

/// <summary>
/// A delivery taken off the work queue. The body is a copy rather than a view over the channel's
/// buffer, because that buffer is only promised to be valid until the consumer's handler returns and
/// the handler is allowed to take as long as it needs.
/// </summary>
/// <param name="MessageId">The outbox message that produced this delivery, for correlating a log line.</param>
/// <param name="Body">The dispatch payload, as it was published.</param>
public sealed record QueueMessage(string? MessageId, ReadOnlyMemory<byte> Body);
