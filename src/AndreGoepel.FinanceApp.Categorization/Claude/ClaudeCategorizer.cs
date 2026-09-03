using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Credentials;
using Microsoft.Extensions.Logging;

namespace AndreGoepel.FinanceApp.Categorization.Claude;

/// <summary>
/// Claude Messages API client: batches of transactions, structured output via a
/// forced tool call, temperature 0, Haiku-class model. The API key comes from
/// the encrypted credential store and is never logged. The instructions, the
/// category tree and the recent examples form a cached prefix that every batch
/// of a run shares; see <see cref="BuildSystemBlocks"/>.
/// </summary>
/// <remarks>
/// Takes <see cref="IHttpClientFactory"/> (a named client) rather than a typed
/// <c>HttpClient</c>: Wolverine generates handler code that constructs its
/// dependencies inline and forbids service location, which a typed-client factory
/// registration would require. A plain service depending on the factory is
/// inline-constructable. The class is <c>public</c> (not <c>internal</c>) for the
/// same reason: Wolverine's generated handler assembly must be able to reference
/// the concrete type to construct it inline.
/// </remarks>
public sealed class ClaudeCategorizer(
    IHttpClientFactory httpClientFactory,
    ICredentialStore credentialStore,
    ILogger<ClaudeCategorizer> logger
) : IClaudeCategorizer
{
    internal const string HttpClientName = "claude";
    internal const string Model = "claude-haiku-4-5-20251001";
    private const string ToolName = "categorize_transactions";

    public async Task<Result<IReadOnlyList<ClaudeCategorySuggestion>>> SuggestAsync(
        IReadOnlyList<TransactionToCategorize> transactions,
        IReadOnlyList<CategoryOption> categories,
        FewShotExamples examples,
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
            LogUsage(json, transactions.Count);
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
        FewShotExamples examples
    )
    {
        var transactionLines = transactions.Select(TransactionLine);

        return new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = 4096,
            ["temperature"] = 0,
            ["system"] = BuildSystemBlocks(categories, examples),
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

    /// <summary>
    /// The system prompt as content blocks so the stable part can be served from
    /// the prompt cache: instructions, category tree and recent examples are the
    /// same for every batch of a run and end with the cache breakpoint; the
    /// batch's own counterparty examples follow it. Everything before the
    /// breakpoint (tools included) must stay byte-identical between requests, so
    /// nothing time- or batch-dependent may go there. The cache only kicks in
    /// once the prefix exceeds the model's minimum (4096 tokens on Haiku 4.5);
    /// the recent-example count in the handler is sized for that.
    /// </summary>
    internal static JsonArray BuildSystemBlocks(
        IReadOnlyList<CategoryOption> categories,
        FewShotExamples examples
    )
    {
        var blocks = new JsonArray(
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = BuildStablePrompt(categories, examples.Recent),
                ["cache_control"] = new JsonObject { ["type"] = "ephemeral" },
            }
        );
        if (examples.ForBatch.Count > 0)
        {
            blocks.Add(
                new JsonObject { ["type"] = "text", ["text"] = BuildBatchPrompt(examples.ForBatch) }
            );
        }
        return blocks;
    }

    internal static JsonObject TransactionLine(TransactionToCategorize transaction)
    {
        var line = new JsonObject
        {
            ["transaction_id"] = transaction.TransactionId.ToString("D"),
            ["counterparty"] = transaction.Counterparty,
            ["description"] = transaction.Description,
            ["amount"] = transaction.Amount,
            ["currency"] = transaction.Currency,
        };
        if (transaction.BookingDate is DateOnly bookingDate)
        {
            line["booking_date"] = bookingDate.ToString("yyyy-MM-dd");
        }
        if (!string.IsNullOrWhiteSpace(transaction.RecurrenceHint))
        {
            line["recurrence"] = transaction.RecurrenceHint;
        }
        return line;
    }

    internal static string BuildStablePrompt(
        IReadOnlyList<CategoryOption> categories,
        IReadOnlyList<FewShotExample> recentExamples
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
        builder.AppendLine(
            "A transaction may carry a \"recurrence\" note: the same counterparty has appeared at "
                + "that interval with a consistent amount. Treat it as a strong signal for "
                + "subscriptions, insurance premiums, rent, utilities, loan instalments and salary, "
                + "and prefer that reading over a one-off interpretation of the name."
        );
        builder.AppendLine();
        builder.AppendLine("Available categories (id: path):");
        foreach (var category in categories)
        {
            builder.AppendLine($"{category.Id:D}: {category.Path}");
        }

        if (recentExamples.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine(
                "Recently confirmed examples from this household's history (newest first):"
            );
            AppendExamples(builder, recentExamples);
        }

        return builder.ToString();
    }

    internal static string BuildBatchPrompt(IReadOnlyList<FewShotExample> batchExamples)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine(
            "Confirmed examples for counterparties in this batch — follow them when they apply:"
        );
        AppendExamples(builder, batchExamples);
        return builder.ToString();
    }

    private static void AppendExamples(
        System.Text.StringBuilder builder,
        IReadOnlyList<FewShotExample> examples
    )
    {
        foreach (var example in examples)
        {
            builder.AppendLine(
                $"- counterparty: {example.Counterparty ?? "-"} | description: {example.Description} | amount: {example.Amount} => {example.CategoryPath}"
            );
        }
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

    /// <summary>
    /// The token accounting of one call, so the log shows whether the cached prefix
    /// is actually being read (<c>cache_read_input_tokens</c> &gt; 0 from the second
    /// batch of a run on) or silently ignored because the prefix is too short.
    /// </summary>
    private void LogUsage(string json, int transactionCount)
    {
        JsonNode? usage;
        try
        {
            usage = JsonNode.Parse(json)?["usage"];
        }
        catch (JsonException)
        {
            return;
        }
        if (usage is null)
        {
            return;
        }

        logger.LogInformation(
            "Claude categorized {Count} transactions: {Input} uncached input tokens, {CacheRead} read from cache, {CacheWrite} written to cache, {Output} output tokens.",
            transactionCount,
            Tokens(usage, "input_tokens"),
            Tokens(usage, "cache_read_input_tokens"),
            Tokens(usage, "cache_creation_input_tokens"),
            Tokens(usage, "output_tokens")
        );
    }

    private static long Tokens(JsonNode usage, string field) =>
        usage[field] is JsonValue value && value.TryGetValue<long>(out var tokens) ? tokens : 0;
}
