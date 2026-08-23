using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Resources;
using Marten;
using Microsoft.Extensions.Localization;

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
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(command.Symbol))
        {
            return Result.Fail<CryptoHolding>(localizer["Error.SymbolRequired"]);
        }
        if (string.IsNullOrWhiteSpace(command.CoinGeckoId))
        {
            return Result.Fail<CryptoHolding>(localizer["Error.CoinGeckoIdRequired"]);
        }
        if (command.Quantity <= 0)
        {
            return Result.Fail<CryptoHolding>(localizer["Error.QuantityPositive"]);
        }

        var account = await session.LoadAsync<Account>(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail<CryptoHolding>(localizer["Error.AccountNotFound"]);
        }
        if (account.Type != AccountType.Crypto)
        {
            return Result.Fail<CryptoHolding>(localizer["Error.CryptoAccountsOnly"]);
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
