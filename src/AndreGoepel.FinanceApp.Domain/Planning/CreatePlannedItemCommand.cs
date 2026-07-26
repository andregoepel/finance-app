using AndreGoepel.Core;
using Marten;

namespace AndreGoepel.FinanceApp.Domain.Planning;

public sealed record CreatePlannedItemCommand(
    string Description,
    decimal Amount,
    Guid? CategoryId,
    PlannedFrequency Frequency,
    DateOnly StartDate,
    DateOnly? EndDate,
    Guid? ExpectedAccountId,
    string? CounterpartyPattern,
    decimal AmountTolerance,
    int DateWindowDays
);

public static class CreatePlannedItemCommandHandler
{
    public static async Task<Result<PlannedItem>> Handle(
        CreatePlannedItemCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        var validation = PlannedItemValidation.Validate(
            command.Description,
            command.Amount,
            command.EndDate,
            command.StartDate
        );
        if (validation.IsFailure)
        {
            return Result.Fail<PlannedItem>(validation.Error);
        }

        var item = new PlannedItem
        {
            Description = command.Description.Trim(),
            Amount = command.Amount,
            CategoryId = command.CategoryId,
            Schedule = new PlannedSchedule(command.Frequency, command.StartDate, command.EndDate),
            ExpectedAccountId = command.ExpectedAccountId,
            CounterpartyPattern = string.IsNullOrWhiteSpace(command.CounterpartyPattern)
                ? null
                : command.CounterpartyPattern.Trim(),
            AmountTolerance = Math.Max(0, command.AmountTolerance),
            DateWindowDays = Math.Max(0, command.DateWindowDays),
        };
        session.Store(item);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(item);
    }
}

/// <summary>Shared create/update validation for planned items.</summary>
internal static class PlannedItemValidation
{
    public static Result Validate(
        string description,
        decimal amount,
        DateOnly? endDate,
        DateOnly startDate
    )
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return Result.Fail("A description is required.");
        }
        if (amount == 0)
        {
            return Result.Fail(
                "The amount must not be zero (negative = expense, positive = income)."
            );
        }
        if (endDate is DateOnly end && end < startDate)
        {
            return Result.Fail("The end date must not be before the start date.");
        }
        return Result.Ok();
    }
}
