using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Resources;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Budgets;

/// <summary>
/// Creates a new budget period, or updates an existing one when <paramref name="Id"/>
/// is set. <paramref name="StartMonth"/>/<paramref name="EndMonth"/> are floored to
/// the first of their month; a <c>null</c> <paramref name="EndMonth"/> means ongoing.
/// </summary>
public sealed record SetBudgetCommand(
    Guid? Id,
    Guid CategoryId,
    decimal MonthlyLimit,
    DateOnly StartMonth,
    DateOnly? EndMonth
);

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

        var startMonth = FirstOfMonth(command.StartMonth);
        var endMonth = command.EndMonth is DateOnly e ? FirstOfMonth(e) : (DateOnly?)null;
        if (endMonth < startMonth)
        {
            return Result.Fail<Budget>(localizer["Error.BudgetEndBeforeStart"]);
        }

        var category = await session.LoadAsync<Category>(command.CategoryId, cancellationToken);
        if (category is null)
        {
            return Result.Fail<Budget>(localizer["Error.CategoryNotFound"]);
        }

        Budget? budget = null;
        if (command.Id is Guid id)
        {
            budget = await session.LoadAsync<Budget>(id, cancellationToken);
            if (budget is null)
            {
                return Result.Fail<Budget>(localizer["Error.BudgetNotFound"]);
            }
        }

        var otherPeriods = await session
            .Query<Budget>()
            .Where(b => b.CategoryId == command.CategoryId)
            .ToListAsync(cancellationToken);
        if (
            otherPeriods.Any(other =>
                other.Id != budget?.Id && Overlaps(other, startMonth, endMonth)
            )
        )
        {
            return Result.Fail<Budget>(localizer["Error.BudgetPeriodOverlap"]);
        }

        budget ??= new Budget
        {
            Id = Guid.NewGuid(),
            CategoryId = command.CategoryId,
            MonthlyLimit = command.MonthlyLimit,
            StartMonth = startMonth,
        };
        budget.CategoryId = command.CategoryId;
        budget.MonthlyLimit = command.MonthlyLimit;
        budget.StartMonth = startMonth;
        budget.EndMonth = endMonth;

        session.Store(budget);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(budget);
    }

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    /// <summary>Whether an existing budget period overlaps the given [start, end] range (both inclusive, open-ended when null).</summary>
    private static bool Overlaps(Budget other, DateOnly start, DateOnly? end) =>
        other.StartMonth <= (end ?? DateOnly.MaxValue)
        && (other.EndMonth ?? DateOnly.MaxValue) >= start;
}
