using FinanceApp.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Domain.Tests;

public class InitializationTests
{
    [Fact]
    public void AddFinanceDomain_ReturnsSameServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddFinanceDomain();

        // Assert
        Assert.Same(services, result);
    }
}
