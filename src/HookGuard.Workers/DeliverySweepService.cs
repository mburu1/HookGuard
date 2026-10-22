using HookGuard.Application.Deliveries;

namespace HookGuard.Workers;

/// <summary>
/// Runs the sweep that hands a waiting delivery back to the queue, and the one that takes a delivery
/// away from a worker that never came back.
/// </summary>
/// <remarks>
/// A failed sweep is logged and retried on the next tick rather than abandoned: the delivery rows are
/// the record of what still has to happen, so the cost of a failure is latency, not correctness.
/// </remarks>
public sealed class DeliverySweepService(
    IServiceScopeFactory scopes,
    ILogger<DeliverySweepService> logger) : BackgroundService
{
    /// <summary>Retries to announce per sweep: enough to work through a backlog, small enough to bound one pass.</summary>
    private const int BatchSize = 100;

    /// <summary>
    /// How long to wait before looking for more work. The shortest wait the retry policy can ask for is
    /// the base delay, which is several seconds, so sweeping any more often than this would only read
    /// the table again to find the same rows.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Delivery sweep started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Delivery sweep failed; the delivery rows are retried.");

                if (!await WaitAfterFailureAsync(stoppingToken).ConfigureAwait(false))
                {
                    break;
                }
            }
        }

        logger.LogInformation("Delivery sweep stopped.");
    }

    /// <summary>
    /// One scope per sweep: the scheduler, the store it reads through and the database context all
    /// have to be the same instance for the pass to be one consistent read of the delivery rows.
    /// </summary>
    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();

        var swept = await scope.ServiceProvider
            .GetRequiredService<DeliveryScheduler>()
            .SweepAsync(BatchSize, cancellationToken)
            .ConfigureAwait(false);

        if (swept.Recovered > 0 || swept.Announced > 0)
        {
            logger.LogDebug(
                "Swept {Recovered} stuck deliveries and announced {Announced} due retries.",
                swept.Recovered,
                swept.Announced);
        }
    }

    /// <returns><see langword="false"/> when the wait was cancelled, meaning the host is stopping.</returns>
    private static async Task<bool> WaitAfterFailureAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
