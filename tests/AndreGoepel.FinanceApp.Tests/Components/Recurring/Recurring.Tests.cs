using AndreGoepel.Design.Blazor;
using AndreGoepel.FinanceApp.Domain.Recurring;
using AndreGoepel.FinanceApp.Insights;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Radzen;
using RecurringPage = AndreGoepel.FinanceApp.Components.Recurring.Pages.Recurring;

namespace AndreGoepel.FinanceApp.Tests.Components.Recurring;

/// <summary>
/// "Add as planned" used to be offered again after it had been used, so the same
/// series could be added any number of times. What the page shows for a series
/// that already has a planned item is therefore the behaviour worth pinning.
/// </summary>
public sealed class RecurringTests : LocalizedTestContext
{
    private void RegisterSeries(params RecurringSeries[] series)
    {
        var service = Substitute.For<IRecurringService>();
        service
            .GetAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RecurringSeries>>(series));
        Services.AddSingleton(service);
        Services.AddSingleton(Substitute.For<Wolverine.IMessageBus>());
        Services.AddSingleton(new NotificationService());
        // ConfirmService, used by the row's dismiss action.
        Services.AddDesignBlazor(options => options.BrandName = "Finance");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }
        return count;
    }

    private static RecurringSeries Series(string counterparty, bool alreadyPlanned) =>
        new(
            counterparty,
            -19.99m,
            RecurrenceInterval.Monthly,
            6,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 9, 1),
            alreadyPlanned
        );

    [Fact]
    public void Render_SeriesNotYetPlanned_OffersToAddIt()
    {
        // Arrange
        RegisterSeries(Series("Streaming", alreadyPlanned: false));

        // Act
        var cut = Render<RecurringPage>();

        // Assert
        Assert.Contains("Add as planned", cut.Markup);
        Assert.DoesNotContain(">Planned<", cut.Markup);
    }

    [Fact]
    public void Render_SeriesAlreadyPlanned_ShowsTheMarkInsteadOfTheButton()
    {
        // Arrange
        RegisterSeries(Series("Streaming", alreadyPlanned: true));

        // Act
        var cut = Render<RecurringPage>();

        // Assert — no second chance to add the same series.
        Assert.DoesNotContain("Add as planned", cut.Markup);
        Assert.Contains("Planned", cut.Markup);
    }

    [Fact]
    public void Render_AnySeries_OffersToDismissIt()
    {
        // Arrange — the dismiss action is offered regardless of planned state,
        // since a false positive is still a false positive either way.
        RegisterSeries(Series("Streaming", alreadyPlanned: true));

        // Act
        var cut = Render<RecurringPage>();

        // Assert
        Assert.Contains("Not actually recurring", cut.Markup);
    }

    [Fact]
    public void Render_MixedSeries_MarksOnlyTheOneThatIsPlanned()
    {
        // Arrange
        RegisterSeries(
            Series("Streaming", alreadyPlanned: true),
            Series("Gym", alreadyPlanned: false)
        );

        // Act
        var cut = Render<RecurringPage>();

        // Assert — one row keeps its button, the other does not.
        Assert.Contains("Streaming", cut.Markup);
        Assert.Contains("Gym", cut.Markup);
        Assert.Equal(1, CountOccurrences(cut.Markup, "Add as planned"));
    }
}
