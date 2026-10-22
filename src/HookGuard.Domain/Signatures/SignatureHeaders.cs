namespace HookGuard.Domain.Signatures;

/// <summary>
/// Header names used by HookGuard's own signature scheme. Providers do not use these; the ingest
/// layer maps a provider's headers onto them.
/// </summary>
public static class SignatureHeaders
{
    /// <summary>Unix time in seconds at which the payload was signed.</summary>
    public const string Timestamp = "X-HookGuard-Timestamp";

    /// <summary>Lowercase hex HMAC-SHA256 over <c>timestamp.body</c>.</summary>
    public const string Signature = "X-HookGuard-Signature";
}
