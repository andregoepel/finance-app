using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Resources;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Accounts;

/// <summary>
/// Records an account's current balance anchor for net-worth reconstruction. The
/// caller supplies both the native-currency amount and its EUR value (converted
/// in the application layer), plus the "as of" timestamp.
/// </summary>
public sealed record SetAccountBalanceCommand(
    Guid AccountId,
    decimal Balance,
    decimal? BalanceEur,
    DateTimeOffset AsOf
);

public static class SetAccountBalanceCommandHandler
{
    public static async Task<Result> Handle(
        SetAccountBalanceCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        var account = await session.LoadAsync<Account>(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail(localizer["Error.AccountNotFound"]);
        }

        account.CurrentBalance = command.Balance;
        account.CurrentBalanceEur = command.BalanceEur;
        account.BalanceUpdatedAt = command.AsOf;
        session.Store(account);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
