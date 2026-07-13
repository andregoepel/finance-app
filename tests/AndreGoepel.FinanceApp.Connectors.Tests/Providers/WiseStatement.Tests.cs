using System.Net;
using System.Security.Cryptography;
using System.Text;
using AndreGoepel.FinanceApp.Connectors.Providers.Wise;
using AndreGoepel.FinanceApp.Domain.Providers;
using NSubstitute;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Providers;

/// <summary>Statement parsing and the SCA 403 → sign → retry handshake.</summary>
public class WiseStatementTests
{
    // Real COMPACT statement shape (trimmed): one card debit, one incoming credit.
    private const string StatementJson = """
        {
          "accountHolder": { "type": "PERSONAL" },
          "transactions": [
            {
              "type": "DEBIT",
              "date": "2026-07-03T09:35:12.226Z",
              "amount": { "value": -25.50, "currency": "EUR" },
              "details": {
                "type": "CARD",
                "description": "Card transaction of 25.50 EUR issued by Rewe",
                "merchant": { "name": "REWE Markt" }
              },
              "referenceNumber": "CARD-1234"
            },
            {
              "type": "CREDIT",
              "date": "2026-07-01T00:00:00.000Z",
              "amount": { "value": 1500.00, "currency": "EUR" },
              "details": {
                "type": "DEPOSIT",
                "description": "Received money from Erika Beispiel",
                "senderName": "Erika Beispiel"
              },
              "referenceNumber": "TRANSFER-5678"
            }
          ]
        }
        """;

    [Fact]
    public void ParseStatement_MapsAmountsCounterpartyAndReference()
    {
        // Act
        var rows = WiseApiClient.ParseStatement(StatementJson);

        // Assert
        Assert.Collection(
            rows,
            debit =>
            {
                Assert.Equal(new DateOnly(2026, 7, 3), debit.Date);
                Assert.Equal(-25.50m, debit.Amount);
                Assert.Equal("EUR", debit.Currency);
                Assert.Equal("REWE Markt", debit.Counterparty);
                Assert.Equal("CARD-1234", debit.ReferenceNumber);
                Assert.Contains("Rewe", debit.Description);
            },
            credit =>
            {
                Assert.Equal(1500.00m, credit.Amount);
                Assert.Equal("Erika Beispiel", credit.Counterparty);
                Assert.Equal("TRANSFER-5678", credit.ReferenceNumber);
            }
        );
    }

    [Fact]
    public void ParseStatement_PositiveDebit_IsNegated()
    {
        // Arrange — defensive belt: a DEBIT must never import as income.
        const string json = """
            {"transactions":[{"type":"DEBIT","date":"2026-07-03T00:00:00.000Z",
            "amount":{"value":10.00,"currency":"EUR"},"details":{"description":"x"}}]}
            """;

        // Act
        var rows = WiseApiClient.ParseStatement(json);

        // Assert
        Assert.Equal(-10.00m, Assert.Single(rows).Amount);
    }

    [Fact]
    public async Task GetBalanceStatementAsync_ScaChallenge_SignsAndRetries()
    {
        // Arrange — first answer 403 + one-time token, then the statement.
        using var rsa = RSA.Create(2048);
        var handler = new ScaHandler("one-time-approval-token", StatementJson);
        var client = ClientWith(handler);

        // Act
        var result = await client.GetBalanceStatementAsync(
            "token",
            rsa.ExportPkcs8PrivateKeyPem(),
            ProviderEnvironment.Sandbox,
            profileId: 42,
            balanceId: 306149,
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 7, 1)
        );

        // Assert — retried with the token echoed and a verifiable signature.
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal("one-time-approval-token", handler.RetryApprovalHeader);
        var verified = rsa.VerifyData(
            Encoding.ASCII.GetBytes("one-time-approval-token"),
            Convert.FromBase64String(handler.RetrySignatureHeader!),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        Assert.True(verified);
        Assert.Contains("/v1/profiles/42/balance-statements/306149/", handler.LastUri!.ToString());
        Assert.Contains("intervalStart=2026-06-01", handler.LastUri.ToString());
    }

    [Fact]
    public async Task GetBalanceStatementAsync_ScaChallengeWithoutKey_FailsActionably()
    {
        // Arrange
        var handler = new ScaHandler("one-time-approval-token", StatementJson);
        var client = ClientWith(handler);

        // Act
        var result = await client.GetBalanceStatementAsync(
            "token",
            scaPrivateKeyPem: null,
            ProviderEnvironment.Sandbox,
            42,
            306149,
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 7, 1)
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("SCA", result.Error);
        Assert.Contains("Settings → Connections", result.Error);
    }

    [Fact]
    public async Task GetBalanceStatementAsync_SignatureRejected_ExplainsKeyMismatch()
    {
        // Arrange — the retry is rejected again (key does not match).
        using var rsa = RSA.Create(2048);
        var handler = new ScaHandler("one-time-approval-token", StatementJson)
        {
            RejectRetry = true,
        };
        var client = ClientWith(handler);

        // Act
        var result = await client.GetBalanceStatementAsync(
            "token",
            rsa.ExportPkcs8PrivateKeyPem(),
            ProviderEnvironment.Sandbox,
            42,
            306149,
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 7, 1)
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("does not match", result.Error);
    }

    private static WiseApiClient ClientWith(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler));
        return new WiseApiClient(factory);
    }

    /// <summary>Answers the first request 403 + <c>x-2fa-approval</c>, the signed retry 200.</summary>
    private sealed class ScaHandler(string oneTimeToken, string statementJson) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public string? RetryApprovalHeader { get; private set; }
        public string? RetrySignatureHeader { get; private set; }
        public bool RejectRetry { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastUri = request.RequestUri;

            var isRetry = request.Headers.TryGetValues("x-2fa-approval", out var approval);
            if (!isRetry)
            {
                var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("", Encoding.UTF8, "application/json"),
                };
                challenge.Headers.Add("x-2fa-approval", oneTimeToken);
                return Task.FromResult(challenge);
            }

            RetryApprovalHeader = approval!.First();
            RetrySignatureHeader = request.Headers.TryGetValues("X-Signature", out var signature)
                ? signature.First()
                : null;

            return Task.FromResult(
                RejectRetry
                    ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                    {
                        Content = new StringContent("", Encoding.UTF8, "application/json"),
                    }
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            statementJson,
                            Encoding.UTF8,
                            "application/json"
                        ),
                    }
            );
        }
    }
}
