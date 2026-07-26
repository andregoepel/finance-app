using AndreGoepel.FinanceApp.Sync;
using AndreGoepel.Marten.Configuration;
using NSubstitute;
using Quartz;

namespace AndreGoepel.FinanceApp.Tests.Sync;

public sealed class SyncScheduleServiceTests
{
    private readonly ISettingsStore settingsStore = Substitute.For<ISettingsStore>();
    private readonly IScheduler scheduler = Substitute.For<IScheduler>();
    private readonly ISchedulerFactory schedulerFactory = Substitute.For<ISchedulerFactory>();

    public SyncScheduleServiceTests() =>
        schedulerFactory.GetScheduler(Arg.Any<CancellationToken>()).Returns(scheduler);

    [Fact]
    public async Task GetAsync_NoStoredSchedule_ReturnsDefaults()
    {
        // Arrange
        var service = BuildService();

        // Act
        var schedule = await service.GetAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("0 0 3 * * ?", schedule.CronExpression);
        Assert.True(schedule.Enabled);
    }

    [Fact]
    public async Task GetAsync_WithStoredSchedule_ReturnsStored()
    {
        // Arrange
        settingsStore
            .LoadAsync<SyncSchedule>(Arg.Any<CancellationToken>())
            .Returns(new SyncSchedule { CronExpression = "0 0 4 * * ?", Enabled = false });
        var service = BuildService();

        // Act
        var schedule = await service.GetAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("0 0 4 * * ?", schedule.CronExpression);
        Assert.False(schedule.Enabled);
    }

    [Fact]
    public async Task UpdateAsync_InvalidCronWhileEnabled_ReturnsFailureWithoutSaving()
    {
        // Arrange
        var service = BuildService();

        // Act
        var result = await service.UpdateAsync(
            "not a cron",
            enabled: true,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.False(result.IsSuccess);
        await settingsStore
            .DidNotReceive()
            .SaveAsync(Arg.Any<SyncSchedule>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_ValidCron_SavesAndReschedulesTrigger()
    {
        // Arrange
        scheduler.CheckExists(Arg.Any<JobKey>(), Arg.Any<CancellationToken>()).Returns(true);
        var service = BuildService();

        // Act
        var result = await service.UpdateAsync(
            "0 0 4 * * ?",
            enabled: true,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsSuccess);
        await settingsStore
            .Received(1)
            .SaveAsync(
                Arg.Is<SyncSchedule>(s => s!.CronExpression == "0 0 4 * * ?" && s.Enabled),
                Arg.Any<CancellationToken>()
            );
        await scheduler.Received(1).ScheduleJob(Arg.Any<ITrigger>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_Disabled_SavesButDoesNotScheduleTrigger()
    {
        // Arrange
        scheduler.CheckExists(Arg.Any<JobKey>(), Arg.Any<CancellationToken>()).Returns(true);
        // ApplyAsync (called from UpdateAsync) re-reads via GetAsync, so the stub has to
        // reflect the just-saved state — the substitute doesn't persist what was written.
        settingsStore
            .LoadAsync<SyncSchedule>(Arg.Any<CancellationToken>())
            .Returns(new SyncSchedule { CronExpression = "0 0 4 * * ?", Enabled = false });
        var service = BuildService();

        // Act
        var result = await service.UpdateAsync(
            "0 0 4 * * ?",
            enabled: false,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsSuccess);
        await scheduler
            .DidNotReceive()
            .ScheduleJob(Arg.Any<ITrigger>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyAsync_JobNotYetRegistered_AddsDurableJob()
    {
        // Arrange
        scheduler.CheckExists(Arg.Any<JobKey>(), Arg.Any<CancellationToken>()).Returns(false);
        var service = BuildService();

        // Act
        await service.ApplyAsync(TestContext.Current.CancellationToken);

        // Assert
        await scheduler
            .Received(1)
            .AddJob(Arg.Any<IJobDetail>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void NextRun_InvalidCron_ReturnsNull()
    {
        // Arrange
        var service = BuildService();

        // Act
        var next = service.NextRun("not a cron");

        // Assert
        Assert.Null(next);
    }

    private SyncScheduleService BuildService() => new(settingsStore, schedulerFactory);
}
