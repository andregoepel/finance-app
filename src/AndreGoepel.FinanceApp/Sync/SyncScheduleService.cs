using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Resources;
using AndreGoepel.Marten.Configuration;
using Microsoft.Extensions.Localization;
using Quartz;

namespace AndreGoepel.FinanceApp.Sync;

/// <summary>
/// Implements <see cref="ISyncScheduleService"/> over <see cref="ISettingsStore"/> (the stored
/// schedule) and the Quartz scheduler (the live trigger).
/// </summary>
internal sealed class SyncScheduleService(
    ISettingsStore settingsStore,
    ISchedulerFactory schedulerFactory,
    IStringLocalizer<Strings> localizer
) : ISyncScheduleService
{
    private static readonly JobKey JobKey = new(DailySyncJob.JobName);
    private static readonly TriggerKey TriggerKey = new(DailySyncJob.JobName + "-trigger");

    public async Task<SyncSchedule> GetAsync(CancellationToken cancellationToken = default) =>
        await settingsStore.LoadAsync<SyncSchedule>(cancellationToken) ?? new SyncSchedule();

    public async Task<Result> UpdateAsync(
        string cronExpression,
        bool enabled,
        CancellationToken cancellationToken = default
    )
    {
        var cron = (cronExpression ?? string.Empty).Trim();
        if (enabled && !CronExpression.TryParse(cron, out _))
        {
            return Result.Fail(localizer["Sync.InvalidCronExpression"]);
        }

        await settingsStore.SaveAsync(
            new SyncSchedule
            {
                CronExpression = cron,
                Enabled = enabled,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            cancellationToken
        );

        await ApplyAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        var schedule = await GetAsync(cancellationToken);
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);

        await scheduler.AddJob(
            JobBuilder.Create<DailySyncJob>().WithIdentity(JobKey).StoreDurably().Build(),
            AddJobOptions.Replacing,
            cancellationToken
        );

        if (schedule.Enabled && CronExpression.TryParse(schedule.CronExpression, out _))
        {
            await scheduler.ScheduleJob(
                TriggerBuilder
                    .Create()
                    .WithIdentity(TriggerKey)
                    .ForJob(JobKey)
                    .WithCronSchedule(schedule.CronExpression)
                    .Build(),
                ScheduleJobOptions.Replacing,
                cancellationToken
            );
        }
        else
        {
            await scheduler.UnscheduleJob(TriggerKey, cancellationToken);
        }
    }

    public DateTimeOffset? NextRun(string cronExpression) =>
        CronExpression.TryParse(cronExpression, out var cron)
            ? cron.GetNextValidTimeAfter(DateTimeOffset.UtcNow)
            : null;
}
