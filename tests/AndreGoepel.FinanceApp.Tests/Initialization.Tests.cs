using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.FinanceApp.Tests;

public sealed class InitializationTests
{
    [Fact]
    public void AddFinanceApp_ReturnsSameServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddFinanceApp();

        // Assert
        Assert.Same(services, result);
    }
}
