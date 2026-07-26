using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Providers;

namespace AndreGoepel.FinanceApp.Connectors.Sync;

/// <summary>
/// Selects the API connector for a provider. Crypto exports and Easy Bank stay
/// file-only (no connector); asking for their connector fails loudly rather than
/// silently doing nothing.
/// </summary>
internal sealed class ProviderConnectorRegistry(IEnumerable<IProviderConnector> connectors)
    : IProviderConnectorRegistry
{
    public Result<IProviderConnector> ForProvider(ProviderKind provider)
    {
        var connector = connectors.FirstOrDefault(c => c.Supports(provider));
        return connector is null
            ? Result.Fail<IProviderConnector>(
                $"No API connector is registered for provider {provider}. "
                    + "This provider is import-only (CSV/XLSX upload)."
            )
            : Result.Ok(connector);
    }
}
