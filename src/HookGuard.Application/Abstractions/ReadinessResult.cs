namespace HookGuard.Application.Abstractions;

/// <summary>
/// Outcome of a readiness check for a single external dependency.
/// </summary>
public readonly record struct ReadinessResult(bool IsHealthy, string? Detail)
{
    public static ReadinessResult Healthy() => new(true, null);

    public static ReadinessResult Unhealthy(string detail) => new(false, detail);
}
