using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AndreGoepel.FinanceApp.Connectors.Providers.EnableBanking;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Providers;

public sealed class EnableBankingJwtFactoryTests
{
    private const string ApplicationId = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void Create_WithValidInputs_ProducesThreePartToken()
    {
        // Arrange
        using var rsa = RSA.Create(2048);

        // Act
        var jwt = EnableBankingJwtFactory.Create(
            ApplicationId,
            rsa.ExportPkcs8PrivateKeyPem(),
            DateTimeOffset.UnixEpoch,
            TimeSpan.FromMinutes(5)
        );

        // Assert
        Assert.Equal(3, jwt.Split('.').Length);
    }

    [Fact]
    public void Create_ValidToken_SignatureVerifiesWithPublicKey()
    {
        // Arrange
        using var rsa = RSA.Create(2048);
        var jwt = EnableBankingJwtFactory.Create(
            ApplicationId,
            rsa.ExportPkcs8PrivateKeyPem(),
            DateTimeOffset.UnixEpoch,
            TimeSpan.FromMinutes(5)
        );
        var parts = jwt.Split('.');

        // Act
        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var valid = rsa.VerifyData(
            signingInput,
            Base64UrlDecode(parts[2]),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );

        // Assert
        Assert.True(valid);
    }

    [Fact]
    public void Create_ValidToken_HeaderCarriesApplicationIdAsKid()
    {
        // Arrange
        using var rsa = RSA.Create(2048);
        var jwt = EnableBankingJwtFactory.Create(
            ApplicationId,
            rsa.ExportPkcs8PrivateKeyPem(),
            DateTimeOffset.UnixEpoch,
            TimeSpan.FromMinutes(5)
        );

        // Act
        var header = DecodeJson(jwt.Split('.')[0]);

        // Assert
        Assert.Equal("RS256", (string?)header["alg"]);
        Assert.Equal("JWT", (string?)header["typ"]);
        Assert.Equal(ApplicationId, (string?)header["kid"]);
    }

    [Fact]
    public void Create_ValidToken_PayloadHasExpectedClaimsAndExpiry()
    {
        // Arrange
        using var rsa = RSA.Create(2048);
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var jwt = EnableBankingJwtFactory.Create(
            ApplicationId,
            rsa.ExportPkcs8PrivateKeyPem(),
            issuedAt,
            TimeSpan.FromMinutes(5)
        );

        // Act
        var payload = DecodeJson(jwt.Split('.')[1]);

        // Assert
        Assert.Equal("enablebanking.com", (string?)payload["iss"]);
        Assert.Equal("api.enablebanking.com", (string?)payload["aud"]);
        Assert.Equal(issuedAt.ToUnixTimeSeconds(), (long)payload["iat"]!);
        Assert.Equal(issuedAt.AddMinutes(5).ToUnixTimeSeconds(), (long)payload["exp"]!);
    }

    private static JsonObject DecodeJson(string base64UrlSegment) =>
        JsonNode.Parse(Encoding.UTF8.GetString(Base64UrlDecode(base64UrlSegment)))!.AsObject();

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded,
        };
        return Convert.FromBase64String(padded);
    }
}
