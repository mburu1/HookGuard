using HookGuard.Application.Abstractions;
using HookGuard.Infrastructure.Health;

var builder = WebApplication.CreateBuilder(args);

// Composition root: one reachability probe per configured dependency. The ingest and management
// endpoints arrive in phases 1 and 3; for now the API exists to prove the plumbing and to be
// probeable by container orchestrators.
foreach (var dependency in builder.Configuration.GetSection("Dependencies").GetChildren())
{
    if (!string.IsNullOrWhiteSpace(dependency.Value))
    {
        builder.Services.AddSingleton<IReadinessProbe>(
            new TcpReadinessProbe(dependency.Key, dependency.Value));
    }
}

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new HealthReport("Healthy", [])));

app.MapGet("/health/ready", async (IServiceProvider services, CancellationToken cancellationToken) =>
{
    var probes = services.GetServices<IReadinessProbe>();
    var dependencies = await Task.WhenAll(
        probes.Select(probe => DescribeAsync(probe, cancellationToken)));

    var report = new HealthReport(
        dependencies.All(dependency => dependency.Healthy) ? "Healthy" : "Unhealthy",
        dependencies);

    return report.Status == "Healthy"
        ? Results.Ok(report)
        : Results.Json(report, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();

static async Task<DependencyReport> DescribeAsync(IReadinessProbe probe, CancellationToken cancellationToken)
{
    var result = await probe.CheckAsync(cancellationToken);
    return new DependencyReport(probe.Name, result.IsHealthy, result.Detail);
}

internal sealed record HealthReport(string Status, IReadOnlyList<DependencyReport> Dependencies);

internal sealed record DependencyReport(string Name, bool Healthy, string? Detail);
