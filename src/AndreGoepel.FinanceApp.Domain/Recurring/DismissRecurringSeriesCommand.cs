using Marten;

namespace AndreGoepel.FinanceApp.Domain.Recurring;

/// <summary>
/// Marks a detected recurring series as a false positive — see
/// <see cref="DismissedRecurringSeries"/>. Idempotent: dismissing an
/// already-dismissed series just overwrites the same document.
/// </summary>
public sealed record DismissRecurringSeriesCommand(string Counterparty);

public static class DismissRecurringSeriesCommandHandler
{
    public static async Task Handle(
        DismissRecurringSeriesCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        session.Store(new DismissedRecurringSeries { Id = command.Counterparty });
        await session.SaveChangesAsync(cancellationToken);
    }
}
