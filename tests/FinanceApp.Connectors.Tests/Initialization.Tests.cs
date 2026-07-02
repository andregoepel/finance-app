using FinanceApp.Connectors;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Connectors.Tests;

public class InitializationTests
{
    [Fact]
    public void AddConnectors_ReturnsSameServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddConnectors();

        // Assert
        Assert.Same(services, result);
    }
}
