using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.Resources;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Transactions;

/// <summary>
/// Records a transaction typed in by hand on a manually maintained account (cash).
/// It takes the same shape as an imported row — one <see cref="TransactionImported"/>
/// stream plus a one-row <see cref="ImportBatch"/> for the audit trail — so the entry
/// shows up in the grid, the dashboards and the categorization pipeline like any
/// other transaction. The account's balance anchor moves along with it, so the
/// account always shows its ledger balance. Negative = expense, positive = income.
/// <paramref name="AmountEur"/> is resolved by the caller for non-EUR accounts (as
/// with <see cref="SetAccountBalanceCommand"/>); EUR entries carry their own.
/// </summary>
public sealed record RecordManualTransactionCommand(
    Guid AccountId,
    DateOnly BookingDate,
    decimal Amount,
    decimal? AmountEur,
    string Description,
    string? Counterparty,
    Guid? CategoryId,
    string? RecordedBy
);

public static class RecordManualTransactionCommandHandler
{
    /// <summary>Marks the one-row batches behind manual entries in the import history.</summary>
    public const string ParserId = "manual-entry";

    public static async Task<Result<TransactionView>> Handle(
        RecordManualTransactionCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(command.Description))
        {
            return Result.Fail<TransactionView>(localizer["Error.DescriptionRequired"]);
        }
        if (command.Amount == 0)
        {
            return Result.Fail<TransactionView>(localizer["Error.AmountMustNotBeZero"]);
        }

        var account = await session.LoadAsync<Account>(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail<TransactionView>(localizer["Error.AccountNotFound"]);
        }
        if (account.SyncMethod != SyncMethod.Manual)
        {
            return Result.Fail<TransactionView>(
                localizer["Error.ManualEntriesOnManualAccountsOnly"]
            );
        }
        if (
            command.CategoryId is Guid categoryId
            && await session.LoadAsync<Category>(categoryId, cancellationToken) is null
        )
        {
            return Result.Fail<TransactionView>(localizer["Error.CategoryNotFound"]);
        }

        var description = command.Description.Trim();
        var counterparty = string.IsNullOrWhiteSpace(command.Counterparty)
            ? null
            : command.Counterparty.Trim();
        var amountEur = ManualAccountLedger.IsEur(account) ? command.Amount : command.AmountEur;

        var batch = new ImportBatch
        {
            AccountId = account.Id,
            Provider = account.Provider,
            Source = description,
            ParserId = ParserId,
            ImportedBy = command.RecordedBy,
            TotalRows = 1,
            ImportedCount = 1,
        };

        var transactionId = Guid.CreateVersion7();
        var events = new List<object>
        {
            new TransactionImported(
                transactionId,
                account.Id,
                command.BookingDate,
                ValueDate: null,
                command.Amount,
                account.Currency,
                amountEur,
                counterparty,
                description,
                ExternalId: null,
                DedupHash.Compute(account.Id, command.BookingDate, command.Amount, description),
                batch.Id,
                RawData: null
            ),
        };
        if (command.CategoryId is Guid chosenCategoryId)
        {
            events.Add(new TransactionCategorized(chosenCategoryId, CategorySource.Manual, null));
        }
        session.Events.StartStream<TransactionView>(transactionId, events.ToArray());

        ManualAccountLedger.Move(account, command.Amount, amountEur, DateTimeOffset.UtcNow);
        session.Store(batch);
        session.Store(account);
        await session.SaveChangesAsync(cancellationToken);

        var view = await session.LoadAsync<TransactionView>(transactionId, cancellationToken);
        return Result.Ok(view!);
    }
}
