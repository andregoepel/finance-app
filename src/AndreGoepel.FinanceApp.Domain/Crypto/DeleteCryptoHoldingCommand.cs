using Marten;

namespace AndreGoepel.FinanceApp.Domain.Crypto;

/// <summary>Removes the holding of one asset from a crypto account.</summary>
public sealed record DeleteCryptoHoldingCommand(Guid AccountId, string CoinGeckoId);

public static class DeleteCryptoHoldingCommandHandler
{
    public static async Task<Result> Handle(
        DeleteCryptoHoldingCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        session.Delete<CryptoHolding>(CryptoHolding.KeyFor(command.AccountId, command.CoinGeckoId));
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
