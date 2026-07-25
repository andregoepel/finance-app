using AndreGoepel.FinanceApp.Domain.Categories;

namespace AndreGoepel.FinanceApp.Domain.Tests.Categories;

public sealed class DefaultCategoriesTests
{
    [Fact]
    public void Build_DefaultTree_ProducesGroupsWithChildrenWiredToParents()
    {
        // Act
        var categories = DefaultCategories.Build();

        // Assert
        var groups = categories.Where(c => c.ParentId is null).ToList();
        Assert.Equal(DefaultCategories.Tree.Count, groups.Count);
        foreach (var child in categories.Where(c => c.ParentId is not null))
        {
            Assert.Contains(groups, g => g.Id == child.ParentId);
        }
    }

    [Fact]
    public void Build_DefaultTree_ContainsTheTransfersGroup()
    {
        // Act
        var categories = DefaultCategories.Build();

        // Assert
        var transfers = categories.Single(c => c.Name == "Transfers" && c.ParentId is null);
        Assert.Contains(categories, c => c.ParentId == transfers.Id && c.Name == "Own transfer");
    }

    [Fact]
    public void Build_DefaultTree_ProducesUniqueIds()
    {
        // Act
        var categories = DefaultCategories.Build();

        // Assert
        Assert.Equal(categories.Count, categories.Select(c => c.Id).Distinct().Count());
    }
}
