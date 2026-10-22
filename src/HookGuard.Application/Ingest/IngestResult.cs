using HookGuard.Domain.Signatures;

namespace HookGuard.Application.Ingest;

/// <summary>
/// The outcome of an ingest attempt. <see cref="Failure"/> is carried so the API can say *why* a
/// request was rejected without deciding the reason itself.
/// </summary>
public sealed record IngestResult(IngestStatus Status, Guid? EventId = null, SignatureFailure? Failure = null)
{
    public static IngestResult Accepted(Guid eventId) => new(IngestStatus.Accepted, eventId);

    public static IngestResult Duplicate() => new(IngestStatus.Duplicate);

    public static IngestResult UnknownSource() => new(IngestStatus.UnknownSource);

    public static IngestResult Rejected(SignatureFailure failure) => new(IngestStatus.Rejected, Failure: failure);
}
