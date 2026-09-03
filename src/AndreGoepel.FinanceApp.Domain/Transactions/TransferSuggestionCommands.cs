using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Resources;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Transactions;

/// <summary>Accepts a transfer suggestion: links both legs and clears the review entry.</summary>
public sealed record AcceptTransferSuggestionCommand(string SuggestionId);

public static class AcceptTransferSuggestionCommandHandler
{
    public static async Task<Result> Handle(
        AcceptTransferSuggestionCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        var suggestion = await session.LoadAsync<TransferSuggestion>(
            command.SuggestionId,
            cancellationToken
        );
        if (suggestion is null)
        {
            return Result.Fail(localizer["Error.TransferSuggestionNotFound"]);
        }

        var result = await TransferLinking.LinkAsync(
            session,
            suggestion.OutgoingTransactionId,
            suggestion.IncomingTransactionId,
            localizer,
            cancellationToken
        );
        if (result.IsFailure)
        {
            return result;
        }

        session.Delete(suggestion);

        // Any other pending suggestion touching either leg is now impossible —
        // both transactions just got spoken for.
        var competing = await session
            .Query<TransferSuggestion>()
            .Where(s =>
                s.Id != suggestion.Id
                && !s.Dismissed
                && (
                    s.OutgoingTransactionId == suggestion.OutgoingTransactionId
                    || s.IncomingTransactionId == suggestion.OutgoingTransactionId
                    || s.OutgoingTransactionId == suggestion.IncomingTransactionId
                    || s.IncomingTransactionId == suggestion.IncomingTransactionId
                )
            )
            .ToListAsync(cancellationToken);
        foreach (var other in competing)
        {
            other.Dismissed = true;
            session.Store(other);
        }

        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}

/// <summary>Dismisses a transfer suggestion as "not a transfer" — kept as a tombstone.</summary>
public sealed record DismissTransferSuggestionCommand(string SuggestionId);

public static class DismissTransferSuggestionCommandHandler
{
    public static async Task<Result> Handle(
        DismissTransferSuggestionCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        var suggestion = await session.LoadAsync<TransferSuggestion>(
            command.SuggestionId,
            cancellationToken
        );
        if (suggestion is null)
        {
            return Result.Fail(localizer["Error.TransferSuggestionNotFound"]);
        }

        suggestion.Dismissed = true;
        session.Store(suggestion);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
