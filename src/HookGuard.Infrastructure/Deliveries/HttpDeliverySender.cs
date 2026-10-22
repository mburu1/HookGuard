using System.Globalization;
using System.Net.Http.Headers;
using HookGuard.Application.Abstractions;
using HookGuard.Application.Deliveries;
using HookGuard.Domain.Signatures;
using HookGuard.Domain.Time;

namespace HookGuard.Infrastructure.Deliveries;

/// <summary>
/// Delivers a payload over HTTP, signed so the destination can tell the request really came from
/// HookGuard, and carrying an idempotency key so it can tell a retry from a second intent.
/// </summary>
/// <remarks>
/// <para>
/// A typed client: the <see cref="HttpClient"/> and its pooled handler come from the factory, but the
/// timeout does not. A timeout belongs to the destination and changes from request to request, while
/// <see cref="HttpClient.Timeout"/> is fixed for the lifetime of the client, so the budget is enforced
/// with a linked token instead. The client's own timeout is turned off where it is registered, or it
/// would cut a destination configured for longer than the default.
/// </para>
/// <para>
/// The response body is not read. Nothing here needs it, and reading it would make the attempt depend
/// on a destination finishing its answer.
/// </para>
/// </remarks>
public sealed class HttpDeliverySender(HttpClient client, IClock clock) : IDeliverySender
{
    /// <summary>
    /// What the destination dedupes on: at-least-once delivery means a payload may arrive twice (ADR
    /// 0004), and this is the only thing that lets the destination collapse the two.
    /// </summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly HttpClient _client = client ?? throw new ArgumentNullException(nameof(client));

    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<DeliveryResult> SendAsync(DeliveryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = request.Payload;
        var timestamp = _clock.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        using var content = new ByteArrayContent(payload.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var message = new HttpRequestMessage(HttpMethod.Post, request.DestinationUrl)
        {
            Content = content,
        };

        // Signed over the same bytes that are sent, with the timestamp the verifier on the other side
        // will compare against its tolerance window.
        message.Headers.TryAddWithoutValidation(SignatureHeaders.Timestamp, timestamp);
        message.Headers.TryAddWithoutValidation(
            SignatureHeaders.Signature,
            SignatureVerifier.Sign(request.SigningSecret, timestamp, payload));
        message.Headers.TryAddWithoutValidation(IdempotencyKeyHeader, request.IdempotencyKey);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.Timeout);

        try
        {
            using var response = await _client
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? DeliveryResult.Accepted((int)response.StatusCode)
                : DeliveryResult.Refused((int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The deadline fired without the caller having asked to stop, so the destination ran out
            // of the time it was configured with. That is a failed attempt, not a lost one.
            return DeliveryResult.Unreachable(
                $"The destination did not answer within {request.Timeout.TotalSeconds:0.###} seconds.");
        }
        catch (HttpRequestException exception)
        {
            return DeliveryResult.Unreachable($"The destination could not be reached: {exception.Message}");
        }
    }
}
