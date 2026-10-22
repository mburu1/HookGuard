namespace HookGuard.Workers;

/// <summary>
/// Placeholder hosted service proving the worker host starts, stops and logs cleanly.
/// Phase 2 replaces it with the outbox relay, delivery workers and the retry scheduler.
/// </summary>
public class Worker(ILogger<Worker> logger) : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("HookGuard workers host started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(HeartbeatInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("HookGuard workers host stopping.");
    }
}
