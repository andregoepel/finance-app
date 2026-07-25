using AndreGoepel.FinanceApp.Connectors.Providers.Wise;
using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Credentials;
using AndreGoepel.FinanceApp.Domain.Exchange;
using AndreGoepel.FinanceApp.Domain.Providers;
using Marten;

namespace AndreGoepel.FinanceApp.Connections;

/// <summary>Implements <see cref="IWiseBalanceService"/> against the Wise API.</summary>
internal sealed class WiseBalanceService(
    IWiseApiClient wiseApiClient,
    ICredentialStore credentialStore,
    IExchangeRateProvider exchangeRateProvider,
    IDocumentSession session
) : IWiseBalanceService
{
    public async Task<Result<WiseBalanceSyncResult>> SyncConnectionAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default
    )
    {
        var connection = await session.LoadAsync<ProviderConnection>(
            connectionId,
            cancellationToken
        );
        if (connection is null)
        {
            return Result.Fail<WiseBalanceSyncResult>("Connection not found.");
        }
        if (connection.Provider != ProviderKind.Wise)
        {
            return Result.Fail<WiseBalanceSyncResult>("Balance sync is Wise-only.");
        }

        var token = await credentialStore.GetSecretAsync(
            CredentialKeys.WiseApiToken(connectionId),
            cancellationToken
        );
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Fail<WiseBalanceSyncResult>(
                "No Wise API token is configured for this connection."
            );
        }

        var profiles = await wiseApiClient.GetProfilesAsync(
            token,
            connection.Environment,
            cancellationToken
        );
        if (profiles.IsFailure)
        {
            return Result.Fail<WiseBalanceSyncResult>(profiles.Error!);
        }

        var balances = new List<WiseBalance>();
        foreach (var profile in profiles.Value!)
        {
            var profileBalances = await wiseApiClient.GetBalancesAsync(
                token,
                connection.Environment,
                profile.Id,
                cancellationToken
            );
            if (profileBalances.IsFailure)
            {
                return Result.Fail<WiseBalanceSyncResult>(profileBalances.Error!);
            }
            balances.AddRange(profileBalances.Value!);
        }

        var accountsUpdated = await ApplyBalancesAsync(connectionId, balances, cancellationToken);
        return Result.Ok(new WiseBalanceSyncResult(balances, accountsUpdated));
    }

    // Writes each fetched balance onto the account linked by external id (Wise balance id).
    private async Task<int> ApplyBalancesAsync(
        Guid connectionId,
        IReadOnlyList<WiseBalance> balances,
        CancellationToken cancellationToken
    )
    {
        var accounts = await session
            .Query<Account>()
            .Where(a => a.ConnectionId == connectionId)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var updated = 0;
        foreach (var account in accounts)
        {
            var balance = balances.FirstOrDefault(b => b.Id.ToString() == account.ExternalId);
            if (balance is null)
            {
                continue;
            }
            account.CurrentBalance = balance.Amount;
            account.CurrentBalanceEur = await ToEurAsync(
                balance.Amount,
                balance.Currency,
                today,
                cancellationToken
            );
            account.BalanceUpdatedAt = now;
            session.Store(account);
            updated++;
        }

        if (updated > 0)
        {
            await session.SaveChangesAsync(cancellationToken);
        }
        return updated;
    }

    // Balance in EUR for the net-worth anchor; null if the rate is unavailable.
    private async Task<decimal?> ToEurAsync(
        decimal amount,
        string currency,
        DateOnly date,
        CancellationToken cancellationToken
    )
    {
        var rate = await exchangeRateProvider.GetEurRateAsync(currency, date, cancellationToken);
        return rate.IsSuccess ? amount * rate.Value!.EurPerUnit : null;
    }
}
