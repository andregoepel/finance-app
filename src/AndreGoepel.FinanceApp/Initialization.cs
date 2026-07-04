using AndreGoepel.FinanceApp.Categorization;
using AndreGoepel.FinanceApp.Connections;
using AndreGoepel.FinanceApp.Connectors;
using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Sync;
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

        // Daily scheduled sync via Quartz.NET. The scheduler + hosted service are
        // already registered by app-foundation (identity uses Quartz); this
        // additive AddQuartz only contributes our durable job. The trigger is not
        // static — it is applied from the stored, UI-editable SyncSchedule at
        // startup (SyncScheduleStartup) and whenever the schedule changes.
        services.AddQuartz(quartz =>
            quartz.AddJob<DailySyncJob>(job =>
                job.WithIdentity(DailySyncJob.JobName).StoreDurably()
            )
        );
        services.AddSingleton<ISyncScheduleService, SyncScheduleService>();
        services.AddHostedService<SyncScheduleStartup>();

        return services;
    }
}
