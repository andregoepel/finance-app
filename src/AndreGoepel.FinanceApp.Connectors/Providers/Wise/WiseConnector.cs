using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Domain.Credentials;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Providers.Wise;

/// <summary>
/// Syncs a Wise balance/account via the Wise personal API. Reads the connection's
/// API token from the credential store, fetches the statement for the window, and
/// normalizes it into the shared import shape. Authentication is token-only — no
/// SCA key pair is used.
/// </summary>
/// <remarks>
/// Phase 3 skeleton: the credential wiring, provider routing and pipeline shape
/// are in place; the actual statement fetch and row normalization are the next
/// step we build together against your Wise token.
/// </remarks>
public sealed class WiseConnector(IWiseApiClient apiClient, ICredentialStore credentialStore)
    : IProviderConnector
{
    internal const string SyncSource = "wise-api-v1";

    public bool Supports(ProviderKind provider) => provider == ProviderKind.Wise;

    public async Task<Result<ProviderSyncResult>> FetchAsync(
        ProviderSyncRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var token = await credentialStore.GetSecretAsync(
            CredentialKeys.WiseApiToken(request.ConnectionId),
            cancellationToken
        );
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Fail<ProviderSyncResult>(
                "No Wise API token is configured for this connection (Settings → Connections)."
            );
        }

        if (string.IsNullOrWhiteSpace(request.ExternalId))
        {
            return Result.Fail<ProviderSyncResult>(
                "The Wise account has no external balance id set (Settings → Accounts)."
            );
        }

        // TODO(phase-3): resolve the profile via apiClient.GetProfilesAsync(token),
        // fetch the statement for [request.Since, today] for request.ExternalId
        // (balance id), then map Wise statement lines to NormalizedTransaction
        // (booking/value dates, signed amount + currency, counterparty, reference,
        // Wise reference id as ExternalId, raw JSON as RawData). Built together
        // against a real token.
        _ = apiClient;
        return Result.Fail<ProviderSyncResult>(
            "Wise API sync is not implemented yet — Phase 3 next step."
        );
    }
}
