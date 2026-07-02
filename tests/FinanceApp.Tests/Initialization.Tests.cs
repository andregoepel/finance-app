using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FinanceApp.Tests;

public class InitializationTests
{
    [Fact]
    public void AddFinanceApp_WithoutCertificateConfig_StoresKeysWithoutEncryptor()
    {
        // Arrange
        var services = new ServiceCollection().AddLogging();
        var configuration = new ConfigurationBuilder().Build();

        // Act
        services.AddFinanceApp(configuration);

        // Assert
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        Assert.Null(options.XmlEncryptor);
    }

    [Fact]
    public void AddFinanceApp_WithCertificateConfig_EncryptsKeysWithCertificate()
    {
        // Arrange
        var certificatePath = WriteSelfSignedPfx(password: "test-password");
        try
        {
            var services = new ServiceCollection().AddLogging();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["DataProtection:CertificatePath"] = certificatePath,
                        ["DataProtection:CertificatePassword"] = "test-password",
                    }
                )
                .Build();

            // Act
            services.AddFinanceApp(configuration);

            // Assert
            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
            Assert.IsType<CertificateXmlEncryptor>(options.XmlEncryptor);
        }
        finally
        {
            File.Delete(certificatePath);
        }
    }

    private static string WriteSelfSignedPfx(string password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=FinanceApp Tests",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1)
        );

        var path = Path.Combine(Path.GetTempPath(), $"financeapp-test-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return path;
    }
}
