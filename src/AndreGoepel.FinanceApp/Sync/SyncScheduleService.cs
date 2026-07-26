using AndreGoepel.Core;
using Marten;
using Quartz;

namespace AndreGoepel.FinanceApp.Sync;

/// <summary>
/// Implements <see cref="ISyncScheduleService"/> over Marten (the stored schedule)
/// and the Quartz scheduler (the live trigger). Uses <see cref="IDocumentStore"/>
/// and opens its own sessions so it can be a singleton — usable from the startup
/// hosted service and Blazor pages alike.
/// </summary>
internal sealed class SyncScheduleService(IDocumentStore store, ISchedulerFactory schedulerFactory)
    : ISyncScheduleService
{
    private static readonly JobKey JobKey = new(DailySyncJob.JobName);
    private static readonly TriggerKey TriggerKey = new(DailySyncJob.JobName + "-trigger");

    public async Task<SyncSchedule> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var session = store.QuerySession();
        return await session.LoadAsync<SyncSchedule>(SyncSchedule.SingletonId, cancellationToken)
            ?? new SyncSchedule();
    }

    public async Task<Result> UpdateAsync(
        string cronExpression,
        bool enabled,
        CancellationToken cancellationToken = default
    )
    {
        var cron = (cronExpression ?? string.Empty).Trim();
        if (enabled && !CronExpression.IsValidExpression(cron))
        {
            return Result.Fail("That is not a valid Quartz cron expression.");
        }

        await using var session = store.LightweightSession();
        var schedule =
            await session.LoadAsync<SyncSchedule>(SyncSchedule.SingletonId, cancellationToken)
            ?? new SyncSchedule();
        schedule.CronExpression = cron;
        schedule.Enabled = enabled;
        schedule.UpdatedAt = DateTimeOffset.UtcNow;
        session.Store(schedule);
        await session.SaveChangesAsync(cancellationToken);

        await ApplyAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        var schedule = await GetAsync(cancellationToken);
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);

        if (!await scheduler.CheckExists(JobKey, cancellationToken))
        {
            await scheduler.AddJob(
                JobBuilder.Create<DailySyncJob>().WithIdentity(JobKey).StoreDurably().Build(),
                replace: true,
                cancellationToken
            );
        }

        await scheduler.UnscheduleJob(TriggerKey, cancellationToken);
        if (schedule.Enabled && CronExpression.IsValidExpression(schedule.CronExpression))
        {
            await scheduler.ScheduleJob(
                TriggerBuilder
                    .Create()
                    .WithIdentity(TriggerKey)
                    .ForJob(JobKey)
                    .WithCronSchedule(schedule.CronExpression)
                    .Build(),
                cancellationToken
            );
        }
    }

    public DateTimeOffset? NextRun(string cronExpression) =>
        CronExpression.IsValidExpression(cronExpression)
            ? new CronExpression(cronExpression).GetNextValidTimeAfter(DateTimeOffset.UtcNow)
            : null;
}
