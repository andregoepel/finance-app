using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Resources;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Transactions;

/// <summary>
/// Manual categorization from the grid. The first assignment emits
/// <see cref="TransactionCategorized"/>; changing an existing category emits
/// <see cref="TransactionCategoryCorrected"/> (feeds rule learning in Phase 2).
/// </summary>
public sealed record CategorizeTransactionCommand(Guid TransactionId, Guid CategoryId);

public static class CategorizeTransactionCommandHandler
{
    public static async Task<Result> Handle(
        CategorizeTransactionCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        var category = await session.LoadAsync<Category>(command.CategoryId, cancellationToken);
        if (category is null)
        {
            return Result.Fail(localizer["Error.CategoryNotFound"]);
        }

        var stream = await session.Events.FetchForWriting<TransactionView>(
            command.TransactionId,
            cancellationToken
        );
        if (stream.Aggregate is null)
        {
            return Result.Fail(localizer["Error.TransactionNotFound"]);
        }

        // Only a true no-op skips the event: same category *and* the read model
        // already reflects it as categorized. A document written before
        // IsCategorized existed on TransactionView can carry a CategoryId with
        // IsCategorized still false/missing (a pre-split-feature deploy, for
        // instance) — re-picking the same category from the review queue must
        // still repair that instead of silently doing nothing.
        if (stream.Aggregate.CategoryId == command.CategoryId && stream.Aggregate.IsCategorized)
        {
            return Result.Ok();
        }

        object @event = stream.Aggregate.CategoryId is null
            ? new TransactionCategorized(command.CategoryId, CategorySource.Manual, null)
            : new TransactionCategoryCorrected(stream.Aggregate.CategoryId, command.CategoryId);
        stream.AppendOne(@event);

        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
