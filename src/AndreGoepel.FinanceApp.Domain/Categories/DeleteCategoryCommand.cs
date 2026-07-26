using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;

namespace AndreGoepel.FinanceApp.Domain.Categories;

/// <summary>
/// Deletes a category — refused while child categories or transactions still
/// reference it, so history never points at a missing category.
/// </summary>
public sealed record DeleteCategoryCommand(Guid CategoryId);

public static class DeleteCategoryCommandHandler
{
    public static async Task<Result> Handle(
        DeleteCategoryCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        var category = await session.LoadAsync<Category>(command.CategoryId, cancellationToken);
        if (category is null)
        {
            return Result.Fail("Category not found.");
        }

        var hasChildren = await session
            .Query<Category>()
            .Where(c => c.ParentId == command.CategoryId)
            .AnyAsync(cancellationToken);
        if (hasChildren)
        {
            return Result.Fail("Delete or move the subcategories first.");
        }

        var isInUse = await session
            .Query<TransactionView>()
            .Where(t => t.CategoryId == command.CategoryId)
            .AnyAsync(cancellationToken);
        if (isInUse)
        {
            return Result.Fail("Category is used by existing transactions.");
        }

        session.Delete(category);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
