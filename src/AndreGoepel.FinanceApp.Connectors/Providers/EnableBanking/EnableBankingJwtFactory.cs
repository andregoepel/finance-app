using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AndreGoepel.FinanceApp.Connectors.Providers.EnableBanking;

/// <summary>
/// Builds the RS256 JWT that authenticates every Enable Banking API call. The
/// token's <c>kid</c> header is the registered application id; the body is signed
/// with the application's RSA private key (PEM, from the credential store). Pure
/// and offline-testable — the fiddly crypto kept away from HTTP.
/// </summary>
internal static class EnableBankingJwtFactory
{
    private const string Issuer = "enablebanking.com";
    private const string Audience = "api.enablebanking.com";

    /// <summary>
    /// Creates a signed bearer token valid for <paramref name="lifetime"/> from
    /// <paramref name="now"/>. <paramref name="now"/> is injectable so tests are
    /// deterministic.
    /// </summary>
    public static string Create(
        string applicationId,
        string privateKeyPem,
        DateTimeOffset now,
        TimeSpan lifetime
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);

        var header = new JsonObject
        {
            ["alg"] = "RS256",
            ["kid"] = applicationId,
            ["typ"] = "JWT",
        };
        var payload = new JsonObject
        {
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.Add(lifetime).ToUnixTimeSeconds(),
        };

        var signingInput =
            $"{Base64UrlEncode(ToJsonBytes(header))}.{Base64UrlEncode(ToJsonBytes(payload))}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );

        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    private static byte[] ToJsonBytes(JsonNode node) =>
        JsonSerializer.SerializeToUtf8Bytes(node, JsonSerializerOptions.Default);

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
