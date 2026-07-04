using AndreGoepel.FinanceApp.Connectors.Parsing;

namespace AndreGoepel.FinanceApp.Connectors.Tests;

internal static class Fixtures
{
    public static StatementFile Load(string provider, string file) =>
        new(
            file,
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", provider, file))
        );

    public static StatementFile Text(string content) =>
        new("inline.csv", System.Text.Encoding.UTF8.GetBytes(content));
}
