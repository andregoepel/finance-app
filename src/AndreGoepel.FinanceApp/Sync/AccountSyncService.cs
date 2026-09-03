using System.Diagnostics;
using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Exchange;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.Planning;
using AndreGoepel.FinanceApp.Domain.Providers;
using AndreGoepel.FinanceApp.Domain.Transactions;
using AndreGoepel.FinanceApp.Resources;
using Marten;
using Microsoft.Extensions.Localization;
using Wolverine;

namespace AndreGoepel.FinanceApp.Sync;

/// <summary>
/// Implements <see cref="IAccountSyncService"/>: hands connector-fetched rows to
/// the same <see cref="ImportStatementCommand"/> pipeline a CSV upload uses.
/// </summary>
internal sealed class AccountSyncService(
    IQuerySession querySession,
    IProviderConnectorRegistry connectorRegistry,
    IMessageBus messageBus,
    IStringLocalizer<Strings> localizer,
    ILogger<AccountSyncService> logger
) : IAccountSyncService
{
    // Restricted-mode PSD2 history is ~90 days; default Enable Banking's first window to that.
    // Wise has no such restriction, so its first sync goes straight to FullHistorySince.
    private static readonly int DefaultWindowDays = 90;

    // Re-fetch a few days before the last sync so late-posted bookings are not missed.
    private static readonly int OverlapDays = 3;

    /// <summary>
    /// "Since the beginning" anchor for a full-history sync — well before any
    /// household account existed, so it is effectively "everything the provider
    /// has". Not provider-specific: Enable Banking still only returns whatever its
    /// live PSD2 consent actually allows, regardless of how far back this asks.
    /// </summary>
    internal static readonly DateOnly FullHistorySince = new(2015, 1, 1);

    public async Task<AccountSyncSummary> SyncAccountAsync(
        Guid accountId,
        string? triggeredBy,
        bool fullHistory = false,
        CancellationToken cancellationToken = default
    )
    {
        var started = Stopwatch.GetTimestamp();
        var account = await querySession.LoadAsync<Account>(accountId, cancellationToken);
        if (account is null)
        {
            return Failed(accountId, "Account", localizer["Sync.AccountNotFound"]);
        }
        if (account.SyncMethod != SyncMethod.Api)
        {
            return Failed(account.Id, account.Name, localizer["Sync.ImportOnlyAccount"]);
        }
        if (account.ConnectionId is not Guid connectionId)
        {
            return Failed(
                account.Id,
                account.Name,
                localizer["Sync.AccountNotLinked", localizer["Nav.Accounts"]]
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
                localizer["Sync.ConnectionGone", localizer["Nav.Accounts"]]
            );
        }

        // Consent only exists for Enable Banking (Dkb/Revolut); Wise authenticates with a
        // plain API token and never touches ConsentStatus, so it would stay stuck at its
        // default NotConnected forever if these checks applied to it too.
        if (connection.UsesEnableBanking)
        {
            // Split before reaching the connector, which cannot tell these apart: it only sees a
            // null provider reference and used to blame "not linked" for both. Telling someone to
            // link an account they already linked is the wrong instruction for an expired consent.
            if (connection.ConsentExpired)
            {
                return Failed(
                    account.Id,
                    account.Name,
                    localizer[
                        "Sync.ConsentExpiredOn",
                        account.Provider,
                        // Non-null inside this branch: ConsentExpired compares ConsentExpiresAt to
                        // now, and a lifted comparison against null is false.
                        Dates.Short(connection.ConsentExpiresAt!.Value.ToLocalTime()),
                        localizer["Nav.Connections"]
                    ]
                );
            }
            if (connection.ConsentStatus != ConsentStatus.Authorized)
            {
                return Failed(
                    account.Id,
                    account.Name,
                    localizer[
                        "Sync.ConsentNotAuthorized",
                        account.Provider,
                        localizer["Nav.Connections"]
                    ]
                );
            }
        }

        var connectorResult = connectorRegistry.ForProvider(account.Provider);
        if (connectorResult.IsFailure)
        {
            return Failed(account.Id, account.Name, connectorResult.Error!);
        }

        var request = await BuildRequestAsync(account, connection, fullHistory, cancellationToken);

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
            await messageBus.PublishAsync(new MatchTransfersCommand());
            await messageBus.PublishAsync(
                new CategorizeImportedTransactionsCommand(import.Value.Id)
            );
            await messageBus.PublishAsync(new MatchPlannedTransactionsCommand());
        }

        logger.LogInformation(
            "Synced {Account}: {Imported} imported, {Duplicates} duplicates, in {ElapsedMs}ms.",
            account.Name,
            import.Value.ImportedCount,
            import.Value.DuplicateCount,
            (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds
        );

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
        bool fullHistory = false,
        Guid? connectionId = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = querySession
            .Query<Account>()
            .Where(a => a.SyncMethod == SyncMethod.Api && a.Status == AccountStatus.Active);
        if (connectionId is Guid id)
        {
            query = query.Where(a => a.ConnectionId == id);
        }
        var apiAccounts = await query.ToListAsync(cancellationToken);

        var summaries = new List<AccountSyncSummary>(apiAccounts.Count);
        foreach (var account in apiAccounts)
        {
            summaries.Add(
                await SyncAccountAsync(account.Id, triggeredBy, fullHistory, cancellationToken)
            );
        }
        return summaries;
    }

    // Builds the connector request: window start from the last sync (with overlap), the default
    // backfill (Enable Banking only — PSD2), or full history on request / for Wise's first sync.
    private async Task<ProviderSyncRequest> BuildRequestAsync(
        Account account,
        ProviderConnection connection,
        bool fullHistory,
        CancellationToken cancellationToken
    )
    {
        var lastBatch = await querySession
            .Query<ImportBatch>()
            .Where(b => b.AccountId == account.Id)
            .OrderByDescending(b => b.ImportedAt)
            .FirstOrDefaultAsync(cancellationToken);

        string windowDescription;
        DateOnly since;
        if (fullHistory)
        {
            since = FullHistorySince;
            windowDescription = "full history requested";
        }
        else if (lastBatch is not null)
        {
            since = DateOnly.FromDateTime(lastBatch.ImportedAt.UtcDateTime).AddDays(-OverlapDays);
            windowDescription = $"{OverlapDays}d overlap on last batch";
        }
        else if (connection.UsesEnableBanking)
        {
            // PSD2 restricted-mode consent only exposes ~90 days of history anyway.
            since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-DefaultWindowDays);
            windowDescription = $"first sync, {DefaultWindowDays}d backfill (Enable Banking)";
        }
        else
        {
            // Wise has no PSD2-style history cap, so its first sync goes straight to
            // full history instead of arbitrarily truncating at the EB default.
            since = FullHistorySince;
            windowDescription = "first sync, full history (Wise)";
        }

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

        // The three inputs that decide what a sync actually pulls. Worth a log line because two
        // of them fail quietly: a window that never widens past the overlap, and a linked-account
        // lookup that returns null after a re-consent changed the hash.
        logger.LogInformation(
            "Sync request for {Account}: since {Since} ({Window}), provider reference {Linked}.",
            account.Name,
            since,
            windowDescription,
            providerAccountReference is null ? "unresolved" : "resolved"
        );

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
