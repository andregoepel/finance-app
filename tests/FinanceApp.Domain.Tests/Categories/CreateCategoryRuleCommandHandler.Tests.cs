using FinanceApp.Domain.Categories;
using Marten;
using NSubstitute;

namespace FinanceApp.Domain.Tests.Categories;

public class CreateCategoryRuleCommandHandlerTests
{
    private readonly IDocumentSession session = Substitute.For<IDocumentSession>();

    [Fact]
    public async Task Handle_WithoutAnyTextPattern_Fails()
    {
        // Act
        var result = await CreateCategoryRuleCommandHandler.Handle(
            new CreateCategoryRuleCommand(
                Guid.NewGuid(),
                CounterpartyContains: "  ",
                DescriptionContains: null,
                MinAmount: -100m,
                MaxAmount: 0m,
                CategoryRuleSource.Manual
            ),
            session,
            CancellationToken.None
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("at least", result.Error);
    }

    [Fact]
    public async Task Handle_MinAboveMax_Fails()
    {
        // Act
        var result = await CreateCategoryRuleCommandHandler.Handle(
            new CreateCategoryRuleCommand(
                Guid.NewGuid(),
                "rewe",
                null,
                MinAmount: 10m,
                MaxAmount: -10m,
                CategoryRuleSource.Manual
            ),
            session,
            CancellationToken.None
        );

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Handle_UnknownCategory_Fails()
    {
        // Act
        var result = await CreateCategoryRuleCommandHandler.Handle(
            new CreateCategoryRuleCommand(
                Guid.NewGuid(),
                "rewe",
                null,
                null,
                null,
                CategoryRuleSource.Manual
            ),
            session,
            CancellationToken.None
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Category not found", result.Error);
    }
}
