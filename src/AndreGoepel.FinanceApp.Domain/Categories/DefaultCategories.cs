using JasperFx;
using Marten;
using Marten.Schema;

namespace AndreGoepel.FinanceApp.Domain.Categories;

/// <summary>
/// Default household category tree, seeded once into an empty database
/// (approved 2026-07-03). Fully editable afterwards via the Settings page.
/// </summary>
public static class DefaultCategories
{
    public static IReadOnlyList<(string Group, string[] Children)> Tree { get; } =
    [
        ("Income", ["Salary", "Side income", "Interest & dividends", "Other income"]),
        ("Housing", ["Rent & mortgage", "Utilities", "Internet & phone", "Household & furniture"]),
        ("Living", ["Groceries", "Restaurants & cafés", "Clothing", "Personal care"]),
        ("Mobility", ["Public transport", "Car", "Fuel", "Taxi & rideshare"]),
        ("Leisure", ["Entertainment", "Sports & hobbies", "Travel & holidays", "Subscriptions"]),
        ("Health", ["Medical", "Pharmacy", "Health insurance"]),
        ("Finance", ["Bank fees", "Insurance", "Taxes", "Investments", "Crypto"]),
        ("Family & gifts", ["Gifts", "Donations"]),
        ("Transfers", ["Own transfer", "Cash withdrawal"]),
        ("Other", []),
    ];

    public static List<Category> Build()
    {
        var categories = new List<Category>();
        var groupOrder = 0;
        foreach (var (group, children) in Tree)
        {
            var parent = new Category { Name = group, SortOrder = groupOrder++ };
            categories.Add(parent);
            var childOrder = 0;
            categories.AddRange(
                children.Select(child => new Category
                {
                    Name = child,
                    ParentId = parent.Id,
                    SortOrder = childOrder++,
                })
            );
        }
        return categories;
    }
}

/// <summary>Marten initial data: seeds the default tree only when no categories exist.</summary>
internal sealed class DefaultCategorySeed : IInitialData
{
    public async Task Populate(IDocumentStore store, CancellationToken cancellation)
    {
        await using var session = store.LightweightSession();
        var hasCategories = await session.Query<Category>().AnyAsync(cancellation);
        if (hasCategories)
        {
            return;
        }

        session.Store<Category>(DefaultCategories.Build().ToArray());
        await session.SaveChangesAsync(cancellation);
    }
}
