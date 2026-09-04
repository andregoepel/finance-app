using AndreGoepel.FinanceApp.Domain.Planning;
using AndreGoepel.FinanceApp.Planning;

namespace AndreGoepel.FinanceApp.Tests.Planning;

public sealed class EndOfMonthEstimatorTests
{
    [Fact]
    public void Calculate_AddsOnlyOutstandingPlannedAmountsToCurrentValue()
    {
        var occurrences = new[]
        {
            Create(200m, PlannedOccurrenceStatus.Pending),
            Create(-80m, PlannedOccurrenceStatus.Overdue),
            Create(-40m, PlannedOccurrenceStatus.Matched),
            Create(-20m, PlannedOccurrenceStatus.Skipped),
        };

        var result = EndOfMonthEstimator.Calculate(1_000m, occurrences);

        Assert.Equal(1_120m, result);
    }

    private static PlannedOccurrence Create(decimal amount, PlannedOccurrenceStatus status) =>
        new(
            Guid.NewGuid(),
            "Planned item",
            amount,
            null,
            DateOnly.FromDateTime(DateTime.Today),
            status,
            [],
            status == PlannedOccurrenceStatus.Matched ? amount : null
        );
}
