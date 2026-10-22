using HookGuard.Application.Ingest;

namespace HookGuard.Api.Endpoints;

/// <summary>
/// The provider-facing ingest route. It is the one endpoint an unauthenticated caller may reach,
/// and the one place a request body is read as raw bytes rather than bound to a model.
/// </summary>
internal static class IngestEndpoint
{
    public static IEndpointRouteBuilder MapIngestEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/hooks/{sourceId:guid}", HandleAsync)
            .WithName("IngestEvent")
            .WithSummary("Accept a signed provider callback")
            .WithDescription(
                "Verifies the HMAC signature over the raw request body, then stores the event. "
                + "Returns 202 for a new event and 200 for a duplicate of one already stored.");

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        Guid sourceId,
        HttpRequest request,
        IngestEventHandler handler,
        CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(request, cancellationToken).ConfigureAwait(false);

        // Checked here rather than in the use case: an empty body is a malformed request, not a
        // decision about the event, and it is the only input that could otherwise reach the domain
        // as a violation and surface as a 500.
        if (body.IsEmpty)
        {
            return Results.Problem(
                title: "Empty payload",
                detail: "A callback must carry a body; there is nothing to verify or store.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await handler
            .HandleAsync(sourceId, ReadHeaders(request), body, cancellationToken)
            .ConfigureAwait(false);

        return Respond(result, sourceId);
    }

    private static IResult Respond(IngestResult result, Guid sourceId) => result.Status switch
    {
        IngestStatus.Accepted => Results.Json(
            new IngestResponse(EventId: result.EventId, Duplicate: false),
            statusCode: StatusCodes.Status202Accepted),

        // A duplicate is a success: the provider's retry did its job and must stop retrying. The
        // event is acknowledged without being processed again.
        IngestStatus.Duplicate => Results.Json(
            new IngestResponse(EventId: null, Duplicate: true),
            statusCode: StatusCodes.Status200OK),

        IngestStatus.UnknownSource => Results.Problem(
            title: "Unknown source",
            detail: $"No source '{sourceId}' is configured, so there is no secret to verify against.",
            statusCode: StatusCodes.Status404NotFound),

        IngestStatus.Rejected => Results.Problem(
            title: "Signature verification failed",
            detail: "The request did not prove it was signed by the source's secret.",
            statusCode: StatusCodes.Status401Unauthorized,
            extensions: new Dictionary<string, object?> { ["failure"] = result.Failure?.ToString() }),

        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };

    /// <summary>
    /// Reads the body exactly as it arrived. The signature covers these bytes, so the payload is
    /// never parsed and re-serialised on the way in.
    /// </summary>
    private static async Task<ReadOnlyMemory<byte>> ReadBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <summary>
    /// HTTP header names are case-insensitive, so the dictionary handed to the domain matches its
    /// lookup semantics.
    /// </summary>
    private static Dictionary<string, string> ReadHeaders(HttpRequest request) =>
        request.Headers.ToDictionary(
            header => header.Key,
            header => header.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Ingest acknowledgement. <c>duplicate</c> mirrors the provider-facing contract so a client can
/// tell "stored" from "already stored" without inspecting the status code alone.
/// </summary>
internal sealed record IngestResponse(Guid? EventId, bool Duplicate);
