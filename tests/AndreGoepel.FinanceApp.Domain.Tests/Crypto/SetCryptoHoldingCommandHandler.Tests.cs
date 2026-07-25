using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Crypto;
using AndreGoepel.FinanceApp.Domain.Providers;
using Marten;
using NSubstitute;

namespace AndreGoepel.FinanceApp.Domain.Tests.Crypto;

public sealed class SetCryptoHoldingCommandHandlerTests
{
    private readonly IDocumentSession session = Substitute.For<IDocumentSession>();

    [Theory]
    [InlineData("", "bitcoin", 1)]
    [InlineData("BTC", "  ", 1)]
    [InlineData("BTC", "bitcoin", 0)]
    [InlineData("BTC", "bitcoin", -0.5)]
    public async Task Handle_InvalidInput_Fails(string symbol, string coinGeckoId, double quantity)
    {
        // Act
        var result = await SetCryptoHoldingCommandHandler.Handle(
            new SetCryptoHoldingCommand(Guid.NewGuid(), symbol, coinGeckoId, (decimal)quantity),
            session,
            TimeProvider.System,
            CancellationToken.None
        );

        // Assert
        Assert.True(result.IsFailure);
        session.DidNotReceive().Store(Arg.Any<CryptoHolding>());
    }

    [Fact]
    public async Task Handle_UnknownAccount_Fails()
    {
        // Act
        var result = await SetCryptoHoldingCommandHandler.Handle(
            new SetCryptoHoldingCommand(Guid.NewGuid(), "BTC", "bitcoin", 1m),
            session,
            TimeProvider.System,
            CancellationToken.None
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task Handle_NonCryptoAccount_Fails()
    {
        // Arrange
        var account = CryptoAccount(AccountType.Checking);
        session.LoadAsync<Account>(account.Id, Arg.Any<CancellationToken>()).Returns(account);

        // Act
        var result = await SetCryptoHoldingCommandHandler.Handle(
            new SetCryptoHoldingCommand(account.Id, "BTC", "bitcoin", 1m),
            session,
            TimeProvider.System,
            CancellationToken.None
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("crypto accounts", result.Error);
    }

    [Fact]
    public async Task Handle_NewHolding_StoresWithCompositeKeyAndNormalizedId()
    {
        // Arrange
        var account = CryptoAccount(AccountType.Crypto);
        session.LoadAsync<Account>(account.Id, Arg.Any<CancellationToken>()).Returns(account);

        // Act — the id arrives untrimmed and mixed-case.
        var result = await SetCryptoHoldingCommandHandler.Handle(
            new SetCryptoHoldingCommand(account.Id, " BTC ", " Bitcoin ", 0.25m),
            session,
            TimeProvider.System,
            CancellationToken.None
        );

        // Assert
        Assert.True(result.IsSuccess);
        var holding = result.Value!;
        Assert.Equal(CryptoHolding.KeyFor(account.Id, "bitcoin"), holding.Id);
        Assert.Equal("bitcoin", holding.CoinGeckoId);
        Assert.Equal("BTC", holding.Symbol);
        Assert.Equal(0.25m, holding.Quantity);
        session.Received(1).Store(holding);
        await session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingHolding_UpdatesQuantityInsteadOfDuplicating()
    {
        // Arrange
        var account = CryptoAccount(AccountType.Crypto);
        var key = CryptoHolding.KeyFor(account.Id, "bitcoin");
        var existing = new CryptoHolding
        {
            Id = key,
            AccountId = account.Id,
            Symbol = "BTC",
            CoinGeckoId = "bitcoin",
            Quantity = 0.1m,
        };
        session.LoadAsync<Account>(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        session.LoadAsync<CryptoHolding>(key, Arg.Any<CancellationToken>()).Returns(existing);

        // Act
        var result = await SetCryptoHoldingCommandHandler.Handle(
            new SetCryptoHoldingCommand(account.Id, "BTC", "bitcoin", 0.75m),
            session,
            TimeProvider.System,
            CancellationToken.None
        );

        // Assert — the loaded document is updated, not a fresh one stored.
        Assert.True(result.IsSuccess);
        Assert.Same(existing, result.Value);
        Assert.Equal(0.75m, existing.Quantity);
        session.Received(1).Store(existing);
    }

    [Fact]
    public async Task Handle_Delete_RemovesByCompositeKey()
    {
        // Arrange
        var accountId = Guid.NewGuid();

        // Act
        var result = await DeleteCryptoHoldingCommandHandler.Handle(
            new DeleteCryptoHoldingCommand(accountId, "bitcoin"),
            session,
            CancellationToken.None
        );

        // Assert
        Assert.True(result.IsSuccess);
        session.Received(1).Delete<CryptoHolding>(CryptoHolding.KeyFor(accountId, "bitcoin"));
        await session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static Account CryptoAccount(AccountType type) =>
        new()
        {
            Name = "Crypto.com",
            Provider = ProviderKind.Wise,
            Type = type,
            Currency = "EUR",
            SyncMethod = SyncMethod.CsvUpload,
        };
}
