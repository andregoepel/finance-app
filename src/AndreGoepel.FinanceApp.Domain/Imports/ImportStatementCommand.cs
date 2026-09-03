using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Resources;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Imports;

/// <summary>
/// Imports parsed statement rows into an account: dedup against existing
/// transactions, one <see cref="TransactionImported"/> stream per new row, and
/// an <see cref="ImportBatch"/> audit record. Re-running the same file is a
/// no-op apart from a new batch record with <c>DuplicateCount == TotalRows</c>.
/// </summary>
/// <param name="ForceImportRows">
/// <see cref="NormalizedTransaction.SourceRow"/> values the household explicitly
/// wants imported despite deduping as an existing transaction — an override for
/// the rare case where two genuinely different bookings hash the same (e.g. two
/// identical small card charges the same day). Only used from the manual CSV
/// upload UI; API syncs never set it.
/// </param>
public sealed record ImportStatementCommand(
    Guid AccountId,
    string FileName,
    string ParserId,
    IReadOnlyList<NormalizedTransaction> Rows,
    IReadOnlyList<ImportRowError> ParseErrors,
    string? ImportedBy,
    IReadOnlySet<int>? ForceImportRows = null
);

public static class ImportStatementCommandHandler
{
    public static async Task<Result<ImportBatch>> Handle(
        ImportStatementCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        var account = await session.LoadAsync<Account>(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail<ImportBatch>(localizer["Error.AccountNotFound"]);
        }

        var hashedRows = command
            .Rows.Select(row => new HashedRow(
                row,
                DedupHash.Compute(account.Id, row.BookingDate, row.Amount, row.Description)
            ))
            .ToList();

        // A row carrying the provider's own reference (Enable Banking's EntryReference, mapped to
        // ExternalId) dedups on that instead of the hash: two distinct same-day, same-amount,
        // same-description transactions — ordinary card spending — would otherwise collapse into
        // one under the hash alone, which has no way to tell them apart.
        var candidateExternalIds = hashedRows
            .Where(hashed => !string.IsNullOrEmpty(hashed.Row.ExternalId))
            .Select(hashed => hashed.Row.ExternalId!)
            .Distinct()
            .ToArray();
        var existingExternalIds =
            candidateExternalIds.Length == 0
                ? []
                : await session
                    .Query<TransactionView>()
                    .Where(t =>
                        t.AccountId == account.Id
                        && t.ExternalId != null
                        && t.ExternalId.IsOneOf(candidateExternalIds)
                    )
                    .Select(t => t.ExternalId!)
                    .ToListAsync(cancellationToken);

        var candidateHashes = hashedRows
            .Where(hashed => string.IsNullOrEmpty(hashed.Row.ExternalId))
            .Select(hashed => hashed.Hash)
            .Distinct()
            .ToArray();
        var existingHashes =
            candidateHashes.Length == 0
                ? []
                : await session
                    .Query<TransactionView>()
                    .Where(t => t.AccountId == account.Id && t.DedupHash.IsOneOf(candidateHashes))
                    .Select(t => t.DedupHash)
                    .ToListAsync(cancellationToken);

        var (newRows, duplicateCount) = SplitNewRows(
            hashedRows,
            existingExternalIds.ToHashSet(),
            existingHashes.ToHashSet(),
            command.ForceImportRows
        );

        var batch = new ImportBatch
        {
            AccountId = account.Id,
            Provider = account.Provider,
            Source = command.FileName,
            ParserId = command.ParserId,
            ImportedBy = command.ImportedBy,
            TotalRows = command.Rows.Count + command.ParseErrors.Count,
            ImportedCount = newRows.Count,
            DuplicateCount = duplicateCount,
            Errors = command.ParseErrors,
        };

        foreach (var (row, hash) in newRows)
        {
            var transactionId = Guid.CreateVersion7();
            session.Events.StartStream<TransactionView>(
                transactionId,
                new TransactionImported(
                    transactionId,
                    account.Id,
                    row.BookingDate,
                    row.ValueDate,
                    row.Amount,
                    row.Currency,
                    row.Currency == "EUR" ? row.Amount : null,
                    row.Counterparty,
                    row.Description,
                    row.ExternalId,
                    hash,
                    batch.Id,
                    row.RawData,
                    OriginalAmount: row.OriginalAmount,
                    OriginalCurrency: row.OriginalCurrency
                )
            );
        }

        session.Store(batch);
        await session.SaveChangesAsync(cancellationToken);

        // Categorization is kicked off by the caller's top-level PublishAsync, not from inside this handler — publishing from within an InvokeAsync'd handler does not reliably deliver.
        return Result.Ok(batch);
    }

    /// <summary>
    /// Splits rows into new vs. duplicate — against both the database and earlier rows of the same
    /// file (re-exported files often repeat bookings). A row with a provider reference is checked
    /// against <paramref name="existingExternalIds"/>; every other row falls back to
    /// <paramref name="existingHashes"/>. Both sets are mutated in place, so two rows of the same
    /// file sharing a key dedup against each other too, not only against the database. A row whose
    /// <see cref="NormalizedTransaction.SourceRow"/> is in <paramref name="forceImportRows"/> is
    /// always treated as new — but its key is still recorded, so a later row in the same file
    /// sharing that key (not itself forced) is still caught as a duplicate.
    /// </summary>
    internal static (List<HashedRow> NewRows, int DuplicateCount) SplitNewRows(
        IReadOnlyList<HashedRow> rows,
        HashSet<string> existingExternalIds,
        HashSet<string> existingHashes,
        IReadOnlySet<int>? forceImportRows = null
    )
    {
        var newRows = new List<HashedRow>();
        var duplicateCount = 0;
        foreach (var hashed in rows)
        {
            var notPreviouslySeen = !string.IsNullOrEmpty(hashed.Row.ExternalId)
                ? existingExternalIds.Add(hashed.Row.ExternalId!)
                : existingHashes.Add(hashed.Hash);
            var isNew =
                notPreviouslySeen || (forceImportRows?.Contains(hashed.Row.SourceRow) ?? false);

            if (isNew)
            {
                newRows.Add(hashed);
            }
            else
            {
                duplicateCount++;
            }
        }
        return (newRows, duplicateCount);
    }

    internal sealed record HashedRow(NormalizedTransaction Row, string Hash);
}
