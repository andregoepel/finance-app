using AndreGoepel.FinanceApp.Domain;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Parsing;

/// <summary>
/// Selects the matching format version for an uploaded file. Unknown formats
/// fail loudly with the list of supported formats — never a best-effort parse.
/// </summary>
public interface IStatementParserRegistry
{
    Result<StatementParseResult> Parse(ProviderKind provider, StatementFile file);
}
