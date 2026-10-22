namespace HookGuard.Domain.Signatures;

/// <summary>
/// Outcome of verifying one request. <paramref name="Skew"/> is how long ago the payload was
/// signed (negative when the provider's clock is ahead), which is worth recording on the attempt so
/// a provider with a drifting clock can be spotted before its callbacks start failing.
/// </summary>
public sealed record SignatureVerification(bool IsValid, SignatureFailure Failure, TimeSpan? Skew)
{
    public static SignatureVerification Valid(TimeSpan skew) => new(true, SignatureFailure.None, skew);

    public static SignatureVerification Rejected(SignatureFailure failure) => new(false, failure, null);
}
