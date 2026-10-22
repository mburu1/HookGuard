using HookGuard.Application.Abstractions;
using HookGuard.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace HookGuard.Infrastructure;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the broker client. The connection string is passed in rather than read from
    /// configuration here, so the composition root stays the one place that knows where settings
    /// come from.
    /// </summary>
    public static IServiceCollection AddHookGuardMessaging(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // A single dispatcher for the whole process, so the connection is opened once and closed
        // when the host stops instead of per message.
        services.AddSingleton<IDispatcher>(_ => new RabbitMqDispatcher(connectionString));

        // Likewise one consumer, which the worker host runs as a single long-lived consume loop.
        services.AddSingleton(_ => new RabbitMqConsumer(connectionString));

        return services;
    }
}
