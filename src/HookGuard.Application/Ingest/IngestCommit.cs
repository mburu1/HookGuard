using HookGuard.Domain.Deliveries;
using HookGuard.Domain.Events;
using HookGuard.Domain.Outbox;

namespace HookGuard.Application.Ingest;

/// <summary>
/// Everything one accepted callback commits, in a single transaction: the event, one delivery per
/// active destination, and one outbox message announcing each delivery. These are one unit because a
/// delivery without its message would never be attempted, and a message without its delivery would
/// name a row that does not exist.
/// </summary>
public sealed record IngestCommit(
    Event Event,
    IReadOnlyList<Delivery> Deliveries,
    IReadOnlyList<OutboxMessage> Outbox);
