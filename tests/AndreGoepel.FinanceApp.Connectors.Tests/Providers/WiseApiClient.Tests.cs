using System.Net;
using System.Text;
using AndreGoepel.FinanceApp.Connectors.Providers.Wise;
using AndreGoepel.FinanceApp.Domain.Providers;
using NSubstitute;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Providers;

public sealed class WiseApiClientTests
{
    [Fact]
    public async Task GetProfilesAsync_SuccessResponse_ParsesIdAndType()
    {
        // Arrange — real sandbox shape (trimmed).
        const string json = """
            [{"id":29305553,"type":"personal","details":{"firstName":"Eliana"}}]
            """;
        var client = ClientReturning(json, out var handler);

        // Act
        var result = await client.GetProfilesAsync(
            "token",
            ProviderEnvironment.Sandbox,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsSuccess);
        var profile = Assert.Single(result.Value!);
        Assert.Equal(29305553, profile.Id);
        Assert.Equal("personal", profile.Type);
        Assert.Equal(
            "https://api.wise-sandbox.com/v1/profiles",
            handler.LastRequestUri!.ToString()
        );
        Assert.Equal("Bearer token", handler.LastAuthorization);
    }

    [Fact]
    public async Task GetBalancesAsync_ParsesAmountsTypesJarNamesAndPrimary()
    {
        // Arrange — a primary standard balance (name null), a savings jar with a
        // name, and a second, non-primary standard balance sharing a currency with
        // the first (Wise's "grouped balances"), trimmed to the fields this client
        // reads.
        const string json = """
            [{"id":306149,"currency":"EUR","amount":{"value":999334.00,"currency":"EUR"},"type":"STANDARD","name":null,"primary":true},
             {"id":306999,"currency":"USD","amount":{"value":1000000.00,"currency":"USD"},"type":"SAVINGS","name":"Vacation","primary":false},
             {"id":306150,"currency":"EUR","amount":{"value":0,"currency":"EUR"},"type":"STANDARD","name":"Set aside","primary":false}]
            """;
        var client = ClientReturning(json, out var handler);

        // Act
        var result = await client.GetBalancesAsync(
            "token",
            ProviderEnvironment.Production,
            42,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value!,
            standard =>
            {
                Assert.Equal(306149, standard.Id);
                Assert.Equal("EUR", standard.Currency);
                Assert.Equal(999334.00m, standard.Amount);
                Assert.Equal("STANDARD", standard.Type);
                Assert.Null(standard.Name);
                Assert.True(standard.Primary);
            },
            jar =>
            {
                Assert.Equal("USD", jar.Currency);
                Assert.Equal(1000000.00m, jar.Amount);
                Assert.Equal("SAVINGS", jar.Type);
                Assert.Equal("Vacation", jar.Name);
                Assert.False(jar.Primary);
            },
            grouped =>
            {
                Assert.Equal(306150, grouped.Id);
                Assert.Equal("EUR", grouped.Currency);
                Assert.Equal("STANDARD", grouped.Type);
                Assert.Equal("Set aside", grouped.Name);
                Assert.False(grouped.Primary);
            }
        );
        Assert.Equal(
            "https://api.wise.com/v4/profiles/42/balances?types=STANDARD,SAVINGS",
            handler.LastRequestUri!.ToString()
        );
    }

    [Fact]
    public async Task GetBalancesAsync_MissingPrimaryField_DoesNotDefaultToTrue()
    {
        // Arrange — no "primary" field at all. Defaulting this to true would risk
        // syncing a balance's transactions when it isn't actually the one true
        // primary balance for its currency, duplicating the real primary's history.
        const string json = """
            [{"id":306149,"currency":"EUR","amount":{"value":100.00,"currency":"EUR"},"type":"STANDARD"}]
            """;
        var client = ClientReturning(json, out _);

        // Act
        var result = await client.GetBalancesAsync(
            "token",
            ProviderEnvironment.Production,
            42,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsSuccess);
        var balance = Assert.Single(result.Value!);
        Assert.False(balance.Primary);
    }

    [Fact]
    public async Task GetBalancesAsync_MissingTypeField_DoesNotDefaultToStandard()
    {
        // Arrange — a balance object without a "type" field at all, e.g. a schema
        // drift or a balance shape Wise didn't populate it for. Defaulting this to
        // "STANDARD" previously let a jar's activity sync through undetected.
        const string json = """
            [{"id":306149,"currency":"EUR","amount":{"value":100.00,"currency":"EUR"}}]
            """;
        var client = ClientReturning(json, out _);

        // Act
        var result = await client.GetBalancesAsync(
            "token",
            ProviderEnvironment.Production,
            42,
            TestContext.Current.CancellationToken
        );

        // Assert — Type is neither "STANDARD" nor "SAVINGS", so downstream code
        // (WiseConnector) fails loudly instead of guessing either way.
        Assert.True(result.IsSuccess);
        var balance = Assert.Single(result.Value!);
        Assert.NotEqual("STANDARD", balance.Type);
        Assert.NotEqual("SAVINGS", balance.Type);
    }

    [Fact]
    public async Task GetBalancesAsync_NonSuccessStatus_FailsWithStatus()
    {
        // Arrange
        var client = ClientReturning(
            "{\"error\":\"invalid_token\"}",
            out _,
            HttpStatusCode.Unauthorized
        );

        // Act
        var result = await client.GetBalancesAsync(
            "bad",
            ProviderEnvironment.Sandbox,
            1,
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("401", result.Error);
    }

    private static WiseApiClient ClientReturning(
        string body,
        out StubHandler handler,
        HttpStatusCode status = HttpStatusCode.OK
    )
    {
        handler = new StubHandler(body, status);
        var httpClient = new HttpClient(handler);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(httpClient);
        return new WiseApiClient(factory);
    }

    private sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        public string? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastRequestUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(
                new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
