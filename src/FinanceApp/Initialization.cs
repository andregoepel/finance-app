using FinanceApp.Categorization;
using FinanceApp.Connectors;
using FinanceApp.Domain;

namespace FinanceApp;

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

        return services;
    }
}
