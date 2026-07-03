using FinanceApp.Domain.Categories;
using Marten;

namespace FinanceApp.Domain.Transactions;

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
        CancellationToken cancellationToken
    )
    {
        var category = await session.LoadAsync<Category>(command.CategoryId, cancellationToken);
        if (category is null)
        {
            return Result.Fail("Category not found.");
        }

        var stream = await session.Events.FetchForWriting<TransactionView>(
            command.TransactionId,
            cancellationToken
        );
        if (stream.Aggregate is null)
        {
            return Result.Fail("Transaction not found.");
        }

        if (stream.Aggregate.CategoryId == command.CategoryId)
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
