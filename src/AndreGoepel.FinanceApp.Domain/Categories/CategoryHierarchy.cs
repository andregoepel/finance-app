namespace AndreGoepel.FinanceApp.Domain.Categories;

public static class CategoryHierarchy
{
    public static IReadOnlyList<Guid> InclusiveDescendantIds(
        IReadOnlyCollection<Category> categories,
        Guid categoryId
    )
    {
        var childrenByParent = categories
            .Where(category => category.ParentId is not null)
            .ToLookup(category => category.ParentId!.Value);
        var result = new List<Guid>();
        var pending = new Queue<Guid>();
        var visited = new HashSet<Guid>();
        pending.Enqueue(categoryId);

        while (pending.TryDequeue(out var current))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            result.Add(current);
            foreach (var child in childrenByParent[current])
            {
                pending.Enqueue(child.Id);
            }
        }

        return result;
    }
}
