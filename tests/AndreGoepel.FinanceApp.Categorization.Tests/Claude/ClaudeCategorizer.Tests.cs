using System.Net;
using AndreGoepel.FinanceApp.Categorization.Claude;
using AndreGoepel.FinanceApp.Domain.Credentials;
using NSubstitute;

namespace AndreGoepel.FinanceApp.Categorization.Tests.Claude;

public sealed class ClaudeCategorizerTests
{
    private static readonly Guid CategoryA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TransactionA = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static string ToolUseResponse(string categorizationsJson) =>
        $$"""
            {
              "content": [
                { "type": "text", "text": "thinking" },
                {
                  "type": "tool_use",
                  "name": "categorize_transactions",
                  "input": { "categorizations": {{categorizationsJson}} }
                }
              ]
            }
            """;

    #region ParseResponse

    [Fact]
    public void ParseResponse_ValidToolUse_ReturnsSuggestions()
    {
        // Arrange
        var json = ToolUseResponse(
            $$"""[{ "transaction_id": "{{TransactionA}}", "category_id": "{{CategoryA}}", "confidence": 0.93 }]"""
        );

        // Act
        var result = ClaudeCategorizer.ParseResponse(json, [CategoryA]);

        // Assert
        Assert.True(result.IsSuccess);
        var suggestion = Assert.Single(result.Value!);
        Assert.Equal(TransactionA, suggestion.TransactionId);
        Assert.Equal(CategoryA, suggestion.CategoryId);
        Assert.Equal(0.93m, suggestion.Confidence);
    }

    [Fact]
    public void ParseResponse_UnknownCategoryId_BecomesNull()
    {
        // Arrange
        var json = ToolUseResponse(
            $$"""[{ "transaction_id": "{{TransactionA}}", "category_id": "{{Guid.NewGuid()}}", "confidence": 0.9 }]"""
        );

        // Act
        var result = ClaudeCategorizer.ParseResponse(json, [CategoryA]);

        // Assert
        Assert.Null(Assert.Single(result.Value!).CategoryId);
    }

    [Fact]
    public void ParseResponse_ConfidenceOutsideRange_IsClamped()
    {
        // Arrange
        var json = ToolUseResponse(
            $$"""[{ "transaction_id": "{{TransactionA}}", "category_id": "{{CategoryA}}", "confidence": 1.7 }]"""
        );

        // Act
        var result = ClaudeCategorizer.ParseResponse(json, [CategoryA]);

        // Assert
        Assert.Equal(1m, Assert.Single(result.Value!).Confidence);
    }

