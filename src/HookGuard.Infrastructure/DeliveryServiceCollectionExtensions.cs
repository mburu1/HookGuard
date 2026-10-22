using HookGuard.Application.Abstractions;
using HookGuard.Infrastructure.Deliveries;
using Microsoft.Extensions.DependencyInjection;

namespace HookGuard.Infrastructure;

public static class DeliveryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the outbound client used to attempt deliveries. The client's own timeout is switched
    /// off here because each attempt is bounded by the timeout its destination was configured with,
    /// which the sender applies per request.
    /// </summary>
    public static IServiceCollection AddHookGuardDelivery(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient<IDeliverySender, HttpDeliverySender>(
            client => client.Timeout = Timeout.InfiniteTimeSpan);

        return services;
    }
}
