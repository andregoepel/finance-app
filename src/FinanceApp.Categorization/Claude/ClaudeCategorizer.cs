using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FinanceApp.Domain;
using FinanceApp.Domain.Credentials;

namespace FinanceApp.Categorization.Claude;

/// <summary>
/// Claude Messages API client: batches of transactions, structured output via a
/// forced tool call, temperature 0, Haiku-class model. The API key comes from
/// the encrypted credential store and is never logged.
/// </summary>
/// <remarks>
/// Takes <see cref="IHttpClientFactory"/> (a named client) rather than a typed
/// <c>HttpClient</c>: Wolverine generates handler code that constructs its
/// dependencies inline and forbids service location, which a typed-client factory
/// registration would require. A plain service depending on the factory is
/// inline-constructable.
/// </remarks>
internal sealed class ClaudeCategorizer(
    IHttpClientFactory httpClientFactory,
    ICredentialStore credentialStore
) : IClaudeCategorizer
{
    internal const string HttpClientName = "claude";
    internal const string Model = "claude-haiku-4-5-20251001";
    private const string ToolName = "categorize_transactions";

    public async Task<Result<IReadOnlyList<ClaudeCategorySuggestion>>> SuggestAsync(
        IReadOnlyList<TransactionToCategorize> transactions,
        IReadOnlyList<CategoryOption> categories,
        IReadOnlyList<FewShotExample> examples,
        CancellationToken cancellationToken = default
    )
    {
        if (transactions.Count == 0)
        {
            return Result.Ok<IReadOnlyList<ClaudeCategorySuggestion>>([]);
        }

        var apiKey = await credentialStore.GetSecretAsync(
            CredentialKeys.ClaudeApiKey,
            cancellationToken
        );
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Result.Fail<IReadOnlyList<ClaudeCategorySuggestion>>(
                "No Claude API key is configured (Settings → API Keys)."
            );
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = JsonContent.Create(BuildRequestBody(transactions, categories, examples));

        // Factory-created clients are pooled by the factory — do not dispose here.
        var httpClient = httpClientFactory.CreateClient(HttpClientName);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Fail<IReadOnlyList<ClaudeCategorySuggestion>>(
                    $"Claude API returned {(int)response.StatusCode} {response.StatusCode}."
                );
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseResponse(json, categories.Select(c => c.Id).ToHashSet());
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException)
        {
            return Result.Fail<IReadOnlyList<ClaudeCategorySuggestion>>(
                $"Claude API unreachable: {exception.Message}"
            );
        }
    }

    internal static JsonObject BuildRequestBody(
        IReadOnlyList<TransactionToCategorize> transactions,
        IReadOnlyList<CategoryOption> categories,
        IReadOnlyList<FewShotExample> examples
    )
    {
        var transactionLines = transactions.Select(t => new JsonObject
        {
            ["transaction_id"] = t.TransactionId.ToString("D"),
            ["counterparty"] = t.Counterparty,
            ["description"] = t.Description,
            ["amount"] = t.Amount,
            ["currency"] = t.Currency,
        });

        return new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = 4096,
            ["temperature"] = 0,
            ["system"] = BuildSystemPrompt(categories, examples),
            ["messages"] = new JsonArray(
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray(
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] =
                                "Categorize these household transactions:\n"
                                + new JsonArray([.. transactionLines]).ToJsonString(),
                        }
                    ),
                }
            ),
            ["tools"] = new JsonArray(
                new JsonObject
                {
                    ["name"] = ToolName,
                    ["description"] =
                        "Report the category decision for every transaction in the batch.",
                    ["input_schema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["categorizations"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject
                                {
                                    ["type"] = "object",
                                    ["properties"] = new JsonObject
                                    {
                                        ["transaction_id"] = new JsonObject { ["type"] = "string" },
                                        ["category_id"] = new JsonObject
                                        {
                                            ["type"] = new JsonArray("string", "null"),
                                        },
                                        ["confidence"] = new JsonObject { ["type"] = "number" },
                                    },
                                    ["required"] = new JsonArray(
                                        "transaction_id",
                                        "category_id",
                                        "confidence"
                                    ),
                                },
                            },
                        },
                        ["required"] = new JsonArray("categorizations"),
                    },
                }
            ),
            ["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = ToolName },
        };
    }

    internal static string BuildSystemPrompt(
        IReadOnlyList<CategoryOption> categories,
        IReadOnlyList<FewShotExample> examples
    )
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine(
            "You categorize household bank transactions for a private German/Austrian household."
        );
        builder.AppendLine(
            "For every transaction pick the best matching category id from the list below, "
                + "or null when no category fits. Report a confidence between 0 and 1; use low "
                + "confidence when unsure. Negative amounts are expenses, positive amounts income."
        );
        builder.AppendLine();
        builder.AppendLine("Available categories (id: path):");
        foreach (var category in categories)
        {
            builder.AppendLine($"{category.Id:D}: {category.Path}");
        }

        if (examples.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Confirmed examples from this household's history:");
            foreach (var example in examples)
            {
                builder.AppendLine(
                    $"- counterparty: {example.Counterparty ?? "-"} | description: {example.Description} | amount: {example.Amount} => {example.CategoryPath}"
                );
            }
        }

        return builder.ToString();
    }

    internal static Result<IReadOnlyList<ClaudeCategorySuggestion>> ParseResponse(
        string json,
        HashSet<Guid> validCategoryIds
    )
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            return Result.Fail<IReadOnlyList<ClaudeCategorySuggestion>>(
                $"Unreadable Claude response: {exception.Message}"
            );
        }

        var toolUse = (root?["content"] as JsonArray)?.FirstOrDefault(block =>
            block?["type"]?.GetValue<string>() == "tool_use"
        );
        if (toolUse?["input"]?["categorizations"] is not JsonArray categorizations)
        {
            return Result.Fail<IReadOnlyList<ClaudeCategorySuggestion>>(
                "Claude response contained no structured categorization output."
            );
        }

        var suggestions = new List<ClaudeCategorySuggestion>();
        foreach (var entry in categorizations)
        {
            if (!Guid.TryParse(entry?["transaction_id"]?.GetValue<string>(), out var transactionId))
            {
                continue;
            }

            Guid? categoryId = null;
            if (
                Guid.TryParse(entry?["category_id"]?.GetValue<string?>(), out var parsedCategory)
                && validCategoryIds.Contains(parsedCategory)
            )
            {
                categoryId = parsedCategory;
            }

            var confidence = entry?["confidence"]?.GetValue<decimal>() ?? 0m;
            suggestions.Add(
                new ClaudeCategorySuggestion(
                    transactionId,
                    categoryId,
                    Math.Clamp(confidence, 0m, 1m)
                )
            );
        }

        return Result.Ok<IReadOnlyList<ClaudeCategorySuggestion>>(suggestions);
    }
}
