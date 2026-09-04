using System.Text.Json;
using System.Text.Json.Serialization;

namespace AndreGoepel.FinanceApp.Domain.Budgets;

/// <summary>
/// Reads <see cref="Budget"/> rows written before it had <see cref="Budget.CategoryId"/>/
/// <see cref="Budget.StartMonth"/> — back when the document id doubled as the category id and
/// a budget had no time bound. Without this, the default converter throws on any pre-existing
/// row because those properties are now <c>required</c>. A missing <c>CategoryId</c> falls back
/// to the document id (that's what it always meant before); a missing <c>StartMonth</c> falls
/// back to <see cref="DateOnly.MinValue"/> so the budget keeps applying to every month, exactly
/// as it did with no period at all. The row heals itself the next time it's saved through
/// <see cref="SetBudgetCommand"/>, which always writes every field — this converter only needs
/// to carry old rows until then.
/// </summary>
internal sealed class BudgetJsonConverter : JsonConverter<Budget>
{
    public override Budget? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var id = GetGuid(root, "Id");
        return new Budget
        {
            Id = id,
            CategoryId = TryGetGuid(root, "CategoryId") ?? id,
            MonthlyLimit = GetDecimal(root, "MonthlyLimit"),
            StartMonth = TryGetDate(root, "StartMonth") ?? DateOnly.MinValue,
            EndMonth = TryGetDate(root, "EndMonth"),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        Budget value,
        JsonSerializerOptions options
    ) =>
        JsonSerializer.Serialize(
            writer,
            new BudgetDto(
                value.Id,
                value.CategoryId,
                value.MonthlyLimit,
                value.StartMonth,
                value.EndMonth
            ),
            options
        );

    /// <summary>Mirrors <see cref="Budget"/> exactly so writing goes through the ambient naming policy rather than a hardcoded one.</summary>
    private sealed record BudgetDto(
        Guid Id,
        Guid CategoryId,
        decimal MonthlyLimit,
        DateOnly StartMonth,
        DateOnly? EndMonth
    );

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static Guid GetGuid(JsonElement root, string name) =>
        TryGetProperty(root, name, out var value)
            ? value.GetGuid()
            : throw new JsonException($"Budget JSON is missing required property '{name}'.");

    private static Guid? TryGetGuid(JsonElement root, string name) =>
        TryGetProperty(root, name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetGuid()
            : null;

    private static decimal GetDecimal(JsonElement root, string name) =>
        TryGetProperty(root, name, out var value)
            ? value.GetDecimal()
            : throw new JsonException($"Budget JSON is missing required property '{name}'.");

    private static DateOnly? TryGetDate(JsonElement root, string name) =>
        TryGetProperty(root, name, out var value) && value.ValueKind != JsonValueKind.Null
            ? DateOnly.Parse(value.GetString()!)
            : null;
}
