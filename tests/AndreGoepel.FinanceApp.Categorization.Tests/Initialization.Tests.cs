using AndreGoepel.FinanceApp.Categorization;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.FinanceApp.Categorization.Tests;

public sealed class InitializationTests
{
    [Fact]
    public void AddCategorization_ReturnsSameServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddCategorization();

        // Assert
        Assert.Same(services, result);
    }
}
