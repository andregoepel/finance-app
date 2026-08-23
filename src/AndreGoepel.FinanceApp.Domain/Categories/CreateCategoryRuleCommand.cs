using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Resources;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Categories;

public sealed record CreateCategoryRuleCommand(
    Guid CategoryId,
    string? CounterpartyContains,
    string? DescriptionContains,
    decimal? MinAmount,
    decimal? MaxAmount,
    CategoryRuleSource Source
);

public static class CreateCategoryRuleCommandHandler
{
    public static async Task<Result<CategoryRule>> Handle(
        CreateCategoryRuleCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        var counterparty = Normalize(command.CounterpartyContains);
        var description = Normalize(command.DescriptionContains);
        if (counterparty is null && description is null)
        {
            return Result.Fail<CategoryRule>(
                "A rule needs at least a counterparty or description pattern."
            );
        }
        if (command.MinAmount is decimal min && command.MaxAmount is decimal max && min > max)
        {
            return Result.Fail<CategoryRule>(localizer["Error.MinExceedsMax"]);
        }

        var category = await session.LoadAsync<Category>(command.CategoryId, cancellationToken);
        if (category is null)
        {
            return Result.Fail<CategoryRule>(localizer["Error.CategoryNotFound"]);
        }

        var duplicate = await session
            .Query<CategoryRule>()
            .Where(rule =>
                rule.CategoryId == command.CategoryId
                && rule.CounterpartyContains == counterparty
                && rule.DescriptionContains == description
            )
            .FirstOrDefaultAsync(cancellationToken);
        if (duplicate is not null)
        {
            return Result.Ok(duplicate);
        }

        var newRule = new CategoryRule
        {
            CategoryId = command.CategoryId,
            CounterpartyContains = counterparty,
            DescriptionContains = description,
            MinAmount = command.MinAmount,
            MaxAmount = command.MaxAmount,
            Source = command.Source,
        };
        session.Store(newRule);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(newRule);
    }

    private static string? Normalize(string? pattern) =>
        string.IsNullOrWhiteSpace(pattern) ? null : pattern.Trim();
}
