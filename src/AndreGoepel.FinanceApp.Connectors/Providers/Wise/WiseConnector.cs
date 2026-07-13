using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Domain.Credentials;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Providers.Wise;

/// <summary>
/// Syncs Wise transactions through the token-only activity feed (personal
/// accounts cannot register SCA public keys anymore, so the SCA-protected
/// statement endpoints are unusable). The account's <c>ExternalId</c> is the
/// Wise balance id — used to verify the token actually sees the linked balance
/// and to resolve the owning profile. Activities span every currency of the
/// profile, so only rows in the account's currency are kept; only COMPLETED
/// entries import (in-progress ones still change).
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

        var activities = await client.GetActivitiesAsync(
            token,
            request.Environment,
            profileId.Value,
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
                && string.Equals(a.Currency, request.Currency, StringComparison.OrdinalIgnoreCase)
            )
            .Select(Normalize)
            .ToList();

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

    /// <summary>Maps one activity to the shared import shape.</summary>
    internal static NormalizedTransaction Normalize(WiseActivity a) =>
        new(
            SourceRow: 0,
            BookingDate: a.Date,
            ValueDate: null,
            Amount: a.Amount,
            Currency: a.Currency,
            Counterparty: a.Title,
            Description: string.IsNullOrWhiteSpace(a.Description)
                ? a.Title ?? "Wise transaction"
                : a.Description,
            ExternalId: a.Id,
            RawData: a.RawJson
        );
}
