using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Resources;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Categories;

public sealed record RenameCategoryCommand(Guid CategoryId, string Name);

public static class RenameCategoryCommandHandler
{
    public static async Task<Result<Category>> Handle(
        RenameCategoryCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Fail<Category>(localizer["Error.CategoryNameRequired"]);
        }

        var category = await session.LoadAsync<Category>(command.CategoryId, cancellationToken);
        if (category is null)
        {
            return Result.Fail<Category>(localizer["Error.CategoryNotFound"]);
        }

        category.Name = command.Name.Trim();
        session.Store(category);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(category);
    }
}
