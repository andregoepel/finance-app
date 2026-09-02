using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Credentials;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Providers.Wise;

/// <summary>
/// Syncs Wise transactions through the token-only activity feed (personal
/// accounts cannot register SCA public keys anymore, so the SCA-protected,
/// per-balance statement endpoints are unusable). The account's <c>ExternalId</c>
/// is the Wise balance id — used to verify the token actually sees the linked
/// balance and to resolve the owning profile. The feed is profile-wide and
/// carries no balance attribution at all, so activities of a currency can only
/// ever book onto that currency's one <see cref="WiseBalance.Primary"/> STANDARD
/// balance; every other balance sharing that currency — a SAVINGS jar, or one of
/// Wise's "grouped" non-primary STANDARD balances — stays balance-only, since
/// syncing it too would duplicate the primary balance's whole history onto it.
/// Which currency an activity belongs to is the funding currency, not the
/// headline one — see <see cref="MapForCurrency"/> for point-of-sale conversions.
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

        if (
            !string.Equals(balance.Type, "SAVINGS", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(balance.Type, "STANDARD", StringComparison.OrdinalIgnoreCase)
        )
        {
            // Never guess: a missing/unrecognized type must not silently fall through
            // to a full activity sync (it would duplicate the standard balance's whole
            // transaction history onto whatever this balance actually is) nor silently
            // sync nothing either — both failure modes hide the problem instead of
            // surfacing it.
            return Result.Fail<ProviderSyncResult>(
                $"Wise balance {balanceId} has an unrecognized type "
                    + $"'{balance.Type}' (expected STANDARD or SAVINGS) — check it under "
                    + "Settings → Connections."
            );
        }

        // The activity feed is profile-wide, filtered only by currency — it cannot be
        // attributed to one specific balance. That is fine for the one true STANDARD
        // balance per currency (Primary), but a SAVINGS jar or a non-primary STANDARD
        // balance (Wise allows several "grouped" balances per currency) would each pull
        // in the primary balance's whole transaction history too. Both stay balance-only;
        // returning success keeps scheduled runs quiet.
        if (
            !(
                string.Equals(balance.Type, "STANDARD", StringComparison.OrdinalIgnoreCase)
                && balance.Primary
            )
        )
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
    /// does not touch that account's balance. Balance conversions (INTERBALANCE)
    /// book both sides: the secondary amount is the source currency spent (money
    /// out), the primary amount is the target currency received (money in) — the
    /// two rows land in their respective accounts and can be linked as a transfer.
    /// Same-currency conversions are jar shuffles: money moving between a
    /// standard balance and its jars never leaves the household and cannot be
    /// attributed a direction from the feed, so they are skipped (jar balances
    /// refresh separately).
    /// <para>
    /// Everything else books on the balance the money actually came from, which
    /// is not always the currency in the headline: paying by card abroad without
    /// holding that currency makes Wise convert at the point of sale, and the feed
    /// then shows the merchant's currency as the primary amount and the funding
    /// currency as the secondary one ("100.00 USD" / "92.00 EUR" = 92.00 EUR left
    /// the EUR balance, no USD balance was involved at all). So a differing
    /// secondary currency wins over the primary one, and the primary currency's
    /// account gets nothing — booking it there would invent a movement on a
    /// balance that never held money, while the real debit went missing.
    /// </para>
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

        // Point-of-sale conversion: the secondary currency is the funding balance.
        // Both amounts describe the same single movement, so the funding side keeps
        // the primary amount's direction rather than being forced negative.
        if (
            a.SecondaryAmount is decimal funded
            && !string.IsNullOrWhiteSpace(a.SecondaryCurrency)
            && !string.Equals(a.Currency, a.SecondaryCurrency, StringComparison.OrdinalIgnoreCase)
        )
        {
            return string.Equals(
                a.SecondaryCurrency,
                accountCurrency,
                StringComparison.OrdinalIgnoreCase
            )
                ? Normalize(
                    a,
                    a.Amount < 0 ? -Math.Abs(funded) : Math.Abs(funded),
                    a.SecondaryCurrency!
                )
                : null;
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
