using System.Net;
using System.Text;
using AndreGoepel.FinanceApp.Connectors.Crypto;
using NSubstitute;

namespace AndreGoepel.FinanceApp.Connectors.Tests.Crypto;

public sealed class CoinGeckoPriceProviderTests
{
    [Fact]
    public async Task GetEurPricesAsync_MultipleIds_BatchesOneRequestAndParsesDecimals()
    {
        // Arrange — real CoinGecko simple/price shape.
        const string json = """{"bitcoin":{"eur":95123.45},"ethereum":{"eur":3210.9876}}""";
        var provider = ProviderReturning(json, out var handler);

        // Act
        var result = await provider.GetEurPricesAsync(["bitcoin", "ethereum"]);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(95123.45m, result.Value!["bitcoin"]);
        Assert.Equal(3210.9876m, result.Value["ethereum"]);
        Assert.Equal(
            "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin%2Cethereum&vs_currencies=eur",
            handler.LastRequestUri!.ToString()
        );
    }

    [Fact]
    public async Task GetEurPricesAsync_UnknownId_IsAbsentFromResultWithoutFailing()
    {
        // Arrange — CoinGecko silently omits ids it does not know.
        var provider = ProviderReturning("""{"bitcoin":{"eur":95000.0}}""", out _);

        // Act
        var result = await provider.GetEurPricesAsync(["bitcoin", "no-such-coin"]);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.ContainsKey("bitcoin"));
        Assert.False(result.Value.ContainsKey("no-such-coin"));
    }

    [Fact]
    public async Task GetEurPricesAsync_EmptyIdList_ReturnsEmptyWithoutCallingTheApi()
    {
        // Arrange
        var provider = ProviderReturning("", out var handler);

        // Act
        var result = await provider.GetEurPricesAsync([]);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
        Assert.Null(handler.LastRequestUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetEurPricesAsync_NonSuccessStatus_Fails(HttpStatusCode status)
    {
        // Arrange
        var provider = ProviderReturning("throttled", out _, status);

        // Act
        var result = await provider.GetEurPricesAsync(["bitcoin"]);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains(((int)status).ToString(), result.Error);
    }

    [Fact]
    public async Task GetEurPricesAsync_NetworkError_Fails()
    {
        // Arrange
        var factory = Substitute.For<IHttpClientFactory>();
        factory
            .CreateClient(Arg.Any<string>())
            .Returns(
                new HttpClient(new ThrowingHandler())
                {
                    BaseAddress = new Uri("https://api.coingecko.com/api/v3/"),
                }
            );
        var provider = new CoinGeckoPriceProvider(factory);

        // Act
        var result = await provider.GetEurPricesAsync(["bitcoin"]);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("unreachable", result.Error);
    }

    private static CoinGeckoPriceProvider ProviderReturning(
        string body,
        out StubHandler handler,
        HttpStatusCode status = HttpStatusCode.OK
    )
    {
        handler = new StubHandler(body, status);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.coingecko.com/api/v3/"),
        };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(httpClient);
        return new CoinGeckoPriceProvider(factory);
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

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => throw new HttpRequestException("connection refused");
    }
}
