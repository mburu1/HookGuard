using System.Text.Json;

namespace HookGuard.Domain.Outbox;

/// <summary>
/// The message the relay puts on the queue and the worker reads back. It names the delivery to
/// process rather than carrying the payload to send, so a message that is redelivered late cannot
/// push stale content to a destination.
/// </summary>
public readonly record struct DeliveryDispatch(Guid DeliveryId)
{
    /// <summary>Discriminator stored in <c>outbox_messages.type</c>; other types arrive with replay.</summary>
    public const string MessageType = "delivery.dispatch";

    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    public static DeliveryDispatch For(Guid deliveryId) => new(deliveryId);

    public string ToPayload() => JsonSerializer.Serialize(this, PayloadJson);

    public static DeliveryDispatch FromPayload(string payload) =>
        JsonSerializer.Deserialize<DeliveryDispatch>(payload, PayloadJson);
}
