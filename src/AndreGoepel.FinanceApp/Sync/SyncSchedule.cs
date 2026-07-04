namespace AndreGoepel.FinanceApp.Sync;

/// <summary>
/// The editable schedule for the daily API sync — a single stored document. The
/// cron drives a Quartz trigger that is (re)applied whenever this changes.
/// </summary>
public sealed class SyncSchedule
{
    /// <summary>Singleton document id — there is only ever one schedule.</summary>
    public const string SingletonId = "sync-schedule";

    public string Id { get; init; } = SingletonId;

    /// <summary>Quartz cron expression (sec min hour day-of-month month day-of-week). Default 03:00 daily.</summary>
    public string CronExpression { get; set; } = "0 0 3 * * ?";

    public bool Enabled { get; set; } = true;

    public DateTimeOffset? UpdatedAt { get; set; }
}
