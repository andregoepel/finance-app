using AndreGoepel.FinanceApp.Categorization;
using AndreGoepel.FinanceApp.Connections;
using AndreGoepel.FinanceApp.Connectors;
using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Sync;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddHostedService<SyncSchedulerService>();

        return services;
    }
}
