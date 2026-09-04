using AndreGoepel.FinanceApp.Domain.Categories;

namespace AndreGoepel.FinanceApp.Domain.Tests.Categories;

public sealed class CategoryHierarchyTests
{
    [Fact]
    public void InclusiveDescendantIds_ReturnsSelectedCategoryAndAllDescendants()
    {
        var root = Category("Root");
        var child = Category("Child", root.Id);
        var grandchild = Category("Grandchild", child.Id);
        var unrelated = Category("Unrelated");

        var result = CategoryHierarchy.InclusiveDescendantIds(
            [root, child, grandchild, unrelated],
            root.Id
        );

        Assert.Equal([root.Id, child.Id, grandchild.Id], result);
    }

    [Fact]
    public void InclusiveDescendantIds_ReturnsOnlySelectedLeaf()
    {
        var root = Category("Root");
        var leaf = Category("Leaf", root.Id);

        var result = CategoryHierarchy.InclusiveDescendantIds([root, leaf], leaf.Id);

        Assert.Equal([leaf.Id], result);
    }

    private static Category Category(string name, Guid? parentId = null) =>
        new() { Name = name, ParentId = parentId };
}
