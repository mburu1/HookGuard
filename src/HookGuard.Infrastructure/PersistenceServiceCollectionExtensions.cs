using HookGuard.Application.Abstractions;
using HookGuard.Infrastructure.Persistence;
using HookGuard.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HookGuard.Infrastructure;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the database context. The connection string is passed in rather than read from
    /// configuration here, so the composition root stays the one place that knows where settings
    /// come from.
    /// </summary>
    public static IServiceCollection AddHookGuardPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<HookGuardDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<ISourceRepository, SourceRepository>();
        services.AddScoped<IEventRepository, EventRepository>();

        return services;
    }
}
