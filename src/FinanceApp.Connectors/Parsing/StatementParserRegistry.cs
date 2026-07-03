using FinanceApp.Domain;
using FinanceApp.Domain.Providers;

namespace FinanceApp.Connectors.Parsing;

/// <summary>
/// Selects the matching format version for an uploaded file. Unknown formats
/// fail loudly with the list of supported formats — never a best-effort parse.
/// </summary>
public interface IStatementParserRegistry
{
    Result<StatementParseResult> Parse(ProviderKind provider, string content);
}

internal sealed class StatementParserRegistry(IEnumerable<IStatementParser> parsers)
    : IStatementParserRegistry
{
    public Result<StatementParseResult> Parse(ProviderKind provider, string content)
    {
        var providerParsers = parsers.Where(parser => parser.Provider == provider).ToList();
        if (providerParsers.Count == 0)
        {
            return Result.Fail<StatementParseResult>(
                $"No statement parser is registered for provider {provider}."
            );
        }

        var parser = providerParsers.FirstOrDefault(parser => parser.CanParse(content));
        if (parser is null)
        {
            var known = string.Join(", ", providerParsers.Select(p => p.ParserId));
            return Result.Fail<StatementParseResult>(
                $"Unrecognized {provider} export format. Supported formats: {known}. "
                    + "The provider may have changed its export — a new parser version is needed."
            );
        }

        return Result.Ok(parser.Parse(content));
    }
}
