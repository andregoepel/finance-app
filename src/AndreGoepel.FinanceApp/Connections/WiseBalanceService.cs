using AndreGoepel.FinanceApp.Connectors.Providers.Wise;
using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Credentials;
using AndreGoepel.FinanceApp.Domain.Exchange;
using AndreGoepel.FinanceApp.Domain.Providers;
using Marten;
using Wolverine;

namespace AndreGoepel.FinanceApp.Connections;

/// <summary>Implements <see cref="IWiseBalanceService"/> against the Wise API.</summary>
internal sealed class WiseBalanceService(
    IWiseApiClient wiseApiClient,
    ICredentialStore credentialStore,
    IExchangeRateProvider exchangeRateProvider,
    IDocumentSession session,
    IMessageBus messageBus
) : IWiseBalanceService
{
    public async Task<Result<WiseBalanceSyncResult>> SyncConnectionAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default
    )
    {
        // All profiles here: the refresh only touches accounts that are already
        // linked, so a manually linked business balance keeps refreshing.
        var fetched = await FetchBalancesAsync(
            connectionId,
            personalProfilesOnly: false,
            cancellationToken
        );
        if (fetched.IsFailure)
        {
            return Result.Fail<WiseBalanceSyncResult>(fetched.Error!);
        }

        var accountsUpdated = await ApplyBalancesAsync(
            connectionId,
            fetched.Value!.Balances,
            cancellationToken
        );
        return Result.Ok(new WiseBalanceSyncResult(fetched.Value.Balances, accountsUpdated));
    }

    public async Task<Result<WiseAccountImportResult>> ImportAccountsAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default
    )
    {
        // Business profiles are excluded by default: this is a household app, and
        // a token that also sees a business profile would otherwise flood the
        // household with company balances. Link business balances manually if
        // ever needed.
        var fetched = await FetchBalancesAsync(
            connectionId,
            personalProfilesOnly: true,
            cancellationToken
        );
        if (fetched.IsFailure)
        {
            return Result.Fail<WiseAccountImportResult>(fetched.Error!);
        }
        var (connection, balances) = fetched.Value!;

        // An account needs an owner; the connection's owner is the natural one.
        if (connection.OwnerUserId is not Guid ownerUserId)
        {
            return Result.Fail<WiseAccountImportResult>(
                "The connection has no owner — set one first (Settings → Connections), "
                    + "imported accounts belong to that user."
            );
        }

        // Match against every Wise account, not just this connection's, so a
        // balance that was linked manually elsewhere is not duplicated.
        var linkedExternalIds = (
            await session
                .Query<Account>()
                .Where(a => a.Provider == ProviderKind.Wise && a.ExternalId != null)
                .Select(a => a.ExternalId!)
                .ToListAsync(cancellationToken)
        ).ToHashSet();

        var created = new List<string>();
        var alreadyLinked = 0;
        foreach (var balance in balances)
        {
            if (linkedExternalIds.Contains(balance.Id.ToString()))
            {
                alreadyLinked++;
                continue;
            }

            var name = AccountNameFor(balance);
            var result = await messageBus.InvokeAsync<Result<Account>>(
                new CreateAccountCommand(
                    name,
                    ProviderKind.Wise,
                    AccountType.Checking,
                    balance.Currency,
                    IsShared: false,
                    OwnerUserIds: [ownerUserId],
                    SyncMethod.Api,
                    Iban: null,
                    ConnectionId: connectionId,
                    ExternalId: balance.Id.ToString()
                ),
                cancellationToken
            );
            if (result.IsFailure)
            {
                return Result.Fail<WiseAccountImportResult>(
                    $"Could not create account “{name}”: {result.Error}"
                );
            }
            created.Add(name);
        }

        // New accounts start with the current amount so net worth is right away.
        await ApplyBalancesAsync(connectionId, balances, cancellationToken);
        return Result.Ok(new WiseAccountImportResult(created, alreadyLinked));
    }

    /// <summary>
    /// Account name for an unlinked balance: a jar keeps its own name, a standard
    /// balance becomes "Wise EUR" etc.
    /// </summary>
    internal static string AccountNameFor(WiseBalance balance) =>
        string.IsNullOrWhiteSpace(balance.Name) ? $"Wise {balance.Currency}" : balance.Name.Trim();

    /// <summary>
    /// Shared plumbing: connection + token checks, then the balances of every
    /// profile — optionally only personal profiles (account import skips
    /// business profiles by default).
    /// </summary>
    private async Task<
        Result<(ProviderConnection Connection, IReadOnlyList<WiseBalance> Balances)>
    > FetchBalancesAsync(
        Guid connectionId,
        bool personalProfilesOnly,
        CancellationToken cancellationToken
    )
    {
        var connection = await session.LoadAsync<ProviderConnection>(
            connectionId,
            cancellationToken
        );
        if (connection is null)
        {
            return Result.Fail<(ProviderConnection, IReadOnlyList<WiseBalance>)>(
                "Connection not found."
            );
        }
        if (connection.Provider != ProviderKind.Wise)
        {
            return Result.Fail<(ProviderConnection, IReadOnlyList<WiseBalance>)>(
                "Balance sync is Wise-only."
            );
        }

        var token = await credentialStore.GetSecretAsync(
            CredentialKeys.WiseApiToken(connectionId),
            cancellationToken
        );
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Fail<(ProviderConnection, IReadOnlyList<WiseBalance>)>(
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
            return Result.Fail<(ProviderConnection, IReadOnlyList<WiseBalance>)>(profiles.Error!);
        }

        var relevantProfiles = personalProfilesOnly
            ? profiles.Value!.Where(p =>
                string.Equals(p.Type, "personal", StringComparison.OrdinalIgnoreCase)
            )
            : profiles.Value!;

        var balances = new List<WiseBalance>();
        foreach (var profile in relevantProfiles)
        {
            var profileBalances = await wiseApiClient.GetBalancesAsync(
                token,
                connection.Environment,
                profile.Id,
                cancellationToken
            );
            if (profileBalances.IsFailure)
            {
                return Result.Fail<(ProviderConnection, IReadOnlyList<WiseBalance>)>(
                    profileBalances.Error!
                );
            }
            balances.AddRange(profileBalances.Value!);
        }

        return Result.Ok<(ProviderConnection, IReadOnlyList<WiseBalance>)>((connection, balances));
    }

    /// <summary>Writes each fetched balance onto the account linked by external id (Wise balance id).</summary>
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

    /// <summary>Balance in EUR for the net-worth anchor; <c>null</c> if the rate is unavailable.</summary>
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
