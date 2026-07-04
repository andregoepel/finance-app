using AndreGoepel.AppFoundation;
using AndreGoepel.AppFoundation.Hosting;
using AndreGoepel.Marten.Identity.Blazor.Components.Account;
using FinanceApp;
using FinanceApp.Categorization;
using FinanceApp.Components;
using FinanceApp.Domain.Imports;

var builder = WebApplication.CreateBuilder(args);

builder.AddAppFoundation(options =>
{
    options.DatabaseConnectionName = "financeapp-database";
    options.WolverineServiceName = "FinanceApp";

    // The host owns the single UseWolverine call; opt the finance handler
    // assemblies into Wolverine's discovery here so command handlers (import,
    // categorization, …) are found and routed to.
    options.ConfigureWolverine = wolverine =>
    {
        wolverine.Discovery.IncludeAssembly(typeof(ImportStatementCommand).Assembly);
        wolverine.Discovery.IncludeAssembly(
            typeof(CategorizeImportedTransactionsCommandHandler).Assembly
        );
    };
});

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddFinanceApp();

builder.Services.Configure<AppFoundationLayoutOptions>(options =>
{
    options.BrandName = "Finance";
    options.Copyright = "Finance © 2026";
    options.AdminMenu = typeof(FinanceNavMenu);
});

var app = builder.Build();

app.UseAppFoundation();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(AppFoundationLayoutOptions).Assembly,
        typeof(AndreGoepel.Marten.Identity.Blazor.Initialization).Assembly
    );

app.MapAdditionalIdentityEndpoints();

app.Run();
