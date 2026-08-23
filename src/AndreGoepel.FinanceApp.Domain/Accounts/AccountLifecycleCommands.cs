using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Resources;
using AndreGoepel.FinanceApp.Domain.Transactions;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Accounts;

/// <summary>
/// Deactivate (soft delete): the account disappears from selection lists but its
/// transactions and history are preserved. Reversible via <see cref="ReactivateAccountCommand"/>.
/// </summary>
public sealed record DeactivateAccountCommand(Guid AccountId);

/// <summary>Bring a deactivated account back into active use.</summary>
public sealed record ReactivateAccountCommand(Guid AccountId);

/// <summary>
/// Permanently remove an account. Only allowed when it has no transactions —
/// otherwise deactivation is the correct action and this fails loudly so history
/// is never silently destroyed.
/// </summary>
public sealed record DeleteAccountCommand(Guid AccountId);

public static class DeactivateAccountCommandHandler
{
    public static async Task<Result<Account>> Handle(
        DeactivateAccountCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        var account = await session.LoadAsync<Account>(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail<Account>(localizer["Error.AccountNotFound"]);
        }

        account.Status = AccountStatus.Deactivated;
        account.DeactivatedAt = DateTimeOffset.UtcNow;
        session.Store(account);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(account);
    }
}

public static class ReactivateAccountCommandHandler
{
    public static async Task<Result<Account>> Handle(
        ReactivateAccountCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        var account = await session.LoadAsync<Account>(command.AccountId, cancellationToken);
        if (account is null)
        {
            return Result.Fail<Account>(localizer["Error.AccountNotFound"]);
        }

        account.Status = AccountStatus.Active;
        account.DeactivatedAt = null;
        session.Store(account);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok(account);
    }
}

public static class DeleteAccountCommandHandler
{
    public static async Task<Result> Handle(
        DeleteAccountCommand command,
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

        var transactionCount = await session
            .Query<TransactionView>()
            .CountAsync(t => t.AccountId == command.AccountId, cancellationToken);
        if (transactionCount > 0)
        {
            return Result.Fail(
                $"This account has {transactionCount} transaction(s); deactivate it instead of "
                    + "deleting, so its history is preserved."
            );
        }

        session.Delete(account);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
