using HookGuard.Domain.Deliveries;
using HookGuard.Domain.Destinations;
using HookGuard.Domain.Events;

namespace HookGuard.Application.Deliveries;

/// <summary>
/// A delivery this worker has taken responsibility for, together with the two rows an attempt needs:
/// the event whose payload is sent, and the destination it is sent to. They are read as one claim
/// because a delivery means nothing without both of them.
/// </summary>
public sealed record ClaimedDelivery(Delivery Delivery, Event Event, Destination Destination);
