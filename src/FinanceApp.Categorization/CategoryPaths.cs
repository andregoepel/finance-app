using FinanceApp.Categorization.Claude;
using FinanceApp.Domain.Categories;

namespace FinanceApp.Categorization;

/// <summary>Builds display paths ("Living › Groceries") for the category tree.</summary>
public static class CategoryPaths
{
    public static List<CategoryOption> Build(IReadOnlyList<Category> categories)
    {
        var byId = categories.ToDictionary(category => category.Id);
        return categories
            .OrderBy(category =>
                byId.TryGetValue(category.ParentId ?? category.Id, out var parent)
                    ? parent.SortOrder
                    : 0
            )
            .ThenBy(category => category.ParentId is null ? -1 : category.SortOrder)
            .Select(category => new CategoryOption(category.Id, PathOf(category, byId)))
            .ToList();
    }

    public static string PathOf(Category category, IReadOnlyDictionary<Guid, Category> byId) =>
        category.ParentId is Guid parentId && byId.TryGetValue(parentId, out var parent)
            ? $"{parent.Name} › {category.Name}"
            : category.Name;
}
