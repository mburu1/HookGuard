using HookGuard.Domain.Retry;

namespace HookGuard.UnitTests.Fakes;

/// <summary>
/// Entropy under test control. The value it is told to return is the delay the policy settles on, and
/// the bound it was asked for is recorded, so a jittered window is still exact in a test.
/// </summary>
internal sealed class FakeRandom(int value = 0) : IRandom
{
    /// <summary>
    /// The exclusive upper bound of the last draw, or <see langword="null"/> if nothing was drawn.
    /// </summary>
    public int? LastMaxExclusive { get; private set; }

    public int Next(int maxExclusive)
    {
        LastMaxExclusive = maxExclusive;

        return value;
    }
}
