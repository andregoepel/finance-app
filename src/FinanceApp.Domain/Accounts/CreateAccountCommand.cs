using FinanceApp.Domain.Providers;
using Marten;

namespace FinanceApp.Domain.Accounts;

public sealed record CreateAccountCommand(
    string Name,
    ProviderKind Provider,
    AccountType Type,
    string Currency,
    AccountOwner Owner,
    SyncMethod SyncMethod,
    string? Iban
);

public static class CreateAccountCommandHandler
{
    public static async Task<Result<Account>> Handle(
        CreateAccountCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Fail<Account>("Account name is required.");
        }

        var account = new Account
        {
            Name = command.Name.Trim(),
            Provider = command.Provider,
            Type = command.Type,
            Currency = command.Currency.Trim().ToUpperInvariant(),
            Owner = command.Owner,
            SyncMethod = command.SyncMethod,
            Iban = string.IsNullOrWhiteSpace(command.Iban) ? null : command.Iban.Trim(),
        };
        session.Store(account);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(account);
    }
}
