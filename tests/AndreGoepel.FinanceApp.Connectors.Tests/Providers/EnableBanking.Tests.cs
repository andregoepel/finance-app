using System.Text.Json.Nodes;
using AndreGoepel.FinanceApp.Connectors.Providers.EnableBanking;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Providers;

public class EnableBankingTransactionParsingTests
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

public class EnableBankingConnectorNormalizeTests
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
