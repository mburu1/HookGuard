namespace HookGuard.Domain.Destinations;

/// <summary>
/// An endpoint a source's events are forwarded to, together with the policy for getting them there:
/// the secret HookGuard signs with, how many attempts are allowed and how long each may take.
/// </summary>
public sealed class Destination
{
    /// <summary>Below this an attempt fails before a healthy endpoint can realistically answer.</summary>
    public static readonly TimeSpan MinimumTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Above this a single slow destination holds a worker out of the pool for too long.</summary>
    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromMinutes(2);

    public const int MaximumAttempts = 100;

    private Destination()
    {
        // Materialised by EF Core.
    }

    private Destination(
        Guid id,
        Guid sourceId,
        string url,
        string signingSecret,
        int maxAttempts,
        TimeSpan timeout,
        bool isActive)
    {
        Id = id;
        SourceId = sourceId;
        Url = url;
        SigningSecret = signingSecret;
        MaxAttempts = maxAttempts;
        Timeout = timeout;
        IsActive = isActive;
    }

    /// <param name="url">
    /// Absolute <c>http</c> or <c>https</c>. Validated here rather than at delivery time, so a
    /// destination that could never be reached is rejected when it is configured.
    /// </param>
    public static Destination Configure(
        Guid id,
        Guid sourceId,
        string url,
        string signingSecret,
        int maxAttempts,
        TimeSpan timeout,
        bool isActive = true)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Destination id is required.", nameof(id));
        }

        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Source id is required.", nameof(sourceId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Destination url must be an absolute http or https url.", nameof(url));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(signingSecret);

        if (signingSecret.Length < 16)
        {
            throw new ArgumentException("Signing secret must be at least 16 characters.", nameof(signingSecret));
        }

        if (maxAttempts < 1 || maxAttempts > MaximumAttempts)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAttempts),
                maxAttempts,
                $"Max attempts must be between 1 and {MaximumAttempts}.");
        }

        if (timeout < MinimumTimeout || timeout > MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                $"Timeout must be between {MinimumTimeout} and {MaximumTimeout}.");
        }

        return new Destination(id, sourceId, url, signingSecret, maxAttempts, timeout, isActive);
    }

    public Guid Id { get; private set; }

    public Guid SourceId { get; private set; }

    public string Url { get; private set; } = string.Empty;

    /// <summary>
    /// In-memory plaintext secret. Encryption at rest is an Infrastructure concern
    /// (<c>signing_secret_enc</c>), so this type never sees ciphertext.
    /// </summary>
    public string SigningSecret { get; private set; } = string.Empty;

    /// <summary>Total attempts allowed before a delivery is dead-lettered.</summary>
    public int MaxAttempts { get; private set; } = 1;

    public TimeSpan Timeout { get; private set; } = MinimumTimeout;

    /// <summary>
    /// A deactivated destination stops receiving new deliveries but keeps the history of the ones
    /// it already has, so it is a flag rather than a delete.
    /// </summary>
    public bool IsActive { get; private set; } = true;
}
