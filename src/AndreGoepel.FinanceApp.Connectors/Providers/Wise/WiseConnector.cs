using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Domain.Credentials;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Providers.Wise;

/// <summary>
/// Syncs Wise transactions through the SCA-protected balance-statement endpoint.
/// The account's <c>ExternalId</c> is the Wise balance id (the same link the
/// balance refresh uses); the owning profile is resolved per sync because the
/// statement endpoint is addressed per profile + balance.
/// </summary>
public sealed class WiseConnector(IWiseApiClient client, ICredentialStore credentialStore)
    : IProviderConnector
{
    internal const string SyncSource = "wise-api-v1";

    public bool Supports(ProviderKind provider) => provider is ProviderKind.Wise;

    public async Task<Result<ProviderSyncResult>> FetchAsync(
        ProviderSyncRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!long.TryParse(request.ExternalId, out var balanceId))
        {
            return Result.Fail<ProviderSyncResult>(
                "The account has no Wise balance id — set it under Settings → Accounts "
                    + "(the balance id shows on Settings → Connections after a balance refresh)."
            );
        }

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

        var scaPrivateKey = await credentialStore.GetSecretAsync(
            CredentialKeys.WiseScaPrivateKey(request.ConnectionId),
            cancellationToken
        );

        var profileId = await ResolveProfileIdAsync(
            token,
            request.Environment,
            balanceId,
            cancellationToken
        );
        if (profileId.IsFailure)
        {
            return Result.Fail<ProviderSyncResult>(profileId.Error!);
        }

        var statement = await client.GetBalanceStatementAsync(
            token,
            scaPrivateKey,
            request.Environment,
            profileId.Value,
            balanceId,
            request.Since,
            DateOnly.FromDateTime(DateTime.UtcNow),
            cancellationToken
        );
        if (statement.IsFailure)
        {
            return Result.Fail<ProviderSyncResult>(statement.Error!);
        }

        var rows = statement.Value!.Select(Normalize).ToList();
        return Result.Ok(new ProviderSyncResult(SyncSource, rows, []));
    }

    /// <summary>Finds the profile that owns the linked balance (a token usually sees one profile).</summary>
    private async Task<Result<long>> ResolveProfileIdAsync(
        string token,
        ProviderEnvironment environment,
        long balanceId,
        CancellationToken cancellationToken
    )
    {
        var profiles = await client.GetProfilesAsync(token, environment, cancellationToken);
        if (profiles.IsFailure)
        {
            return Result.Fail<long>(profiles.Error!);
        }

        foreach (var profile in profiles.Value!)
        {
            var balances = await client.GetBalancesAsync(
                token,
                environment,
                profile.Id,
                cancellationToken
            );
            if (balances.IsFailure)
            {
                return Result.Fail<long>(balances.Error!);
            }
            if (balances.Value!.Any(b => b.Id == balanceId))
            {
                return Result.Ok(profile.Id);
            }
        }

        return Result.Fail<long>(
            $"No Wise profile reachable with this token holds balance {balanceId} — "
                + "check the balance id on the account (Settings → Accounts)."
        );
    }

    /// <summary>Maps one statement line to the shared import shape.</summary>
    internal static NormalizedTransaction Normalize(WiseStatementTransaction t) =>
        new(
            SourceRow: 0,
            BookingDate: t.Date,
            ValueDate: null,
            Amount: t.Amount,
            Currency: t.Currency,
            Counterparty: t.Counterparty,
            Description: t.Description ?? t.Counterparty ?? t.ReferenceNumber ?? "Wise transaction",
            ExternalId: t.ReferenceNumber,
            RawData: t.RawJson
        );
}
