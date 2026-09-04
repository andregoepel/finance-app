using AndreGoepel.FinanceApp.Categorization;
using AndreGoepel.FinanceApp.Connections;
using AndreGoepel.FinanceApp.Connectors;
using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Insights;
using AndreGoepel.FinanceApp.Planning;
using AndreGoepel.FinanceApp.Sync;
using AndreGoepel.Marten.Configuration;
using Marten;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;

namespace AndreGoepel.FinanceApp;

public static class Initialization
{
    // DataProtection (Postgres-persisted key ring, optional certificate encryption
    // via DataProtection:CertificatePath/CertificatePassword) comes from
    // AndreGoepel.AppFoundation.Hosting 1.1.0 — see docs/data-protection.md.
    public static IServiceCollection AddFinanceApp(this IServiceCollection services)
    {
        services.AddFinanceDomain();
        services.AddConnectors();
        services.AddCategorization();

        // Phase 3 API sync orchestration + scheduling. Connectors (HTTP clients,
        // registry) come from AddConnectors; these tie them to the import pipeline.
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAccountSyncService, AccountSyncService>();
        services.AddScoped<IProviderConnectionService, ProviderConnectionService>();
        services.AddScoped<IWiseBalanceService, WiseBalanceService>();

        // Daily scheduled sync via Quartz.NET, added to app-foundation's existing scheduler; the trigger comes from the stored, UI-editable SyncSchedule (SyncScheduleStartup).
        services.AddQuartz(quartz =>
            quartz.AddJob<DailySyncJob>(job =>
                job.WithIdentity(DailySyncJob.JobName).StoreDurably()
            )
        );
        // ISettingsStore itself is already registered transitively via AddAppFoundation ->
        // AddMartenIdentity, but AddMartenConfiguration() is idempotent (TryAdd) — call it
        // explicitly so this module doesn't rely on that incidental wiring.
        services.AddMartenConfiguration();
        services.ConfigureMarten(marten => marten.AddSettingsDocument<SyncSchedule>());
        services.AddSingleton<ISyncScheduleService, SyncScheduleService>();
        services.AddHostedService<SyncScheduleStartup>();

        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<INetWorthService, NetWorthService>();
        services.AddScoped<IRecurringService, RecurringService>();
        services.AddScoped<ICryptoService, CryptoService>();
        services.AddScoped<IPlanningService, PlanningService>();
        services.AddScoped<IMonthlyCategoryPlanService, MonthlyCategoryPlanService>();

        return services;
    }
}
