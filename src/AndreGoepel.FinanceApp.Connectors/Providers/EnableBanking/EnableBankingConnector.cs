using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Providers.EnableBanking;

/// <summary>
/// Syncs DKB and Revolut through the Enable Banking PSD2 aggregator. One connector
/// serves both because they share the same API; the concrete
/// <see cref="ProviderKind"/> is carried on the request. The application layer
/// resolves the session account (via the stable identification hash) into the
/// session-specific account uid and passes it as
/// <see cref="ProviderSyncRequest.ProviderAccountReference"/>.
/// </summary>
public sealed class EnableBankingConnector(IEnableBankingClient client) : IProviderConnector
{
    internal const string SyncSource = "enablebanking-api-v1";

    public bool Supports(ProviderKind provider) =>
        provider is ProviderKind.Dkb or ProviderKind.Revolut;

    public async Task<Result<ProviderSyncResult>> FetchAsync(
        ProviderSyncRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.ProviderAccountReference))
        {
            return Result.Fail<ProviderSyncResult>(
                $"The account is not linked to an authorized {request.Provider} consent "
                    + "(Settings → Connections → Connect, then link the account)."
            );
        }

        var transactions = await client.GetTransactionsAsync(
            request.ProviderAccountReference,
            request.Since,
            cancellationToken
        );
        if (transactions.IsFailure)
        {
            return Result.Fail<ProviderSyncResult>(transactions.Error!);
        }

        var rows = transactions
            .Value!.Where(t => t.Status == "BOOK") // only booked; pending entries change
            .Select(Normalize)
            .ToList();

        return Result.Ok(new ProviderSyncResult(SyncSource, rows, []));
    }

    /// <summary>Maps one Enable Banking transaction to the shared import shape.</summary>
    internal static NormalizedTransaction Normalize(EnableBankingTransaction t)
    {
        var isDebit = string.Equals(
            t.CreditDebitIndicator,
            "DBIT",
            StringComparison.OrdinalIgnoreCase
        );
        var amount = isDebit ? -t.Amount : t.Amount;

        // Counterparty is the other party: on a debit we paid the creditor; on a
        // credit we were paid by the debtor. Fall back to whichever name is present.
        var counterparty =
            (isDebit ? t.CreditorName : t.DebtorName) ?? t.CreditorName ?? t.DebtorName;

        var description =
            t.RemittanceInformation.Count > 0
                ? string.Join(" ", t.RemittanceInformation)
                : counterparty ?? "";

        return new NormalizedTransaction(
            SourceRow: 0,
            BookingDate: t.BookingDate,
            ValueDate: t.ValueDate,
            Amount: amount,
            Currency: t.Currency,
            Counterparty: counterparty,
            Description: description,
            ExternalId: t.EntryReference,
            RawData: t.RawJson
        );
    }
}
