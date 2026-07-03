namespace FinanceApp.Connectors.Tests;

internal static class Fixtures
{
    public static string Read(string provider, string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", provider, file));
}
