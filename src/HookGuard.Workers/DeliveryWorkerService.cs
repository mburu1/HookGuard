using System.Text;
using System.Text.Json;
using HookGuard.Application.Deliveries;
using HookGuard.Domain.Outbox;
using HookGuard.Infrastructure.Messaging;

namespace HookGuard.Workers;

/// <summary>
/// Runs the consume loop that turns queued dispatches into delivery attempts.
/// </summary>
/// <remarks>
/// The queue is a nudge, not the state. The delivery row is claimed before anything is sent, so a
/// message that is redelivered or duplicated — which at-least-once delivery allows — can only ever
/// produce one attempt at a time.
/// </remarks>
public sealed class DeliveryWorkerService(
    RabbitMqConsumer consumer,
    IServiceScopeFactory scopes,
    ILogger<DeliveryWorkerService> logger) : BackgroundService
{
    /// <summary>
    /// How long to wait before trying to consume again. A broker that is not up yet is a startup
    /// ordering problem, and taking the process down with it would only turn that into a crash loop.
    /// </summary>
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Delivery worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await consumer.ConsumeAsync(HandleAsync, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Delivery consumer stopped; reconnecting.");

                if (!await WaitAsync(stoppingToken).ConfigureAwait(false))
                {
                    break;
                }
            }
        }

        logger.LogInformation("Delivery worker stopped.");
    }

    /// <summary>
    /// One scope per message: the worker and the store it claims through have to be the pair that
    /// writes the outcome back, and a scope per message keeps concurrent attempts from sharing a
    /// database context.
    /// </summary>
    private async Task HandleAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        if (!TryReadDispatch(message, out var dispatch))
        {
            // Nothing to retry: the message cannot be interpreted, and the delivery it was meant to
            // name is not knowable from it. Dropping it is the only thing left to do.
            logger.LogWarning("Discarded a queue message that does not name a delivery.");

            return;
        }

        using var scope = scopes.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<DeliveryWorker>()
            .ProcessAsync(dispatch.DeliveryId, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool TryReadDispatch(QueueMessage message, out DeliveryDispatch dispatch)
    {
        try
        {
            dispatch = DeliveryDispatch.FromPayload(Encoding.UTF8.GetString(message.Body.Span));
        }
        catch (JsonException)
        {
            dispatch = default;
            return false;
        }

        return dispatch.DeliveryId != Guid.Empty;
    }

    /// <returns><see langword="false"/> when the wait was cancelled, meaning the host is stopping.</returns>
    private static async Task<bool> WaitAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(ReconnectDelay, stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
