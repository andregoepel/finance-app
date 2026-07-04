namespace AndreGoepel.FinanceApp.Connections;

/// <summary>
/// The redirect endpoint Enable Banking sends the user back to after they
/// authorize a consent at their bank. Exchanges the code for a session and
/// redirects to the Connections page with a status — the browser-facing half of
/// the consent flow (the Connections page starts it).
/// </summary>
public static class EnableBankingCallbackEndpoint
{
    /// <summary>Route registered in the Enable Banking application as the redirect URI.</summary>
    public const string CallbackPath = "/enablebanking/callback";

    public static IEndpointRouteBuilder MapEnableBankingCallback(
        this IEndpointRouteBuilder endpoints
    )
    {
        endpoints.MapGet(
            CallbackPath,
            async (
                string? code,
                string? state,
                string? error,
                IProviderConnectionService connectionService,
                CancellationToken cancellationToken
            ) =>
            {
                if (!string.IsNullOrEmpty(error))
                {
                    return Results.Redirect(
                        $"/settings/connections?consent=denied&reason={Uri.EscapeDataString(error)}"
                    );
                }
                if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                {
                    return Results.Redirect("/settings/connections?consent=invalid");
                }

                var result = await connectionService.CompleteConsentAsync(
                    code,
                    state,
                    cancellationToken
                );
                return result.IsSuccess
                    ? Results.Redirect("/settings/connections?consent=ok")
                    : Results.Redirect(
                        $"/settings/connections?consent=failed&reason={Uri.EscapeDataString(result.Error!)}"
                    );
            }
        );

        return endpoints;
    }
}
