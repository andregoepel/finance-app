using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Domain.Tests.Providers;

public sealed class ProviderConnectionTests
{
    [Theory]
    [InlineData(ProviderKind.Dkb, true)]
    [InlineData(ProviderKind.Revolut, true)]
    [InlineData(ProviderKind.Wise, false)]
    [InlineData(ProviderKind.EasyBank, false)]
    public void UsesEnableBanking_IsTrueOnlyForPsd2Providers(ProviderKind provider, bool expected)
    {
        // Arrange
        var connection = new ProviderConnection { Provider = provider, Label = "x" };

        // Assert
        Assert.Equal(expected, connection.UsesEnableBanking);
    }

    [Fact]
    public void ConsentExpired_AuthorizedAndPastExpiry_IsTrue()
    {
        // Arrange
        var connection = new ProviderConnection
        {
            Provider = ProviderKind.Dkb,
            Label = "DKB",
            ConsentStatus = ConsentStatus.Authorized,
            ConsentExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
        };

        // Assert
        Assert.True(connection.ConsentExpired);
    }

    [Fact]
    public void ConsentExpired_AuthorizedAndFutureExpiry_IsFalse()
    {
        // Arrange
        var connection = new ProviderConnection
        {
            Provider = ProviderKind.Dkb,
            Label = "DKB",
            ConsentStatus = ConsentStatus.Authorized,
            ConsentExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        };

        // Assert
        Assert.False(connection.ConsentExpired);
    }

    [Fact]
    public void ConsentExpired_NotAuthorized_IsFalseEvenWithoutExpiry()
    {
        // Arrange — a pending/never-connected consent is not "expired".
        var connection = new ProviderConnection
        {
            Provider = ProviderKind.Revolut,
            Label = "Revolut",
            ConsentStatus = ConsentStatus.Pending,
        };

        // Assert
        Assert.False(connection.ConsentExpired);
    }
}
