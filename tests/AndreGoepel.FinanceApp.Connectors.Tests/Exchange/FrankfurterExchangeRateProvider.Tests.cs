using System.Net;
using System.Text;
using AndreGoepel.FinanceApp.Connectors.Exchange;
using NSubstitute;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Exchange;

public class FrankfurterExchangeRateProviderTests
{
    [Fact]
    public async Task GetEurRateAsync_Eur_ReturnsOneWithoutCallingTheApi()
    {
        // Arrange
        var provider = ProviderReturning("", out var handler);

        // Act
        var result = await provider.GetEurRateAsync("EUR", new DateOnly(2026, 7, 2));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1m, result.Value!.EurPerUnit);
        Assert.Null(handler.LastRequestUri); // no HTTP for EUR
    }

    [Fact]
    public async Task GetEurRateAsync_ForeignCurrency_ParsesRateAndActualDate()
    {
        // Arrange — real Frankfurter shape; requested a Saturday, API returns Friday.
        const string json =
            """{"amount":1.0,"base":"USD","date":"2026-07-03","rates":{"EUR":0.87352}}""";
        var provider = ProviderReturning(json, out var handler);

        // Act
        var result = await provider.GetEurRateAsync("USD", new DateOnly(2026, 7, 4));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0.87352m, result.Value!.EurPerUnit);
        Assert.Equal(new DateOnly(2026, 7, 3), result.Value.RateDate);
        Assert.Equal(
            "https://api.frankfurter.dev/v1/2026-07-04?from=USD&to=EUR",
            handler.LastRequestUri!.ToString()
        );
    }

    [Fact]
    public async Task GetEurRateAsync_UnsupportedCurrency_Fails()
    {
        // Arrange — a response with no EUR rate (e.g. a crypto asset).
        var provider = ProviderReturning(
            """{"amount":1.0,"base":"BTC","date":"2026-07-02","rates":{}}""",
            out _
        );

        // Act
        var result = await provider.GetEurRateAsync("BTC", new DateOnly(2026, 7, 2));

        // Assert
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GetEurRateAsync_NonSuccessStatus_Fails()
    {
        // Arrange
        var provider = ProviderReturning("not found", out _, HttpStatusCode.NotFound);

        // Act
        var result = await provider.GetEurRateAsync("USD", new DateOnly(2026, 7, 2));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("404", result.Error);
    }

    private static FrankfurterExchangeRateProvider ProviderReturning(
        string body,
        out StubHandler handler,
        HttpStatusCode status = HttpStatusCode.OK
    )
    {
        handler = new StubHandler(body, status);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.frankfurter.dev/v1/"),
        };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(httpClient);
        return new FrankfurterExchangeRateProvider(factory);
    }

    private sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(
                new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
