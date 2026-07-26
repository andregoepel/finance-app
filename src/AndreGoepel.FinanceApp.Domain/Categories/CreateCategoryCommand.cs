using AndreGoepel.Core;
using Marten;

namespace AndreGoepel.FinanceApp.Domain.Categories;

public sealed record CreateCategoryCommand(string Name, Guid? ParentId);

public static class CreateCategoryCommandHandler
{
    public static async Task<Result<Category>> Handle(
        CreateCategoryCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Fail<Category>("Category name is required.");
        }

        if (command.ParentId is Guid parentId)
        {
            var parent = await session.LoadAsync<Category>(parentId, cancellationToken);
            if (parent is null)
            {
                return Result.Fail<Category>("Parent category not found.");
            }
            if (parent.ParentId is not null)
            {
                return Result.Fail<Category>(
                    "Categories support two levels only (group > category)."
                );
            }
        }

        var siblings = await session
            .Query<Category>()
            .Where(c => c.ParentId == command.ParentId)
            .ToListAsync(cancellationToken);

        var category = new Category
        {
            Name = command.Name.Trim(),
            ParentId = command.ParentId,
            SortOrder = siblings.Count == 0 ? 0 : siblings.Max(c => c.SortOrder) + 1,
        };
        session.Store(category);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(category);
    }
}
