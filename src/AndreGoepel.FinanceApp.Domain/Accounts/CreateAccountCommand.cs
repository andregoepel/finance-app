using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Providers;
using AndreGoepel.FinanceApp.Domain.Resources;
using Marten;
using Microsoft.Extensions.Localization;

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
    string? ExternalId = null,
    string? IdentificationHash = null
);

public static class CreateAccountCommandHandler
{
    public static async Task<Result<Account>> Handle(
        CreateAccountCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result.Fail<Account>(localizer["Error.AccountNameRequired"]);
        }

        var owners = AccountOwners.Validate(command.IsShared, command.OwnerUserIds, localizer);
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
            IdentificationHash = string.IsNullOrWhiteSpace(command.IdentificationHash)
                ? null
                : command.IdentificationHash.Trim(),
        };
        session.Store(account);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(account);
    }
}

/// <summary>Shared owner-selection validation for create and update.</summary>
internal static class AccountOwners
{
    public static Result<List<Guid>> Validate(
        bool isShared,
        IReadOnlyList<Guid> ownerUserIds,
        IStringLocalizer<DomainStrings> localizer
    )
    {
        var owners = (ownerUserIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (owners.Count == 0)
        {
            return Result.Fail<List<Guid>>(localizer["Error.SelectAtLeastOneOwner"]);
        }
        if (!isShared && owners.Count > 1)
        {
            return Result.Fail<List<Guid>>(localizer["Error.NonSharedSingleOwner"]);
        }
        return Result.Ok(owners);
    }
}
