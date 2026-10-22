using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HookGuard.Domain.Time;

namespace HookGuard.Domain.Signatures;

/// <summary>
/// Verifies that a callback was signed by the holder of a source's secret and that it was signed
/// recently enough to be trustworthy. Pure domain logic: no HTTP, no database, no configuration.
/// </summary>
public sealed class SignatureVerifier(IClock clock)
{
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    private static readonly long MinimumUnixSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();
    private static readonly long MaximumUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    /// <summary>
    /// Checks the signature against the raw request bytes, then the timestamp against the source's
    /// tolerance window.
    /// </summary>
    /// <param name="headers">Must be case-insensitive, matching HTTP header semantics.</param>
    /// <param name="body">
    /// Raw bytes exactly as received. The signature covers these bytes, so a parsed and
    /// re-serialised payload will not verify.
    /// </param>
    public SignatureVerification Verify(
        string signingSecret,
        IReadOnlyDictionary<string, string> headers,
        ReadOnlyMemory<byte> body,
        TimeSpan tolerance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signingSecret);
        ArgumentNullException.ThrowIfNull(headers);

        if (!headers.TryGetValue(SignatureHeaders.Timestamp, out var timestamp)
            || string.IsNullOrWhiteSpace(timestamp))
        {
            return SignatureVerification.Rejected(SignatureFailure.MissingTimestamp);
        }

        if (!headers.TryGetValue(SignatureHeaders.Signature, out var provided)
            || string.IsNullOrWhiteSpace(provided))
        {
            return SignatureVerification.Rejected(SignatureFailure.MissingSignature);
        }

        // Authenticity first: an unsigned or wrongly signed request is rejected without letting the
        // caller learn anything about the timestamp window.
        if (!TryDecode(provided, out var supplied))
        {
            return SignatureVerification.Rejected(SignatureFailure.MalformedSignature);
        }

        // Fixed-time comparison: a byte-by-byte early exit leaks how much of a guessed signature was
        // correct, which turns forgery into a search problem.
        if (!CryptographicOperations.FixedTimeEquals(Compute(signingSecret, timestamp, body), supplied))
        {
            return SignatureVerification.Rejected(SignatureFailure.SignatureMismatch);
        }

        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds)
            || unixSeconds < MinimumUnixSeconds
            || unixSeconds > MaximumUnixSeconds)
        {
            return SignatureVerification.Rejected(SignatureFailure.MalformedTimestamp);
        }

        var skew = _clock.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        return skew.Duration() > tolerance
            ? SignatureVerification.Rejected(SignatureFailure.TimestampOutsideTolerance)
            : SignatureVerification.Valid(skew);
    }

    /// <summary>
    /// Produces the signature a provider (or the delivery worker signing an outgoing request) must
    /// send, so signing and verification can never drift apart.
    /// </summary>
    public static string Sign(string signingSecret, string timestamp, ReadOnlyMemory<byte> body) =>
        Convert.ToHexStringLower(Compute(signingSecret, timestamp, body));

    private static byte[] Compute(string signingSecret, string timestamp, ReadOnlyMemory<byte> body)
    {
        var key = Encoding.UTF8.GetBytes(signingSecret);
        var prefix = Encoding.ASCII.GetBytes($"{timestamp}.");

        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        hmac.AppendData(prefix);
        hmac.AppendData(body.Span);

        return hmac.GetHashAndReset();
    }

    /// <summary>
    /// Hex-decodes a caller-supplied signature. Malformed input is expected on the public ingest
    /// endpoint, so the format check happens here and the comparison stays constant-time.
    /// </summary>
    private static bool TryDecode(string signature, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(signature);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
