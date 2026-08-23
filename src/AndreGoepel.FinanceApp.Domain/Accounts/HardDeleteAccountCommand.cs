using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Crypto;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.Planning;
using AndreGoepel.FinanceApp.Domain.Resources;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Accounts;

/// <summary>
/// Purges an account and everything hanging off it: transaction event streams
/// and their projections, import batches, crypto holdings, plan-vs-actual
/// matches and review-queue entries. Irreversible — the transaction event
/// history goes with it, including the <c>TransactionCategoryCorrected</c>
/// events that fed rule learning. The learned <see cref="CategoryRule"/>s
/// themselves are keyed on text patterns, not on accounts, and survive.
/// <see cref="DeleteAccountCommand"/> stays the safe default: it refuses as soon
/// as the account has any transaction.
/// </summary>
public sealed record HardDeleteAccountCommand(Guid AccountId);

public static class HardDeleteAccountCommandHandler
{
    /// <summary>
    /// One session, one <c>SaveChangesAsync</c> — the whole cascade commits as a
    /// single Postgres transaction or not at all, so a failure can never leave
    /// transactions without their account or transfer links pointing at ghosts.
    /// </summary>
    public static async Task<Result<AccountDeletionImpact>> Handle(
        HardDeleteAccountCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        var account = await session.LoadAsync<Account>(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail<AccountDeletionImpact>(localizer["Error.AccountNotFound"]);
        }

        var targets = await AccountPurgeTargets.CollectAsync(
            session,
            command.AccountId,
            cancellationToken
        );

        await UnlinkTransferCounterpartsAsync(session, targets, cancellationToken);
        await DetachPlannedItemsAsync(session, targets, cancellationToken);

        foreach (var plannedMatchId in targets.PlannedMatchIds)
        {
            session.Delete<PlannedMatch>(plannedMatchId);
        }
        foreach (var suggestionId in targets.ReviewQueueIds)
        {
            session.Delete<CategorySuggestion>(suggestionId);
        }
        foreach (var batchId in targets.ImportBatchIds)
        {
            session.Delete<ImportBatch>(batchId);
        }
        foreach (var holdingId in targets.CryptoHoldingIds)
        {
            session.Delete<CryptoHolding>(holdingId);
        }
        foreach (var transactionId in targets.TransactionIds)
        {
            session.Delete<TransactionView>(transactionId);
        }

        QueueEventStreamDeletion(session, targets.TransactionIds);
        session.Delete(account);

        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(targets.ToImpact());
    }

    /// <summary>
    /// The other leg of a transfer lives on an account that is <em>not</em> being
    /// deleted, so it is unlinked rather than removed — as an event, like every
    /// other transfer change.
    /// </summary>
    private static async Task UnlinkTransferCounterpartsAsync(
        IDocumentSession session,
        AccountPurgeTargets targets,
        CancellationToken cancellationToken
    )
    {
        foreach (var counterpartId in targets.TransferCounterpartIds)
        {
            var stream = await session.Events.FetchForWriting<TransactionView>(
                counterpartId,
                cancellationToken
            );
            if (stream.Aggregate is { TransferCounterpartId: not null })
            {
                stream.AppendOne(new TransactionTransferUnlinked());
            }
        }
    }

    private static async Task DetachPlannedItemsAsync(
        IDocumentSession session,
        AccountPurgeTargets targets,
        CancellationToken cancellationToken
    )
    {
        foreach (var plannedItemId in targets.PlannedItemIdsToDetach)
        {
            var item = await session.LoadAsync<PlannedItem>(plannedItemId, cancellationToken);
            if (item is null)
            {
                continue;
            }

            item.ExpectedAccountId = null;
            session.Store(item);
        }
    }

    /// <summary>
    /// Marten has no session-scoped API for hard-deleting an event stream:
    /// <c>ArchiveStream</c> only flags rows, and
    /// <c>Advanced.Clean.DeleteSingleEventStreamAsync</c> issues these very deletes
    /// on its own connection, one round trip per stream, outside this unit of work.
    /// Queuing them instead keeps the event rows in the same transaction as every
    /// document change above. The events are deleted in a CTE of the same statement
    /// that drops the streams, so the foreign key from <c>mt_events</c> to
    /// <c>mt_streams</c> is satisfied without depending on how Marten orders the
    /// operations in its batch.
    /// </summary>
    private static void QueueEventStreamDeletion(
        IDocumentSession session,
        IReadOnlyList<Guid> transactionIds
    )
    {
        if (transactionIds.Count == 0)
        {
            return;
        }

        var streamIds = transactionIds.ToArray();
        var schema = session.DocumentStore.Options.Events.DatabaseSchemaName;
        session.QueueSqlCommand(
            $"with purged_events as (delete from {schema}.mt_events where stream_id = any(?)) "
                + $"delete from {schema}.mt_streams where id = any(?)",
            streamIds,
            streamIds
        );
    }
}
