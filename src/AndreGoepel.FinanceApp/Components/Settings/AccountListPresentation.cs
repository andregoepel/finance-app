using AndreGoepel.FinanceApp.Domain.Accounts;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Components.Settings;

internal sealed record WiseAccountGroup(
    Guid? ConnectionId,
    string Label,
    IReadOnlyList<Account> Accounts
);

internal sealed record AccountListPresentation(
    IReadOnlyList<Account> NonWiseAccounts,
    IReadOnlyList<WiseAccountGroup> WiseGroups
)
{
    public static AccountListPresentation Build(
        IEnumerable<Account> accounts,
        IEnumerable<ProviderConnection> connections,
        string unlinkedLabel
    )
    {
        var orderedAccounts = accounts.OrderBy(account => account.Name).ToList();
        var wiseConnections = connections
            .Where(connection => connection.Provider == ProviderKind.Wise)
            .ToDictionary(connection => connection.Id);

        var nonWise = orderedAccounts
            .Where(account => account.Provider != ProviderKind.Wise)
            .ToList();
        var wiseGroups = orderedAccounts
            .Where(account => account.Provider == ProviderKind.Wise)
            .GroupBy(account =>
                account.ConnectionId is Guid connectionId
                && wiseConnections.ContainsKey(connectionId)
                    ? connectionId
                    : (Guid?)null
            )
            .Select(group => new WiseAccountGroup(
                group.Key,
                group.Key is Guid connectionId
                    ? wiseConnections[connectionId].Label
                    : unlinkedLabel,
                group.ToList()
            ))
            .OrderBy(group => group.ConnectionId is null)
            .ThenBy(group => group.Label)
            .ToList();

        return new AccountListPresentation(nonWise, wiseGroups);
    }
}
