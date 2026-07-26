using AndreGoepel.Core;

namespace AndreGoepel.FinanceApp.Domain.Providers;

/// <summary>Resolves the connector responsible for a provider; fails loudly when none is registered.</summary>
public interface IProviderConnectorRegistry
{
    Result<IProviderConnector> ForProvider(ProviderKind provider);
}
