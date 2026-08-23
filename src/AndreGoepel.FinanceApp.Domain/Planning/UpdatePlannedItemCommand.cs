using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Resources;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Planning;

public sealed record UpdatePlannedItemCommand(
    Guid Id,
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
    bool Active
);

public static class UpdatePlannedItemCommandHandler
{
    public static async Task<Result<PlannedItem>> Handle(
        UpdatePlannedItemCommand command,
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

        var item = await session.LoadAsync<PlannedItem>(command.Id, cancellationToken);
        if (item is null)
        {
            return Result.Fail<PlannedItem>(localizer["Error.PlannedItemNotFound"]);
        }

        item.Description = command.Description.Trim();
        item.Amount = command.Amount;
        item.CategoryId = command.CategoryId;
        item.Schedule = new PlannedSchedule(command.Frequency, command.StartDate, command.EndDate);
        item.ExpectedAccountId = command.ExpectedAccountId;
        item.CounterpartyPattern = string.IsNullOrWhiteSpace(command.CounterpartyPattern)
            ? null
            : command.CounterpartyPattern.Trim();
        item.AmountTolerance = Math.Max(0, command.AmountTolerance);
        item.DateWindowDays = Math.Max(0, command.DateWindowDays);
        item.Active = command.Active;

        session.Store(item);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(item);
    }
}
