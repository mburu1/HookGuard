namespace HookGuard.Domain.Sources;

/// <summary>
/// How a provider proves that a callback is authentic. Stored per source so that adding a provider
/// preset (Stripe's <c>Stripe-Signature</c>, Flutterwave's <c>verif-hash</c>) is a data change
/// rather than a code path.
/// </summary>
public sealed record SignatureScheme
{
    /// <summary>
    /// HMAC-SHA256 over <c>timestamp.body</c>, hex encoded in lowercase, carried by the
    /// <c>X-HookGuard-Timestamp</c> and <c>X-HookGuard-Signature</c> headers.
    /// </summary>
    public static readonly SignatureScheme TimestampBodyHmacSha256 = new("timestamp-body-hmac-sha256");

    private SignatureScheme(string name) => Name = name;

    public string Name { get; }

    public static IReadOnlyList<SignatureScheme> Known { get; } = [TimestampBodyHmacSha256];

    public static SignatureScheme FromName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Known.FirstOrDefault(scheme => string.Equals(scheme.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown signature scheme.");
    }

    public override string ToString() => Name;
}
