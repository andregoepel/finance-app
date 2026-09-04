using AndreGoepel.FinanceApp.Categorization.Claude;
using AndreGoepel.FinanceApp.Components.Shared;
using Bunit;
using Radzen.Blazor;

namespace AndreGoepel.FinanceApp.Tests.Components.Shared;

public sealed class CategoryPickerTests : LocalizedTestContext
{
    [Fact]
    public void Render_WithPopupStyle_ForwardsItToDropdown()
    {
        // Arrange
        const string popupStyle = "width: 32rem; max-height: 200px;";

        // Act
        var cut = Render<CategoryPicker>(parameters =>
            parameters
                .Add(
                    picker => picker.Options,
                    [new CategoryOption(Guid.NewGuid(), "Housing › Rent")]
                )
                .Add(picker => picker.PopupStyle, popupStyle)
        );

        // Assert
        Assert.Equal(popupStyle, cut.FindComponent<RadzenDropDown<Guid?>>().Instance.PopupStyle);
    }

    [Fact]
    public void Render_WithoutPopupStyle_PreservesRadzenDefault()
    {
        // Act
        var cut = Render<CategoryPicker>(parameters =>
            parameters.Add(picker => picker.Options, Array.Empty<CategoryOption>())
        );

        // Assert
        Assert.Equal(
            "max-height:200px;overflow-x:hidden",
            cut.FindComponent<RadzenDropDown<Guid?>>().Instance.PopupStyle
        );
    }
}
