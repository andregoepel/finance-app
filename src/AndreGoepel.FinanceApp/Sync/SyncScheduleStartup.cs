namespace AndreGoepel.FinanceApp.Sync;

/// <summary>
/// Applies the stored <see cref="SyncSchedule"/> to Quartz at startup. Registered
/// after app-foundation's Quartz hosted service, so the scheduler and durable job
/// exist by the time this runs. A failure here (e.g. DB not ready) is logged, not
/// fatal — the app still starts; the schedule can be saved again from the UI.
/// </summary>
internal sealed class SyncScheduleStartup(
    ISyncScheduleService scheduleService,
    ILogger<SyncScheduleStartup> logger
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await scheduleService.ApplyAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not apply the stored sync schedule at startup");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
