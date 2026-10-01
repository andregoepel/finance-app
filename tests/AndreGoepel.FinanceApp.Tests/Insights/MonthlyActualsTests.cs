using AndreGoepel.FinanceApp.Domain.Planning;
using AndreGoepel.FinanceApp.Domain.Transactions;
using AndreGoepel.FinanceApp.Planning;

namespace AndreGoepel.FinanceApp.Tests.Insights;

public sealed class MonthlyActualsTests
{
    private static readonly DateOnly September = new(2026, 9, 1);

    [Theory]
    [InlineData(-1200, -300, -900)]
    [InlineData(-1000, -250, -750)]
    [InlineData(-1400, -350, -1050)]
    public void SharedBooking_UsesPlannedMonthsCategoriesAndProportions(
        decimal amount,
        decimal expectedCar,
        decimal expectedRent
    )
    {
        var car = Item(-300);
        var rent = Item(-900);
        var transaction = Transaction(amount);
        var matches = new[]
        {
            Match(car, transaction, September),
            Match(rent, transaction, September.AddMonths(1)),
        };
        var items = new[] { car, rent }.ToDictionary(item => item.Id);

        var september = MonthlyActuals.BuildLines(
            [transaction],
            matches,
            items,
            September,
            September.AddMonths(1)
        );
        var october = MonthlyActuals.BuildLines(
            [transaction],
            matches,
            items,
            September.AddMonths(1),
            September.AddMonths(2)
        );
        var november = MonthlyActuals.BuildLines(
            [transaction],
            matches,
            items,
            September.AddMonths(2),
            September.AddMonths(3)
        );

        Assert.Equal(expectedCar, Assert.Single(september).AmountEur);
        Assert.Equal(car.CategoryId, september[0].CategoryId);
        Assert.Equal(expectedRent, Assert.Single(october).AmountEur);
        Assert.Equal(rent.CategoryId, october[0].CategoryId);
        Assert.Empty(november);
        Assert.Equal(amount, september.Concat(october).Sum(line => line.AmountEur));
    }

    [Fact]
    public void Unmatching_RestoresBookingDateAndOriginalCategory()
    {
        var transaction = Transaction(-1200);
        var lines = MonthlyActuals.BuildLines(
            [transaction],
            [],
            new Dictionary<Guid, PlannedItem>(),
            September,
            September.AddMonths(1)
        );
        Assert.Equal(transaction.CategoryId, Assert.Single(lines).CategoryId);
        Assert.Equal(-1200m, lines[0].AmountEur);
    }

    [Fact]
    public void TransfersAreExcluded_AndUnconvertedAmountsStayUnknown()
    {
        var item = Item(-300);
        var transaction = Transaction(-300);
        transaction.AmountEur = null;
        var match = Match(item, transaction, September);
        var items = new Dictionary<Guid, PlannedItem> { [item.Id] = item };
        Assert.Null(
            Assert
                .Single(
                    MonthlyActuals.BuildLines(
                        [transaction],
                        [match],
                        items,
                        September,
                        September.AddMonths(1)
                    )
                )
                .AmountEur
        );
        transaction.TransferCounterpartId = Guid.NewGuid();
        Assert.Empty(
            MonthlyActuals.BuildLines(
                [transaction],
                [match],
                items,
                September,
                September.AddMonths(1)
            )
        );
    }

    [Fact]
    public void Allocation_ConservesFullPrecision_AndIncludesInactiveItems()
    {
        var transaction = Transaction(-100);
        var items = Enumerable.Range(0, 3).Select(_ => Item(-1)).ToDictionary(item => item.Id);
        items.Values.First().Active = false;
        var matches = items.Values.Select(item => Match(item, transaction, September)).ToList();
        var shares = PlannedAmountAllocator.Allocate(transaction.AmountEur, matches, items);
        Assert.Equal(3, shares.Count);
        Assert.Equal(-100m, shares.Values.Sum());
        Assert.All(shares.Values, value => Assert.InRange(value!.Value, -33.334m, -33.333m));
    }

    [Fact]
    public void MultipleBookingsForOneOccurrence_KeepBothActualAmounts()
    {
        var item = Item(1000);
        var first = Transaction(600);
        var second = Transaction(400);
        var lines = MonthlyActuals.BuildLines(
            [first, second],
            [Match(item, first, September), Match(item, second, September)],
            new Dictionary<Guid, PlannedItem> { [item.Id] = item },
            September,
            September.AddMonths(1)
        );
        Assert.Equal(2, lines.Count);
        Assert.Equal(1000m, lines.Sum(line => line.AmountEur));
    }

    private static PlannedItem Item(decimal amount) =>
        new()
        {
            Description = "Planned expense",
            Amount = amount,
            CategoryId = Guid.NewGuid(),
            Schedule = new PlannedSchedule(PlannedFrequency.Monthly, September),
        };

    private static TransactionView Transaction(decimal amount) =>
        new()
        {
            Id = Guid.NewGuid(),
            BookingDate = new DateOnly(2026, 9, 28),
            Amount = amount,
            AmountEur = amount,
            CategoryId = Guid.NewGuid(),
        };

    private static PlannedMatch Match(
        PlannedItem item,
        TransactionView transaction,
        DateOnly due
    ) =>
        new()
        {
            Id = PlannedMatch.KeyFor(item.Id, due, transaction.Id),
            PlannedItemId = item.Id,
            TransactionId = transaction.Id,
            DueDate = due,
        };
}
