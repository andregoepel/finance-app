using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Credentials;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Providers.Wise;

/// <summary>
/// Syncs Wise transactions through the token-only activity feed (personal
/// accounts cannot register SCA public keys anymore, so the SCA-protected
/// statement endpoints are unusable). The account's <c>ExternalId</c> is the
/// Wise balance id — used to verify the token actually sees the linked balance
/// and to resolve the owning profile. The feed is profile-wide and carries no
/// balance attribution, so all activities of a currency book onto that
/// currency's <b>standard</b> balance account only; jar (SAVINGS) accounts are
/// balance-only — anything else would import the same movement once per jar.
/// Only COMPLETED entries import (in-progress ones still change).
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
        if (string.IsNullOrWhiteSpace(request.Currency))
        {
            return Result.Fail<ProviderSyncResult>(
                "The account has no currency — Wise activities are filtered per currency."
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

        var resolved = await ResolveBalanceAsync(
            token,
            request.Environment,
            balanceId,
            cancellationToken
        );
        if (resolved.IsFailure)
        {
            return Result.Fail<ProviderSyncResult>(resolved.Error!);
        }
        var (profileId, balance) = resolved.Value!;

        // The activity feed cannot be attributed per balance, so jars stay
        // balance-only: their money movements are jar shuffles that book (once)
        // on the standard account. Returning success keeps scheduled runs quiet.
        if (!string.Equals(balance.Type, "STANDARD", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Ok(new ProviderSyncResult(SyncSource, [], []));
        }

        var activities = await client.GetActivitiesAsync(
            token,
            request.Environment,
            profileId,
            request.Since,
            DateOnly.FromDateTime(DateTime.UtcNow),
            cancellationToken
        );
        if (activities.IsFailure)
        {
            return Result.Fail<ProviderSyncResult>(activities.Error!);
        }

        var rows = activities
            .Value!.Where(a =>
                string.Equals(a.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
            )
            .Select(a => MapForCurrency(a, request.Currency!))
            .OfType<NormalizedTransaction>()
            .ToList();

        return Result.Ok(new ProviderSyncResult(SyncSource, rows, []));
    }

    /// <summary>
    /// Finds the profile that owns the linked balance (a token usually sees one
    /// profile) and the balance itself, whose type decides jar vs. standard.
    /// </summary>
    private async Task<Result<(long ProfileId, WiseBalance Balance)>> ResolveBalanceAsync(
        string token,
        ProviderEnvironment environment,
        long balanceId,
        CancellationToken cancellationToken
    )
    {
        var profiles = await client.GetProfilesAsync(token, environment, cancellationToken);
        if (profiles.IsFailure)
        {
            return Result.Fail<(long, WiseBalance)>(profiles.Error!);
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
                return Result.Fail<(long, WiseBalance)>(balances.Error!);
            }
            if (balances.Value!.FirstOrDefault(b => b.Id == balanceId) is WiseBalance balance)
            {
                return Result.Ok((profile.Id, balance));
            }
        }

        return Result.Fail<(long, WiseBalance)>(
            $"No Wise profile reachable with this token holds balance {balanceId} — "
                + "check the balance id on the account (Settings → Accounts)."
        );
    }

    /// <summary>
    /// Maps one activity to the account being synced, or null when the activity
    /// does not touch that account's currency. Balance conversions (INTERBALANCE)
    /// book both sides: the secondary amount is the source currency spent (money
    /// out), the primary amount is the target currency received (money in) — the
    /// two rows land in their respective accounts and can be linked as a transfer.
    /// Same-currency conversions are jar shuffles: money moving between a
    /// standard balance and its jars never leaves the household and cannot be
    /// attributed a direction from the feed, so they are skipped (jar balances
    /// refresh separately).
    /// </summary>
    internal static NormalizedTransaction? MapForCurrency(WiseActivity a, string accountCurrency)
    {
        var isConversion = string.Equals(
            a.Type,
            "INTERBALANCE",
            StringComparison.OrdinalIgnoreCase
        );

        if (isConversion)
        {
            if (string.Equals(a.Currency, a.SecondaryCurrency, StringComparison.OrdinalIgnoreCase))
            {
                return null; // jar shuffle within one currency
            }
            if (
                string.Equals(
                    a.SecondaryCurrency,
                    accountCurrency,
                    StringComparison.OrdinalIgnoreCase
                ) && a.SecondaryAmount is decimal spent
            )
            {
                return Normalize(a, -Math.Abs(spent), a.SecondaryCurrency!);
            }
            if (string.Equals(a.Currency, accountCurrency, StringComparison.OrdinalIgnoreCase))
            {
                return Normalize(a, Math.Abs(a.Amount), a.Currency);
            }
            return null;
        }

        return string.Equals(a.Currency, accountCurrency, StringComparison.OrdinalIgnoreCase)
            ? Normalize(a, a.Amount, a.Currency)
            : null;
    }

    private static NormalizedTransaction Normalize(
        WiseActivity a,
        decimal amount,
        string currency
    ) =>
        new(
            SourceRow: 0,
            BookingDate: a.Date,
            ValueDate: null,
            Amount: amount,
            Currency: currency,
            Counterparty: a.Title,
            Description: string.IsNullOrWhiteSpace(a.Description)
                ? a.Title ?? "Wise transaction"
                : a.Description,
            ExternalId: a.Id,
            RawData: a.RawJson
        );
}
