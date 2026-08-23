using System.Text.Json.Nodes;
using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Connectors.Providers.EnableBanking;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.FinanceApp.Domain.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Providers;

public sealed class EnableBankingTransactionParsingTests
{
    // Real shape captured from the Enable Banking sandbox.
    private const string CreditJson = """
        {"entry_reference":"sn3mm","transaction_amount":{"currency":"EUR","amount":"3.38"},
         "creditor":{"name":"Ella Nieminen"},"debtor":null,"credit_debit_indicator":"CRDT",
         "status":"BOOK","booking_date":"2026-07-02","value_date":"2026-06-30",
         "remittance_information":["Ella Nieminen-CRDT-3.38-sn3mm"],"transaction_id":null}
        """;

    [Fact]
    public void TryParseTransaction_ReadsAllFields()
    {
        // Act
        var ok = EnableBankingClient.TryParseTransaction(JsonNode.Parse(CreditJson), out var t);

        // Assert
        Assert.True(ok);
        Assert.Equal("sn3mm", t.EntryReference);
        Assert.Equal(new DateOnly(2026, 7, 2), t.BookingDate);
        Assert.Equal(new DateOnly(2026, 6, 30), t.ValueDate);
        Assert.Equal(3.38m, t.Amount);
        Assert.Equal("EUR", t.Currency);
        Assert.Equal("CRDT", t.CreditDebitIndicator);
        Assert.Equal("Ella Nieminen", t.CreditorName);
        Assert.Null(t.DebtorName);
        Assert.Equal("BOOK", t.Status);
        Assert.Equal(["Ella Nieminen-CRDT-3.38-sn3mm"], t.RemittanceInformation);
    }

    [Fact]
    public void TryParseTransaction_MissingAmount_ReturnsFalse()
    {
        // Arrange — a row without a parseable amount must not silently become 0.
        var node = JsonNode.Parse("""{"booking_date":"2026-07-02","transaction_amount":{}}""");

        // Act
        var ok = EnableBankingClient.TryParseTransaction(node, out _);

        // Assert
        Assert.False(ok);
    }
}

public sealed class EnableBankingConnectorNormalizeTests
{
    private static readonly DateOnly Date = new(2026, 7, 2);

    [Fact]
    public void Normalize_Debit_NegatesAmountAndUsesCreditorAsCounterparty()
    {
        // Arrange — money out: we paid the creditor.
        var debit = new EnableBankingTransaction(
            "ref1",
            Date,
            Date,
            12.34m,
            "EUR",
            "DBIT",
            CreditorName: "Netflix",
            DebtorName: null,
            RemittanceInformation: ["Subscription July"],
            Status: "BOOK",
            RawJson: "{}"
        );

        // Act
        var row = EnableBankingConnector.Normalize(debit);

        // Assert
        Assert.Equal(-12.34m, row.Amount);
        Assert.Equal("Netflix", row.Counterparty);
        Assert.Equal("Subscription July", row.Description);
        Assert.Equal("ref1", row.ExternalId);
    }

    [Fact]
    public void Normalize_Credit_KeepsPositiveAndFallsBackToCreditorName()
    {
        // Arrange — money in; the mock fills creditor even for credits.
        var credit = new EnableBankingTransaction(
            "ref2",
            Date,
            null,
            3.38m,
            "EUR",
            "CRDT",
            CreditorName: "Ella Nieminen",
            DebtorName: null,
            RemittanceInformation: [],
            Status: "BOOK",
            RawJson: "{}"
        );

        // Act
        var row = EnableBankingConnector.Normalize(credit);

        // Assert
        Assert.Equal(3.38m, row.Amount);
        Assert.Equal("Ella Nieminen", row.Counterparty);
        Assert.Equal("Ella Nieminen", row.Description); // falls back to counterparty when no remittance
    }
}

public sealed class EnableBankingConnectorFetchTests
{
    private readonly IEnableBankingClient client = Substitute.For<IEnableBankingClient>();
    private readonly EnableBankingConnector connector;

    public EnableBankingConnectorFetchTests() =>
        connector = new EnableBankingConnector(client, NullLogger<EnableBankingConnector>.Instance);

    private static EnableBankingTransaction Transaction(string status, string entryReference) =>
        new(
            entryReference,
            new DateOnly(2026, 7, 2),
            null,
            1m,
            "EUR",
            "DBIT",
            CreditorName: "Someone",
            DebtorName: null,
            RemittanceInformation: [],
            Status: status,
            RawJson: "{}"
        );

    private static ProviderSyncRequest Request() =>
        new(
            AccountId: Guid.CreateVersion7(),
            Provider: ProviderKind.Revolut,
            ConnectionId: Guid.CreateVersion7(),
            ExternalId: null,
            IdentificationHash: null,
            ProviderAccountReference: "session-account-1",
            Since: new DateOnly(2026, 1, 1)
        );

    [Theory]
    [InlineData("BOOK")]
    [InlineData("book")]
    [InlineData("Book")]
    public async Task FetchAsync_BookedStatusInAnyCasing_IsImported(string status)
    {
        // Arrange
        client
            .GetTransactionsAsync(
                Arg.Any<string>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Ok(new EnableBankingFetch([Transaction(status, "ref1")], [])));

        // Act
        var result = await connector.FetchAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Rows);
    }

    [Fact]
    public async Task FetchAsync_PendingStatus_IsExcludedButSyncStillSucceeds()
    {
        // Arrange
        client
            .GetTransactionsAsync(
                Arg.Any<string>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Ok(new EnableBankingFetch([Transaction("PDNG", "ref1")], [])));

        // Act
        var result = await connector.FetchAsync(Request(), TestContext.Current.CancellationToken);

        // Assert — a real "not booked yet" case must not look like a failure.
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Rows);
    }

    [Fact]
    public async Task FetchAsync_MixOfBookedAndOtherStatuses_OnlyBookedIsImported()
    {
        // Arrange
        client
            .GetTransactionsAsync(
                Arg.Any<string>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Ok(
                    new EnableBankingFetch(
                        [
                            Transaction("BOOK", "ref1"),
                            Transaction("PDNG", "ref2"),
                            Transaction("BOOK", "ref3"),
                        ],
                        []
                    )
                )
            );

        // Act
        var result = await connector.FetchAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Value!.Rows.Count);
        Assert.Equal(["ref1", "ref3"], result.Value.Rows.Select(row => row.ExternalId));
    }

    [Fact]
    public async Task FetchAsync_UnparseableEntries_AreReportedNotDropped()
    {
        // Arrange — the client could not read two entries. IProviderConnector's own contract is
        // that a row is never dropped silently, so these must reach the caller as problem rows
        // rather than dying in a log line.
        client
            .GetTransactionsAsync(
                Arg.Any<string>(),
                Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Ok(
                    new EnableBankingFetch(
                        [Transaction("BOOK", "ref1")],
                        [
                            new ImportRowError(3, "booking_date was missing.", "{}"),
                            new ImportRowError(7, "amount was unreadable.", "{}"),
                        ]
                    )
                )
            );

        // Act
        var result = await connector.FetchAsync(Request(), TestContext.Current.CancellationToken);

        // Assert — the good row still imports, and the bad ones travel with it.
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Rows);
        Assert.Equal(2, result.Value.Errors.Count);
        Assert.Equal([3, 7], result.Value.Errors.Select(e => e.RowNumber));
    }
}
