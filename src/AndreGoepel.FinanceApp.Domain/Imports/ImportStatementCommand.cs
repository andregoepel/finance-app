using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;

namespace AndreGoepel.FinanceApp.Domain.Imports;

/// <summary>
/// Imports parsed statement rows into an account: dedup against existing
/// transactions, one <see cref="TransactionImported"/> stream per new row, and
/// an <see cref="ImportBatch"/> audit record. Re-running the same file is a
/// no-op apart from a new batch record with <c>DuplicateCount == TotalRows</c>.
/// </summary>
public sealed record ImportStatementCommand(
    Guid AccountId,
    string FileName,
    string ParserId,
    IReadOnlyList<NormalizedTransaction> Rows,
    IReadOnlyList<ImportRowError> ParseErrors,
    string? ImportedBy
);

public static class ImportStatementCommandHandler
{
    public static async Task<Result<ImportBatch>> Handle(
        ImportStatementCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        var account = await session.LoadAsync<Account>(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail<ImportBatch>("Account not found.");
        }

        var hashedRows = command
            .Rows.Select(row => new HashedRow(
                row,
                DedupHash.Compute(account.Id, row.BookingDate, row.Amount, row.Description)
            ))
            .ToList();

        var candidateHashes = hashedRows.Select(hashed => hashed.Hash).Distinct().ToArray();
        var existingHashes = await session
            .Query<TransactionView>()
            .Where(t => t.AccountId == account.Id && t.DedupHash.IsOneOf(candidateHashes))
            .Select(t => t.DedupHash)
            .ToListAsync(cancellationToken);

        var (newRows, duplicateCount) = SplitNewRows(hashedRows, existingHashes.ToHashSet());

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
                    row.RawData
                )
            );
        }

        session.Store(batch);
        await session.SaveChangesAsync(cancellationToken);

        // Categorization is kicked off by the caller after this returns (a
        // top-level PublishAsync from application code, the same path the
        // foundation's email uses), not from inside this handler: publishing a
        // routed message from within an InvokeAsync'd handler does not reliably
        // deliver it. Failures there never affect this import.
        return Result.Ok(batch);
    }

    /// <summary>
    /// Splits rows into new vs. duplicate — against both the database and
    /// earlier rows of the same file (re-exported files often repeat bookings).
    /// </summary>
    internal static (List<HashedRow> NewRows, int DuplicateCount) SplitNewRows(
        IReadOnlyList<HashedRow> rows,
        HashSet<string> existingHashes
    )
    {
        var newRows = new List<HashedRow>();
        var duplicateCount = 0;
        foreach (var hashed in rows)
        {
            if (existingHashes.Add(hashed.Hash))
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
