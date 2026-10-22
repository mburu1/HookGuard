namespace HookGuard.Domain.Sources;

/// <summary>
/// A provider integration a tenant has configured: the secret its callbacks are signed with, how
/// long a signature stays valid, and which scheme to use when verifying.
/// </summary>
public sealed class Source
{
    /// <summary>Lower bound that still tolerates a slow mobile network hop from the provider.</summary>
    public static readonly TimeSpan MinimumTolerance = TimeSpan.FromSeconds(5);

    /// <summary>Upper bound; a wider window than this makes replay attacks cheap.</summary>
    public static readonly TimeSpan MaximumTolerance = TimeSpan.FromMinutes(30);

    private Source()
    {
        // Materialised by EF Core.
    }

    public Source(
        Guid id,
        Guid tenantId,
        string name,
        string signingSecret,
        SignatureScheme signatureScheme,
        TimeSpan tolerance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(signingSecret);
        ArgumentNullException.ThrowIfNull(signatureScheme);

        if (id == Guid.Empty)
        {
            throw new ArgumentException("Source id is required.", nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (signingSecret.Length < 16)
        {
            throw new ArgumentException("Signing secret must be at least 16 characters.", nameof(signingSecret));
        }

        if (tolerance < MinimumTolerance || tolerance > MaximumTolerance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tolerance),
                tolerance,
                $"Tolerance must be between {MinimumTolerance} and {MaximumTolerance}.");
        }

        Id = id;
        TenantId = tenantId;
        Name = name.Trim();
        SigningSecret = signingSecret;
        SignatureScheme = signatureScheme;
        Tolerance = tolerance;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// In-memory plaintext secret. Encryption at rest is an Infrastructure concern
    /// (<c>signing_secret_enc</c>), so this type never sees ciphertext.
    /// </summary>
    public string SigningSecret { get; private set; } = string.Empty;

    public SignatureScheme SignatureScheme { get; private set; } = SignatureScheme.TimestampBodyHmacSha256;

    public TimeSpan Tolerance { get; private set; } = MinimumTolerance;
}
