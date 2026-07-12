using AndreGoepel.FinanceApp.Domain.Accounts;
using Marten;

namespace AndreGoepel.FinanceApp.Domain.Crypto;

/// <summary>
/// Creates or updates the holding of one asset on a crypto account. The composite
/// document key makes saving the same (account, asset) pair an update of the
/// quantity rather than a duplicate.
/// </summary>
public sealed record SetCryptoHoldingCommand(
    Guid AccountId,
    string Symbol,
    string CoinGeckoId,
    decimal Quantity
);

public static class SetCryptoHoldingCommandHandler
{
    public static async Task<Result<CryptoHolding>> Handle(
        SetCryptoHoldingCommand command,
        IDocumentSession session,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(command.Symbol))
        {
            return Result.Fail<CryptoHolding>("A symbol is required.");
        }
        if (string.IsNullOrWhiteSpace(command.CoinGeckoId))
        {
            return Result.Fail<CryptoHolding>("A CoinGecko id is required.");
        }
        if (command.Quantity <= 0)
        {
            return Result.Fail<CryptoHolding>("The quantity must be greater than zero.");
        }

        var account = await session.LoadAsync<Account>(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail<CryptoHolding>("Account not found.");
        }
        if (account.Type != AccountType.Crypto)
        {
            return Result.Fail<CryptoHolding>("Holdings can only be added to crypto accounts.");
        }

        var key = CryptoHolding.KeyFor(command.AccountId, command.CoinGeckoId);
        var holding =
            await session.LoadAsync<CryptoHolding>(key, cancellationToken)
            ?? new CryptoHolding
            {
                Id = key,
                AccountId = command.AccountId,
                Symbol = command.Symbol.Trim(),
                CoinGeckoId = command.CoinGeckoId.Trim().ToLowerInvariant(),
                Quantity = command.Quantity,
            };
        holding.Symbol = command.Symbol.Trim();
        holding.Quantity = command.Quantity;
        holding.UpdatedAt = timeProvider.GetUtcNow();
        session.Store(holding);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(holding);
    }
}
