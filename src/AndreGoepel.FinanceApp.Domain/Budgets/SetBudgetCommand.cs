using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Resources;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Budgets;

/// <summary>Sets (or updates) the monthly EUR limit for a category.</summary>
public sealed record SetBudgetCommand(Guid CategoryId, decimal MonthlyLimit);

public static class SetBudgetCommandHandler
{
    public static async Task<Result<Budget>> Handle(
        SetBudgetCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        if (command.MonthlyLimit <= 0)
        {
            return Result.Fail<Budget>(localizer["Error.BudgetLimitPositive"]);
        }

        var category = await session.LoadAsync<Category>(command.CategoryId, cancellationToken);
        if (category is null)
        {
            return Result.Fail<Budget>(localizer["Error.CategoryNotFound"]);
        }

        var budget =
            await session.LoadAsync<Budget>(command.CategoryId, cancellationToken)
            ?? new Budget { Id = command.CategoryId, MonthlyLimit = command.MonthlyLimit };
        budget.MonthlyLimit = command.MonthlyLimit;
        session.Store(budget);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(budget);
    }
}
