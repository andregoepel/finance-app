using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Providers;
using AndreGoepel.FinanceApp.Domain.Resources;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Connectors.Parsing;

/// <summary>Implements <see cref="IStatementParserRegistry"/> over the registered parsers.</summary>
internal sealed class StatementParserRegistry(
    IEnumerable<IStatementParser> parsers,
    IStringLocalizer<DomainStrings> localizer
) : IStatementParserRegistry
{
    public Result<StatementParseResult> Parse(ProviderKind provider, StatementFile file)
    {
        var providerParsers = parsers.Where(parser => parser.Provider == provider).ToList();
        if (providerParsers.Count == 0)
        {
            return Result.Fail<StatementParseResult>(
                localizer["Error.NoParserForProvider", provider]
            );
        }

        var parser = providerParsers.FirstOrDefault(parser => parser.CanParse(file));
        if (parser is null)
        {
            var known = string.Join(", ", providerParsers.Select(p => p.ParserId));
            return Result.Fail<StatementParseResult>(
                localizer["Error.UnrecognizedExportFormat", provider, known]
            );
        }

        return Result.Ok(parser.Parse(file));
    }
}
