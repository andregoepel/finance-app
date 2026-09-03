using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Resources;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Transactions;

/// <summary>
/// Splits a transaction across two or more categories, each with its own share
/// of the amount. Replaces any prior single category or split. Manual only —
/// rules, history and AI always assign a single category; splitting is a
/// deliberate follow-up action from the transactions grid.
/// </summary>
public sealed record SplitTransactionCategoryCommand(
    Guid TransactionId,
    IReadOnlyList<CategoryLine> Lines
);

public static class SplitTransactionCategoryCommandHandler
{
    public static async Task<Result> Handle(
        SplitTransactionCategoryCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        if (command.Lines.Count < 2)
        {
            return Result.Fail(localizer["Error.SplitNeedsAtLeastTwoLines"]);
        }

        var stream = await session.Events.FetchForWriting<TransactionView>(
            command.TransactionId,
            cancellationToken
        );
        if (stream.Aggregate is null)
        {
            return Result.Fail(localizer["Error.TransactionNotFound"]);
        }

        var total = stream.Aggregate.Amount;
        decimal sum = 0;
        foreach (var line in command.Lines)
        {
            if (line.Amount == 0)
            {
                return Result.Fail(localizer["Error.SplitLineAmountZero"]);
            }
            if (Math.Sign(line.Amount) != Math.Sign(total))
            {
                return Result.Fail(localizer["Error.SplitLineWrongSign"]);
            }
            if (await session.LoadAsync<Category>(line.CategoryId, cancellationToken) is null)
            {
                return Result.Fail(localizer["Error.CategoryNotFound"]);
            }
            sum += line.Amount;
        }
        if (sum != total)
        {
            return Result.Fail(localizer["Error.SplitAmountsMustSumToTotal"]);
        }

        stream.AppendOne(new TransactionCategorySplit(command.Lines, CategorySource.Manual));
        session.Delete<CategorySuggestion>(command.TransactionId);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
