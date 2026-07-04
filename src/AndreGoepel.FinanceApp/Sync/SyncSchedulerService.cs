namespace AndreGoepel.FinanceApp.Sync;

/// <summary>
/// Runs a daily API sync of every API-backed account. Deliberately a plain
/// <see cref="BackgroundService"/> with a <see cref="PeriodicTimer"/> so the
/// Phase 3 skeleton adds no new dependency; PLAN.md's Quartz.NET is the intended
/// production scheduler (cron windows, misfire handling, clustering) and swaps in
/// here as a next step. Each tick runs in its own DI scope; failures are logged,
/// never fatal.
/// </summary>
internal sealed class SyncSchedulerService(
    IServiceScopeFactory scopeFactory,
    ILogger<SyncSchedulerService> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait one interval before the first run so startup is not slowed and a
        // crash-loop cannot hammer the provider APIs.
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var syncService = scope.ServiceProvider.GetRequiredService<IAccountSyncService>();
                var summaries = await syncService.SyncAllAsync("scheduled", stoppingToken);

                foreach (var summary in summaries.Where(s => !s.Success))
                {
                    logger.LogWarning(
                        "Scheduled sync failed for {Account}: {Error}",
                        summary.AccountName,
                        summary.Error
                    );
                }
                logger.LogInformation(
                    "Scheduled sync completed: {Total} accounts, {Imported} transactions imported",
                    summaries.Count,
                    summaries.Sum(s => s.Imported)
                );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scheduled sync run failed");
            }
        }
    }
}
