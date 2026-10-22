namespace HookGuard.Application.Deliveries;

/// <summary>
/// What one attempt to reach a destination produced. A refusal, a timeout and an unreachable host are
/// all outcomes here rather than exceptions, because the retry policy acts on the delivery row and
/// every attempt has to end in something recorded on it.
/// </summary>
/// <param name="StatusCode">What the destination answered, when it answered at all.</param>
/// <param name="Error">Why the attempt did not succeed, phrased for whoever reads the delivery row.</param>
public readonly record struct DeliveryResult(bool Succeeded, int? StatusCode, string? Error)
{
    /// <summary>The destination answered <c>2xx</c>.</summary>
    public static DeliveryResult Accepted(int statusCode) => new(true, statusCode, null);

    /// <summary>The destination answered, and the answer was not <c>2xx</c>.</summary>
    public static DeliveryResult Refused(int statusCode) =>
        new(false, statusCode, $"The destination answered HTTP {statusCode}.");

    /// <summary>The attempt never reached a verdict: a timeout, or a host that could not be reached.</summary>
    public static DeliveryResult Unreachable(string error) => new(false, StatusCode: null, error);
}
