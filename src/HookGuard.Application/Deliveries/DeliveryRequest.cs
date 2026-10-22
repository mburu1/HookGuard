namespace HookGuard.Application.Deliveries;

/// <summary>
/// One outgoing attempt, described without reference to how it is carried: where it goes, what it is
/// signed with, and how much time it is allowed. Keeping the transport out of this keeps the sender
/// replaceable and the attempt testable.
/// </summary>
/// <param name="IdempotencyKey">
/// Stable for the life of the delivery, so a retry after a lost response carries the same key and the
/// destination can recognise it as the same intent rather than a second one.
/// </param>
/// <param name="Payload">
/// The bytes that were received and signed, unchanged. The destination verifies the signature over
/// exactly these, so re-serialising the payload would break every destination that checks it.
/// </param>
public sealed record DeliveryRequest(
    string IdempotencyKey,
    string DestinationUrl,
    string SigningSecret,
    TimeSpan Timeout,
    ReadOnlyMemory<byte> Payload);
