namespace HookGuard.Domain.Retry;

/// <summary>
/// Abstracts entropy so that a jittered delay is exact in a test rather than merely plausible. Kept
/// as narrow as the retry policy needs instead of exposing a general-purpose number generator.
/// </summary>
public interface IRandom
{
    /// <param name="maxExclusive">Upper bound, which the result never reaches.</param>
    /// <returns>An integer in <c>[0, maxExclusive)</c>.</returns>
    int Next(int maxExclusive);
}
