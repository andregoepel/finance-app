using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Exchange;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.Planning;
using AndreGoepel.FinanceApp.Domain.Providers;
using Marten;
using Wolverine;

namespace AndreGoepel.FinanceApp.Sync;

/// <summary>
/// Implements <see cref="IAccountSyncService"/>: hands connector-fetched rows to
/// the same <see cref="ImportStatementCommand"/> pipeline a CSV upload uses.
/// </summary>
internal sealed class AccountSyncService(
    IQuerySession querySession,
    IProviderConnectorRegistry connectorRegistry,
    IMessageBus messageBus
) : IAccountSyncService
{
    // Restricted-mode PSD2 history is ~90 days; default the first window to that.
    private static readonly int DefaultWindowDays = 90;

    // Re-fetch a few days before the last sync so late-posted bookings are not missed.
    private static readonly int OverlapDays = 3;

    public async Task<AccountSyncSummary> SyncAccountAsync(
        Guid accountId,
        string? triggeredBy,
        CancellationToken cancellationToken = default
    )
    {
        var account = await querySession.LoadAsync<Account>(accountId, cancellationToken);
        if (account is null)
        {
            return Failed(accountId, "Account", "Account not found.");
        }
        if (account.SyncMethod != SyncMethod.Api)
        {
            return Failed(account.Id, account.Name, "Account is import-only (no API sync).");
        }
        if (account.ConnectionId is not Guid connectionId)
        {
            return Failed(
                account.Id,
                account.Name,
                "Account is not linked to a connection (Settings → Accounts)."
            );
        }

        var connection = await querySession.LoadAsync<ProviderConnection>(
            connectionId,
            cancellationToken
        );
        if (connection is null)
        {
            return Failed(
                account.Id,
                account.Name,
                "The account's connection no longer exists — re-link it (Settings → Accounts)."
            );
        }

        var connectorResult = connectorRegistry.ForProvider(account.Provider);
        if (connectorResult.IsFailure)
        {
            return Failed(account.Id, account.Name, connectorResult.Error!);
        }

        var request = await BuildRequestAsync(account, connection, cancellationToken);

        var fetch = await connectorResult.Value!.FetchAsync(request, cancellationToken);
        if (fetch.IsFailure)
        {
            return Failed(account.Id, account.Name, fetch.Error!);
        }

        var import = await messageBus.InvokeAsync<Result<ImportBatch>>(
            new ImportStatementCommand(
                account.Id,
                // Deliberately English and ISO-formatted, and deliberately NOT localized: this is
                // persisted into ImportBatch.Source and shown later in the Import history grid.
                // Localizing at write time would freeze whichever culture was active during the
                // sync, leaving a permanent mix of languages that no later culture switch could
                // fix. A stored provenance marker, not display copy.
                $"API sync {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC",
                fetch.Value!.SyncSource,
                fetch.Value.Rows,
                fetch.Value.Errors,
                triggeredBy
            ),
            cancellationToken
        );
        if (import.IsFailure)
        {
            return Failed(account.Id, account.Name, import.Error!);
        }

        // Fire-and-forget EUR conversion + categorization — the same top-level
        // publish the upload page uses. Neither blocks or fails a sync.
        if (import.Value!.ImportedCount > 0)
        {
            await messageBus.PublishAsync(new ConvertPendingTransactionsToEurCommand());
            await messageBus.PublishAsync(
                new CategorizeImportedTransactionsCommand(import.Value.Id)
            );
            await messageBus.PublishAsync(new MatchPlannedTransactionsCommand());
        }

        return new AccountSyncSummary(
            account.Id,
            account.Name,
            Success: true,
            Error: null,
            import.Value.ImportedCount,
            import.Value.DuplicateCount
        );
    }

    public async Task<IReadOnlyList<AccountSyncSummary>> SyncAllAsync(
        string? triggeredBy,
        CancellationToken cancellationToken = default
    )
    {
        var apiAccounts = await querySession
            .Query<Account>()
            .Where(a => a.SyncMethod == SyncMethod.Api && a.Status == AccountStatus.Active)
            .ToListAsync(cancellationToken);

        var summaries = new List<AccountSyncSummary>(apiAccounts.Count);
        foreach (var account in apiAccounts)
        {
            summaries.Add(await SyncAccountAsync(account.Id, triggeredBy, cancellationToken));
        }
        return summaries;
    }

    // Builds the connector request: window start from the last sync (with overlap) or the default backfill, plus the Enable Banking session where the provider needs it.
    private async Task<ProviderSyncRequest> BuildRequestAsync(
        Account account,
        ProviderConnection connection,
        CancellationToken cancellationToken
    )
    {
        var lastBatch = await querySession
            .Query<ImportBatch>()
            .Where(b => b.AccountId == account.Id)
            .OrderByDescending(b => b.ImportedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var since = lastBatch is null
            ? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-DefaultWindowDays)
            : DateOnly.FromDateTime(lastBatch.ImportedAt.UtcDateTime).AddDays(-OverlapDays);

        // For Enable Banking, resolve the current session account uid from the
        // connection's linked accounts by the stable identification hash (uids
        // rotate every re-consent). Only while the consent is active.
        var providerAccountReference = connection
            is { ConsentStatus: ConsentStatus.Authorized, ConsentExpired: false }
            ? connection
                .LinkedAccounts.FirstOrDefault(a =>
                    a.IdentificationHash == account.IdentificationHash
                )
                ?.Uid
            : null;

        return new ProviderSyncRequest(
            account.Id,
            account.Provider,
            connection.Id,
            account.ExternalId,
            account.IdentificationHash,
            providerAccountReference,
            since,
            connection.Environment,
            account.Currency
        );
    }

    private static AccountSyncSummary Failed(Guid accountId, string name, string error) =>
        new(accountId, name, Success: false, error, Imported: 0, Duplicates: 0);
}
