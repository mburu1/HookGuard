using HookGuard.Domain.Retry;

namespace HookGuard.Infrastructure.Randomness;

/// <summary>
/// Production entropy, drawn from the shared generator and therefore thread-safe for every worker in
/// the process.
/// </summary>
public sealed class SystemRandom : IRandom
{
    public int Next(int maxExclusive) => Random.Shared.Next(maxExclusive);
}
