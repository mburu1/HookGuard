using HookGuard.Application.Outbox;

namespace HookGuard.Workers;

/// <summary>
/// Drains the transactional outbox. Each sweep reads a batch, publishes it in order and records the
/// broker's confirmations in the same database scope.
/// </summary>
/// <remarks>
/// A failed sweep is logged and retried on the next tick rather than abandoned: only the database
/// knows which messages are still owed to the queue, so the cost of a failure is latency, not
/// correctness.
/// </remarks>
public sealed class OutboxRelayService(
    IServiceScopeFactory scopes,
    ILogger<OutboxRelayService> logger) : BackgroundService
{
    /// <summary>Messages per sweep: enough to drain a backlog, small enough to bound the batch.</summary>
    private const int BatchSize = 100;

    /// <summary>
    /// How long to wait before looking for more work. A batch that came back full is followed
    /// straight away by the next sweep, so a backlog drains at publishing speed rather than one
    /// batch per tick.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox relay started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await PublishBatchAsync(stoppingToken) < BatchSize)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox relay sweep failed; the backlog is retried.");

                if (!await WaitAfterFailureAsync(stoppingToken))
                {
                    break;
                }
            }
        }

        logger.LogInformation("Outbox relay stopped.");
    }

    /// <summary>
    /// One scope per sweep: the store and the context it reads through have to be the pair that
    /// writes the confirmations back.
    /// </summary>
    private async Task<int> PublishBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();

        var published = await scope.ServiceProvider
            .GetRequiredService<OutboxRelay>()
            .PublishBatchAsync(BatchSize, cancellationToken);

        if (published > 0)
        {
            logger.LogDebug("Published {Count} outbox messages.", published);
        }

        return published;
    }

    /// <returns><see langword="false"/> when the wait was cancelled, meaning the host is stopping.</returns>
    private static async Task<bool> WaitAfterFailureAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(PollInterval, stoppingToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
