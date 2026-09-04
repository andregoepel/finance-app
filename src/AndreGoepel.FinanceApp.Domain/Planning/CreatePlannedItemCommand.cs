using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Resources;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Planning;

/// <param name="CreatedFromRecurringKey">
/// Set when the item is created from a detected recurring series, carrying that
/// series' key — see <see cref="PlannedItem.CreatedFromRecurringKey"/>. Null for
/// items entered by hand.
/// </param>
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
    int DateWindowDays,
    string? CreatedFromRecurringKey = null
);

public static class CreatePlannedItemCommandHandler
{
    public static async Task<Result<PlannedItem>> Handle(
        CreatePlannedItemCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        var validation = PlannedItemValidation.Validate(
            command.Description,
            command.Amount,
            command.EndDate,
            command.StartDate,
            localizer
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
            CreatedFromRecurringKey = string.IsNullOrWhiteSpace(command.CreatedFromRecurringKey)
                ? null
                : command.CreatedFromRecurringKey.Trim(),
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
        DateOnly startDate,
        IStringLocalizer<DomainStrings> localizer
    )
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return Result.Fail(localizer["Error.DescriptionRequired"]);
        }
        if (amount == 0)
        {
            return Result.Fail(localizer["Error.AmountMustNotBeZero"]);
        }
        if (endDate is DateOnly end && end < startDate)
        {
            return Result.Fail(localizer["Error.EndBeforeStart"]);
        }
        return Result.Ok();
    }
}
