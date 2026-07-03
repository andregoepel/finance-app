using Marten;

namespace FinanceApp.Domain.Accounts;

/// <summary>
/// Edits the mutable account fields. Provider is fixed at creation — imported
/// transactions and parsers depend on it.
/// </summary>
public sealed record UpdateAccountCommand(
    Guid AccountId,
    string Name,
    AccountType Type,
    string Currency,
    AccountOwner Owner,
    SyncMethod SyncMethod,
    string? Iban
);

public static class UpdateAccountCommandHandler
{
    public static async Task<Result<Account>> Handle(
        UpdateAccountCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Fail<Account>("Account name is required.");
        }

        var account = await session.LoadAsync<Account>(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail<Account>("Account not found.");
        }

        account.Name = command.Name.Trim();
        account.Type = command.Type;
        account.Currency = command.Currency.Trim().ToUpperInvariant();
        account.Owner = command.Owner;
        account.SyncMethod = command.SyncMethod;
        account.Iban = string.IsNullOrWhiteSpace(command.Iban) ? null : command.Iban.Trim();

        session.Store(account);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(account);
    }
}
