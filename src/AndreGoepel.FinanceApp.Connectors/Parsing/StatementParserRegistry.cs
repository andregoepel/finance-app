using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Parsing;

/// <summary>Implements <see cref="IStatementParserRegistry"/> over the registered parsers.</summary>
internal sealed class StatementParserRegistry(IEnumerable<IStatementParser> parsers)
    : IStatementParserRegistry
{
    public Result<StatementParseResult> Parse(ProviderKind provider, StatementFile file)
    {
        var providerParsers = parsers.Where(parser => parser.Provider == provider).ToList();
        if (providerParsers.Count == 0)
        {
            return Result.Fail<StatementParseResult>(
                $"No statement parser is registered for provider {provider}."
            );
        }

        var parser = providerParsers.FirstOrDefault(parser => parser.CanParse(file));
        if (parser is null)
        {
            var known = string.Join(", ", providerParsers.Select(p => p.ParserId));
            return Result.Fail<StatementParseResult>(
                $"Unrecognized {provider} export format. Supported formats: {known}. "
                    + "The provider may have changed its export — a new parser version is needed."
            );
        }

        return Result.Ok(parser.Parse(file));
    }
}
