using System.Security.Cryptography;
using System.Text;
using AndreGoepel.FinanceApp.Connectors.Providers.Wise;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Providers;

public class WiseScaSignerTests
{
    [Fact]
    public void Sign_ProducesVerifiableRsaSha256Signature()
    {
        // Arrange
        using var rsa = RSA.Create(2048);
        var privatePem = rsa.ExportPkcs8PrivateKeyPem();
        const string oneTimeToken = "1f8750ec-2e39-4ad2-a0f8-08baa9b6ab51";

        // Act
        var signature = WiseScaSigner.Sign(privatePem, oneTimeToken);

        // Assert — the public half verifies what the private half signed.
        var valid = rsa.VerifyData(
            Encoding.ASCII.GetBytes(oneTimeToken),
            Convert.FromBase64String(signature),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        Assert.True(valid);
    }

    [Fact]
    public void Sign_AcceptsRsaPrivateKeyPemFormat()
    {
        // Arrange — "BEGIN RSA PRIVATE KEY" (PKCS#1), the format Wise's docs generate.
        using var rsa = RSA.Create(2048);
        var pkcs1Pem = rsa.ExportRSAPrivateKeyPem();

        // Act
        var signature = WiseScaSigner.Sign(pkcs1Pem, "token");

        // Assert
        Assert.NotEmpty(signature);
    }

    [Fact]
    public void Sign_GarbagePem_Throws()
    {
        // Act / Assert
        Assert.ThrowsAny<ArgumentException>(() => WiseScaSigner.Sign("not-a-pem", "token"));
    }
}
