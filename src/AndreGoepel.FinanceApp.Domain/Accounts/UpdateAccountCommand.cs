using Marten;

namespace AndreGoepel.FinanceApp.Domain.Accounts;

/// <summary>
/// Edits the mutable account fields. Provider is fixed at creation — imported
/// transactions and parsers depend on it. Lifecycle (activate/deactivate/delete)
/// is handled by dedicated commands.
/// </summary>
public sealed record UpdateAccountCommand(
    Guid AccountId,
    string Name,
    AccountType Type,
    string Currency,
    bool IsShared,
    IReadOnlyList<Guid> OwnerUserIds,
    SyncMethod SyncMethod,
    string? Iban,
    Guid? ConnectionId = null
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

        var owners = AccountOwners.Validate(command.IsShared, command.OwnerUserIds);
        if (owners.IsFailure)
        {
            return Result.Fail<Account>(owners.Error);
        }

        var account = await session.LoadAsync<Account>(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail<Account>("Account not found.");
        }

        account.Name = command.Name.Trim();
        account.Type = command.Type;
        account.Currency = command.Currency.Trim().ToUpperInvariant();
        account.IsShared = command.IsShared;
        account.OwnerUserIds = owners.Value!;
        account.SyncMethod = command.SyncMethod;
        account.Iban = string.IsNullOrWhiteSpace(command.Iban) ? null : command.Iban.Trim();
        account.ConnectionId = command.ConnectionId;

        session.Store(account);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(account);
    }
}
