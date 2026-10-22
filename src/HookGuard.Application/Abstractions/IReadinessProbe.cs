namespace HookGuard.Application.Abstractions;

/// <summary>
/// Reports whether an infrastructure dependency HookGuard cannot work without is reachable.
/// Implemented in the Infrastructure layer, aggregated by the readiness endpoint.
/// </summary>
public interface IReadinessProbe
{
    /// <summary>Human-readable dependency name, for example <c>Postgres</c>.</summary>
    string Name { get; }

    Task<ReadinessResult> CheckAsync(CancellationToken cancellationToken);
}
