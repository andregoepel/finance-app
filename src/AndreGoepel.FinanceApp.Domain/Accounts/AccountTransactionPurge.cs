using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.Planning;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;

namespace AndreGoepel.FinanceApp.Domain.Accounts;

/// <summary>
/// Removes an account's imported history: the transaction event streams and their
/// projections, the import batches that produced them, the plan matches and
/// review-queue entries hanging off them, and the transfer links held by legs on
/// other accounts. Shared by <see cref="HardDeleteAccountCommand"/>, which goes on
/// to drop the account itself, and <see cref="ClearAccountTransactionsCommand"/>,
/// which keeps it.
/// <para>
/// Nothing is committed here — everything is queued on the caller's session so the
/// whole cascade lands as a single Postgres transaction together with whatever else
/// that command removes.
/// </para>
/// </summary>
internal static class AccountTransactionPurge
{
    public static async Task ApplyAsync(
        IDocumentSession session,
        AccountPurgeTargets targets,
        CancellationToken cancellationToken
    )
    {
        await UnlinkTransferCounterpartsAsync(session, targets, cancellationToken);

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
        foreach (var transactionId in targets.TransactionIds)
        {
            session.Delete<TransactionView>(transactionId);
        }

        QueueEventStreamDeletion(session, targets.TransactionIds);
    }

    /// <summary>
    /// The other leg of a transfer lives on an account whose history is <em>not</em>
    /// being removed, so it is unlinked rather than deleted — as an event, like every
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
