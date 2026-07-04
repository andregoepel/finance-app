using Quartz;

namespace AndreGoepel.FinanceApp.Sync;

/// <summary>
/// Quartz job that runs the daily API sync of every API-backed account. The
/// scheduler and hosted service come from app-foundation (identity uses Quartz
/// too), so this job is added to the existing scheduler rather than starting a
/// new one. <see cref="DisallowConcurrentExecutionAttribute"/> stops a slow run
/// from overlapping the next tick; failures are logged, never fatal.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class DailySyncJob(IAccountSyncService syncService, ILogger<DailySyncJob> logger)
    : IJob
{
    /// <summary>Quartz job identity, shared with the schedule service that manages its trigger.</summary>
    internal const string JobName = "daily-account-sync";

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            var summaries = await syncService.SyncAllAsync("scheduled", context.CancellationToken);
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
        catch (Exception exception)
        {
            logger.LogError(exception, "Scheduled sync run failed");
        }
    }
}
