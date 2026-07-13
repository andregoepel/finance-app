using System.Security.Cryptography;
using System.Text;

namespace AndreGoepel.FinanceApp.Connectors.Providers.Wise;

/// <summary>
/// Answers Wise's Strong Customer Authentication challenge on protected reads
/// (balance statements): the API rejects the first request with 403 and a
/// one-time token in the <c>x-2fa-approval</c> header; the client signs that
/// token with the account's registered RSA key (SHA-256, PKCS#1) and retries.
/// Pure and offline-testable — the fiddly crypto kept away from HTTP, like
/// <c>EnableBankingJwtFactory</c>.
/// </summary>
internal static class WiseScaSigner
{
    /// <summary>Base64 RSA-SHA256 signature of the one-time approval token.</summary>
    public static string Sign(string privateKeyPem, string oneTimeToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);
        ArgumentException.ThrowIfNullOrWhiteSpace(oneTimeToken);

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(oneTimeToken),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        return Convert.ToBase64String(signature);
    }
}
