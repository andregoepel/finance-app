using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Providers.EnableBanking;

/// <summary>
/// Syncs DKB and Revolut through the Enable Banking PSD2 aggregator. One
/// connector serves both because they share the same API; the concrete
/// <see cref="ProviderKind"/> is carried on the request. Accounts are matched to
/// a session balance via their stable identification hash, never the
/// session-specific id.
/// </summary>
/// <remarks>
/// Phase 3 skeleton: provider routing and the session/hash contract are in place;
/// resolving the session account from the identification hash and mapping
/// Enable Banking transactions to <c>NormalizedTransaction</c> land next, built
/// together once the consent flow returns real sessions.
/// </remarks>
public sealed class EnableBankingConnector(IEnableBankingClient client) : IProviderConnector
{
    internal const string SyncSource = "enablebanking-api-v1";

    public bool Supports(ProviderKind provider) =>
        provider is ProviderKind.Dkb or ProviderKind.Revolut;

    public Task<Result<ProviderSyncResult>> FetchAsync(
        ProviderSyncRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.SessionReference))
        {
            return Task.FromResult(
                Result.Fail<ProviderSyncResult>(
                    $"No active Enable Banking consent for {request.Provider} "
                        + "(Settings → Connections → Connect)."
                )
            );
        }

        if (string.IsNullOrWhiteSpace(request.IdentificationHash))
        {
            return Task.FromResult(
                Result.Fail<ProviderSyncResult>(
                    "The account has no Enable Banking identification hash set — "
                        + "re-link it after authorizing the consent."
                )
            );
        }

        // TODO(phase-3): resolve the session account id whose identification_hash
        // matches request.IdentificationHash, call client.GetTransactionsJsonAsync
        // for [request.Since, today], then map each entry to NormalizedTransaction
        // (booking/value dates, signed amount + currency, creditor/debtor name,
        // remittance info, entry reference as ExternalId, raw JSON as RawData).
        _ = client;
        return Task.FromResult(
            Result.Fail<ProviderSyncResult>(
                "Enable Banking sync is not implemented yet — Phase 3 next step."
            )
        );
    }
}
