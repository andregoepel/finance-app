using AndreGoepel.FinanceApp.Domain;

namespace AndreGoepel.FinanceApp.Connectors.Providers.Wise;

/// <summary>
/// Thin HTTP client over the Wise personal API (profiles, balances, statements).
/// The API token is passed per call by the connector — it belongs to a connection
/// and is read from the credential store just-in-time, never held in a field.
/// Isolated behind an interface so <see cref="WiseConnector"/> is unit-testable
/// with a mocked API and no network — the project's testing rule.
/// </summary>
public interface IWiseApiClient
{
    /// <summary>Wise profile ids visible to the token (usually one personal profile).</summary>
    Task<Result<IReadOnlyList<WiseProfile>>> GetProfilesAsync(
        string apiToken,
        CancellationToken cancellationToken = default
    );

    /// <summary>Statement lines for a balance over a window (raw JSON).</summary>
    Task<Result<string>> GetStatementJsonAsync(
        string apiToken,
        long profileId,
        string balanceId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default
    );
}

public sealed record WiseProfile(long Id, string Type);

/// <summary>
/// Phase 3 skeleton client. The named HttpClient and token header are wired; the
/// request/response bodies are filled in together against the Wise API.
/// </summary>
internal sealed class WiseApiClient(IHttpClientFactory httpClientFactory) : IWiseApiClient
{
    internal const string HttpClientName = "wise";

    public Task<Result<IReadOnlyList<WiseProfile>>> GetProfilesAsync(
        string apiToken,
        CancellationToken cancellationToken = default
    )
    {
        _ = httpClientFactory;
        return Task.FromResult(
            Result.Fail<IReadOnlyList<WiseProfile>>(
                "Wise API client is not implemented yet — Phase 3 next step."
            )
        );
    }

    public Task<Result<string>> GetStatementJsonAsync(
        string apiToken,
        long profileId,
        string balanceId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            Result.Fail<string>("Wise API client is not implemented yet — Phase 3 next step.")
        );
}