    [Fact]
    public void ParseResponse_WithoutToolUseBlock_Fails()
    {
        // Act
        var result = ClaudeCategorizer.ParseResponse(
            """{ "content": [ { "type": "text", "text": "sorry" } ] }""",
            [CategoryA]
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("no structured", result.Error);
    }

    [Fact]
    public void ParseResponse_InvalidJson_Fails()
    {
        // Act
        var result = ClaudeCategorizer.ParseResponse("not json", [CategoryA]);

        // Assert
        Assert.True(result.IsFailure);
    }

    #endregion

    #region BuildSystemPrompt / BuildRequestBody

    [Fact]
    public void BuildSystemPrompt_ContainsCategoriesAndExamples()
    {
        // Act
        var prompt = ClaudeCategorizer.BuildSystemPrompt(
            [new CategoryOption(CategoryA, "Living › Groceries")],
            [new FewShotExample("REWE", "REWE SAGT DANKE", -23.45m, "Living › Groceries")]
        );

        // Assert
        Assert.Contains(CategoryA.ToString("D"), prompt);
        Assert.Contains("Living › Groceries", prompt);
        Assert.Contains("REWE SAGT DANKE", prompt);
    }

    [Fact]
    public void BuildRequestBody_ForcesToolChoiceWithTemperatureZero()
    {
        // Act
        var body = ClaudeCategorizer.BuildRequestBody(
            [new TransactionToCategorize(TransactionA, "REWE", "Einkauf", -12.34m, "EUR")],
            [new CategoryOption(CategoryA, "Living › Groceries")],
            []
        );

        // Assert
        Assert.Equal(ClaudeCategorizer.Model, body["model"]!.GetValue<string>());
        Assert.Equal(0, body["temperature"]!.GetValue<int>());
        Assert.Equal("tool", body["tool_choice"]!["type"]!.GetValue<string>());
        Assert.Contains(TransactionA.ToString("D"), body.ToJsonString());
    }

    [Fact]
    public void BuildRequestBody_IncludesBookingDateAndRecurrenceOnlyWhenPresent()
    {
        // Arrange
        var withHints = new TransactionToCategorize(
            TransactionA,
            "UNIQA",
            "premium",
            -142.50m,
            "EUR",
            new DateOnly(2026, 6, 15),
            "recurs monthly with a consistent amount"
        );
        var plain = new TransactionToCategorize(Guid.NewGuid(), "Billa", "shop", -9m, "EUR");

        // Act
        var hinted = ClaudeCategorizer.TransactionLine(withHints);
        var bare = ClaudeCategorizer.TransactionLine(plain);

        // Assert
        Assert.Equal("2026-06-15", hinted["booking_date"]!.GetValue<string>());
        Assert.Equal(
            "recurs monthly with a consistent amount",
            hinted["recurrence"]!.GetValue<string>()
        );
        Assert.False(bare.ContainsKey("booking_date"));
        Assert.False(bare.ContainsKey("recurrence"));
    }

    [Fact]
    public void BuildSystemPrompt_ExplainsTheRecurrenceNote()
    {
        // Act
        var prompt = ClaudeCategorizer.BuildSystemPrompt(
            [new CategoryOption(CategoryA, "Living › Groceries")],
            []
        );

        // Assert
        Assert.Contains("\"recurrence\" note", prompt);
        Assert.Contains("insurance premiums", prompt);
        Assert.DoesNotContain("Confirmed examples", prompt);
    }

    #endregion

    #region SuggestAsync

    [Fact]
    public async Task SuggestAsync_WithoutApiKey_FailsGracefully()
    {
        // Arrange
        var categorizer = BuildCategorizer(apiKey: null, HttpStatusCode.OK, "{}");

        // Act
        var result = await categorizer.SuggestAsync(
            [new TransactionToCategorize(TransactionA, null, "x", -1m, "EUR")],
            [new CategoryOption(CategoryA, "A")],
            [],
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("No Claude API key", result.Error);
    }

    [Fact]
    public async Task SuggestAsync_SuccessResponse_ReturnsSuggestions()
    {
        // Arrange
        var response = ToolUseResponse(
            $$"""[{ "transaction_id": "{{TransactionA}}", "category_id": "{{CategoryA}}", "confidence": 0.95 }]"""
        );
        var categorizer = BuildCategorizer("sk-ant-test", HttpStatusCode.OK, response);

        // Act
        var result = await categorizer.SuggestAsync(
            [new TransactionToCategorize(TransactionA, "REWE", "Einkauf", -12.34m, "EUR")],
            [new CategoryOption(CategoryA, "Living › Groceries")],
            [],
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(CategoryA, Assert.Single(result.Value!).CategoryId);
    }

    [Fact]
    public async Task SuggestAsync_ApiError_FailsWithStatusCode()
    {
        // Arrange
        var categorizer = BuildCategorizer("sk-ant-test", HttpStatusCode.Unauthorized, "{}");

        // Act
        var result = await categorizer.SuggestAsync(
            [new TransactionToCategorize(TransactionA, null, "x", -1m, "EUR")],
            [new CategoryOption(CategoryA, "A")],
            [],
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("401", result.Error);
    }

    [Fact]
    public async Task SuggestAsync_EmptyBatch_ReturnsEmptyWithoutApiCall()
    {
        // Arrange
        var categorizer = BuildCategorizer(apiKey: null, HttpStatusCode.OK, "{}");

        // Act
        var result = await categorizer.SuggestAsync(
            [],
            [],
            [],
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    private static ClaudeCategorizer BuildCategorizer(
        string? apiKey,
        HttpStatusCode statusCode,
        string responseBody
    )
    {
        var credentials = Substitute.For<ICredentialStore>();
        credentials
            .GetSecretAsync(CredentialKeys.ClaudeApiKey, Arg.Any<CancellationToken>())
            .Returns(apiKey);
        var httpClient = new HttpClient(new FakeHandler(statusCode, responseBody))
        {
            BaseAddress = new Uri("https://api.anthropic.example/"),
        };
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(httpClient);
        return new ClaudeCategorizer(httpClientFactory, credentials);
    }

    private sealed class FakeHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(statusCode) { Content = new StringContent(body) }
            );
    }

    #endregion
}
