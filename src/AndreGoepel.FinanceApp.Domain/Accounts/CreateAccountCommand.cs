using AndreGoepel.FinanceApp.Domain.Providers;
using Marten;

namespace AndreGoepel.FinanceApp.Domain.Accounts;

public sealed record CreateAccountCommand(
    string Name,
    ProviderKind Provider,
    AccountType Type,
    string Currency,
    bool IsShared,
    IReadOnlyList<Guid> OwnerUserIds,
    SyncMethod SyncMethod,
    string? Iban,
    Guid? ConnectionId = null,
    string? ExternalId = null
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

        var owners = AccountOwners.Validate(command.IsShared, command.OwnerUserIds);
        if (owners.IsFailure)
        {
            return Result.Fail<Account>(owners.Error);
        }

        var account = new Account
        {
            Name = command.Name.Trim(),
            Provider = command.Provider,
            Type = command.Type,
            Currency = command.Currency.Trim().ToUpperInvariant(),
            IsShared = command.IsShared,
            OwnerUserIds = owners.Value!,
            SyncMethod = command.SyncMethod,
            Iban = string.IsNullOrWhiteSpace(command.Iban) ? null : command.Iban.Trim(),
            ConnectionId = command.ConnectionId,
            ExternalId = string.IsNullOrWhiteSpace(command.ExternalId)
                ? null
                : command.ExternalId.Trim(),
        };
        session.Store(account);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(account);
    }
}

/// <summary>Shared owner-selection validation for create and update.</summary>
internal static class AccountOwners
{
    public static Result<List<Guid>> Validate(bool isShared, IReadOnlyList<Guid> ownerUserIds)
    {
        var owners = (ownerUserIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (owners.Count == 0)
        {
            return Result.Fail<List<Guid>>("Select at least one owner for the account.");
        }
        if (!isShared && owners.Count > 1)
        {
            return Result.Fail<List<Guid>>(
                "A non-shared account must have exactly one owner; enable “shared” to add more."
            );
        }
        return Result.Ok(owners);
    }
}
