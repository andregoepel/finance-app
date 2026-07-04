using AndreGoepel.FinanceApp.Domain.Imports;

namespace AndreGoepel.FinanceApp.Domain.Providers;

/// <summary>
/// API-sync counterpart of <c>IStatementParser</c>: pulls transactions for one
/// account straight from a provider (Wise) or PSD2 aggregator (Enable Banking).
/// The output is the same <see cref="NormalizedTransaction"/> shape a parser
/// produces, so both paths share the dedup + import pipeline. Network failures
/// surface as a failed <see cref="Result"/>, never as an exception, and never
/// silently drop rows.
/// </summary>
public interface IProviderConnector
{
    /// <summary>Providers this connector can sync (Enable Banking serves several).</summary>
    bool Supports(ProviderKind provider);

    Task<Result<ProviderSyncResult>> FetchAsync(
        ProviderSyncRequest request,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Everything a connector needs to sync one account, resolved by the
/// application layer so connectors stay HTTP-only (no Marten dependency).
/// </summary>
/// <param name="AccountId">The household account being synced.</param>
/// <param name="Provider">Concrete provider (e.g. <c>Dkb</c> even for the Enable Banking connector).</param>
/// <param name="ConnectionId">The provider login (connection) the account syncs through — scopes the Wise token.</param>
/// <param name="ExternalId">Provider-side account id, where stable (Wise balance id).</param>
/// <param name="IdentificationHash">
/// Enable Banking's stable per-account hash — account ids are session-specific,
/// so accounts are matched via this hash, never the session id.
/// </param>
/// <param name="SessionReference">Active Enable Banking session id for this consent, if any.</param>
/// <param name="Since">Earliest booking date to fetch (window start).</param>
public sealed record ProviderSyncRequest(
    Guid AccountId,
    ProviderKind Provider,
    Guid ConnectionId,
    string? ExternalId,
    string? IdentificationHash,
    string? SessionReference,
    DateOnly Since
);

/// <summary>
/// Rows a connector fetched, ready for the import pipeline. <see cref="SyncSource"/>
/// is recorded on the <c>ImportBatch</c> as both source and parser id (e.g.
/// <c>wise-api-v1</c>) so an API sync is auditable exactly like a file import.
/// </summary>
public sealed record ProviderSyncResult(
    string SyncSource,
    IReadOnlyList<NormalizedTransaction> Rows,
    IReadOnlyList<ImportRowError> Errors
);

/// <summary>Resolves the connector responsible for a provider; fails loudly when none is registered.</summary>
public interface IProviderConnectorRegistry
{
    Result<IProviderConnector> ForProvider(ProviderKind provider);
}
