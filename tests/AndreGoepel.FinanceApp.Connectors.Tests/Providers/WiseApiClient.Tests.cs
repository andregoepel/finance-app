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
        var result = await client.GetProfilesAsync("token", ProviderEnvironment.Sandbox);

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
    public async Task GetBalancesAsync_SuccessResponse_ParsesDecimalAmountsAndCurrency()
    {
        // Arrange
        const string json = """
            [{"id":306149,"currency":"EUR","amount":{"value":999334.00,"currency":"EUR"}},
             {"id":306112,"currency":"USD","amount":{"value":1000000.00,"currency":"USD"}}]
            """;
        var client = ClientReturning(json, out var handler);

        // Act
        var result = await client.GetBalancesAsync("token", ProviderEnvironment.Production, 42);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value!,
            eur =>
            {
                Assert.Equal(306149, eur.Id);
                Assert.Equal("EUR", eur.Currency);
                Assert.Equal(999334.00m, eur.Amount);
            },
            usd =>
            {
                Assert.Equal("USD", usd.Currency);
                Assert.Equal(1000000.00m, usd.Amount);
            }
        );
        Assert.Equal(
            "https://api.wise.com/v4/profiles/42/balances?types=STANDARD",
            handler.LastRequestUri!.ToString()
        );
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
        var result = await client.GetBalancesAsync("bad", ProviderEnvironment.Sandbox, 1);

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
