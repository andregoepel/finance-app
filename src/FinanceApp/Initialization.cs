using System.Security.Cryptography.X509Certificates;
using FinanceApp.Categorization;
using FinanceApp.Connectors;
using FinanceApp.Domain;
using FinanceApp.Infrastructure.DataProtection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;

namespace FinanceApp;

public static class Initialization
{
    public static IServiceCollection AddFinanceApp(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        // Persist the DataProtection key ring in Postgres (via Marten). Provider
        // credentials are encrypted with these keys — a lost key ring makes them
        // unrecoverable, so the keys must live in the database, not the container
        // filesystem.
        services.Configure<DataProtectionOptions>(options =>
            options.ApplicationDiscriminator = "FinanceApp"
        );
        services
            .AddOptions<KeyManagementOptions>()
            .Configure<IServiceProvider>(
                (options, provider) => options.XmlRepository = new MartenXmlRepository(provider)
            );

        // Encrypt the key ring at rest with an X.509 certificate whose private key
        // lives outside the database (mounted secret on the app host). A database
        // dump alone then cannot decrypt stored provider credentials. Without a
        // configured certificate (local development) keys are stored unencrypted
        // and DataProtection logs its at-rest warning.
        var certificatePath = configuration["DataProtection:CertificatePath"];
        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath,
                configuration["DataProtection:CertificatePassword"]
            );
            services.AddDataProtection().ProtectKeysWithCertificate(certificate);
        }

        services.AddFinanceDomain();
        services.AddConnectors();
        services.AddCategorization();

        return services;
    }
}
