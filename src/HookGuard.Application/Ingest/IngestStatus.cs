namespace HookGuard.Application.Ingest;

/// <summary>What the ingest use case decided, in provider-neutral terms.</summary>
public enum IngestStatus
{
    /// <summary>Stored, and new. The provider is told to stop retrying.</summary>
    Accepted,

    /// <summary>Already stored under the same dedupe key; acknowledged but not re-processed.</summary>
    Duplicate,

    /// <summary>No source with that id, so there is no secret to verify against.</summary>
    UnknownSource,

    /// <summary>The request did not prove it came from the source.</summary>
    Rejected,
}
