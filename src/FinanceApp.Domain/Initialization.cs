using FinanceApp.Domain.Categories;
using FinanceApp.Domain.Credentials;
using FinanceApp.Domain.Transactions;
using JasperFx.Events.Projections;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Attributes;

[assembly: WolverineModule]

namespace FinanceApp.Domain;

public static class Initialization
{
    /// <summary>
    /// Registers the finance domain with the Marten store configured by
    /// app-foundation: the inline <see cref="TransactionView"/> projection
    /// (inline so dedup checks see rows imported in the same session), dedup
    /// indexes, and the default category seed. Wolverine discovers the command
    /// handlers via the assembly-level <see cref="WolverineModuleAttribute"/>.
    /// </summary>
    public static IServiceCollection AddFinanceDomain(this IServiceCollection services)
    {
        services.ConfigureMarten(options =>
        {
            options.Projections.Snapshot<TransactionView>(SnapshotLifecycle.Inline);

            options.Schema.For<TransactionView>().Index(x => x.AccountId).Index(x => x.DedupHash);
        });

        services.InitializeMartenWith(new DefaultCategorySeed());

        services.AddSingleton<ICredentialStore, MartenCredentialStore>();

        return services;
    }
}
