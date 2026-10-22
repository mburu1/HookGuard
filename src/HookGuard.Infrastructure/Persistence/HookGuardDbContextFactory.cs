using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HookGuard.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build a context without booting an application host, so migrations can be
/// added and reviewed with the startup project set to this assembly alone. The connection string is
/// only ever used to *generate* SQL; applying a migration uses the host's configuration.
/// </summary>
public sealed class HookGuardDbContextFactory : IDesignTimeDbContextFactory<HookGuardDbContext>
{
    private const string LocalDevelopment =
        "Host=localhost;Port=5432;Database=hookguard;Username=hookguard;Password=hookguard";

    public HookGuardDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("HOOKGUARD_CONNECTION") ?? LocalDevelopment;

        var options = new DbContextOptionsBuilder<HookGuardDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new HookGuardDbContext(options);
    }
}
