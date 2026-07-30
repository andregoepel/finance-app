using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Credentials;
using AndreGoepel.FinanceApp.Domain.Transactions;
using JasperFx.Events.Projections;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.FinanceApp.Domain;

public static class Initialization
{
    /// <summary>
    /// Registers the finance domain with the Marten store configured by
    /// app-foundation: the inline <see cref="TransactionView"/> projection
    /// (inline so dedup checks see rows imported in the same session), dedup
    /// indexes, and the default category seed. The handler assembly is opted into
    /// Wolverine discovery from <c>Program.cs</c> via
    /// <c>AppFoundationOptions.ConfigureWolverine</c>.
    /// </summary>
    public static IServiceCollection AddFinanceDomain(this IServiceCollection services)
    {
        services.ConfigureMarten(ConfigureStore);

        services.InitializeMartenWith(new DefaultCategorySeed());

        services.AddSingleton<ICredentialStore, MartenCredentialStore>();

        return services;
    }

    /// <summary>
    /// The store shape the finance domain needs. Public so integration tests can
    /// build a store that matches the app's — projections included — instead of
    /// re-declaring it and drifting.
    /// </summary>
    public static void ConfigureStore(StoreOptions options)
    {
        options.Projections.Snapshot<TransactionView>(SnapshotLifecycle.Inline);

        options.Schema.For<TransactionView>().Index(x => x.AccountId).Index(x => x.DedupHash);
    }
}
