using AndreGoepel.Core;

namespace AndreGoepel.FinanceApp.Sync;

/// <summary>
/// Reads and updates the daily-sync <see cref="SyncSchedule"/> and keeps the
/// Quartz trigger in step: saving a new cron reschedules the job immediately, and
/// <see cref="ApplyAsync"/> restores the stored schedule at startup.
/// </summary>
public interface ISyncScheduleService
{
    Task<SyncSchedule> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Validates + stores the cron/enabled state, then reschedules the trigger.</summary>
    Task<Result> UpdateAsync(
        string cronExpression,
        bool enabled,
        CancellationToken cancellationToken = default
    );

    /// <summary>(Re)applies the stored schedule to the Quartz scheduler.</summary>
    Task ApplyAsync(CancellationToken cancellationToken = default);

    /// <summary>Next fire time for a cron (UTC), or <c>null</c> when the expression is invalid.</summary>
    DateTimeOffset? NextRun(string cronExpression);
}
