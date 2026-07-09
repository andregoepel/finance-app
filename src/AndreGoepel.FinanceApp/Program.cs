using AndreGoepel.AppFoundation;
using AndreGoepel.AppFoundation.Hosting;
using AndreGoepel.FinanceApp;
using AndreGoepel.FinanceApp.Categorization;
using AndreGoepel.FinanceApp.Components;
using AndreGoepel.FinanceApp.Connections;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.Marten.Identity.Blazor.Components.Account;

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
    options.Menu = typeof(FinanceNavMenu);
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

// Enable Banking consent redirect target (registered as the app's redirect URI).
app.MapEnableBankingCallback();

app.Run();
