using AndreGoepel.Core;
using Marten;

namespace AndreGoepel.FinanceApp.Domain.Transactions;

/// <summary>
/// One-time repair for <see cref="TransactionView.IsCategorized"/>: documents
/// written before that field existed on <see cref="TransactionView"/> (any
/// deployment older than the split-category feature) have a real
/// <see cref="TransactionView.CategoryId"/> but never persisted the flag
/// alongside it, so they wrongly show up as uncategorized in Review. A split
/// transaction cannot be affected — <see cref="TransactionView.CategoryLines"/>
/// only exists on documents written by the same (current) code that always
/// sets both together. Not modeled as a domain event: this repairs a
/// denormalized read-model flag back in line with its source of truth
/// (<see cref="TransactionView.CategoryId"/>), it is not a category change.
/// Safe to run repeatedly — the second run finds nothing left to fix.
/// </summary>
public sealed record RepairCategorizedFlagCommand;

public static class RepairCategorizedFlagCommandHandler
{
    public static async Task<Result> Handle(
        RepairCategorizedFlagCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        var categorized = await session
            .Query<TransactionView>()
            .Where(t => t.CategoryId != null)
            .ToListAsync(cancellationToken);

        var stale = categorized.Where(t => !t.IsCategorized).ToList();
        if (stale.Count == 0)
        {
            return Result.Ok();
        }

        foreach (var transaction in stale)
        {
            transaction.IsCategorized = true;
            session.Store(transaction);
        }

        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
