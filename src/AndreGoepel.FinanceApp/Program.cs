using AndreGoepel.AppFoundation;
using AndreGoepel.AppFoundation.Hosting;
using AndreGoepel.Design.Blazor;
using AndreGoepel.FinanceApp;
using AndreGoepel.FinanceApp.Categorization.Claude;
using AndreGoepel.FinanceApp.Components;
using AndreGoepel.FinanceApp.Connections;
using AndreGoepel.FinanceApp.Domain.Categories;
using AndreGoepel.FinanceApp.Domain.Imports;
using AndreGoepel.Marten.Identity.Blazor.Components.Account;

var builder = WebApplication.CreateBuilder(args);

builder.AddAppFoundation(options =>
{
    options.DatabaseConnectionName = "financeapp-database";
    options.WolverineServiceName = "FinanceApp";

    // The host owns the single UseWolverine call; opt the finance handler assemblies into discovery here.
    options.ConfigureWolverine = wolverine =>
    {
        wolverine.Discovery.IncludeAssembly(typeof(ImportStatementCommand).Assembly);
        wolverine.Discovery.IncludeAssembly(typeof(IClaudeCategorizer).Assembly);

        // One Claude request at a time: a backfill cascades dozens of batches, and
        // running them in parallel would only trade the timeout for rate limits.
        wolverine.LocalQueueFor<CategorizeTransactionBatchCommand>().Sequential();
    };
});

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Design-system services (ConfirmService, etc.). Must run after AddRadzenComponents
// (called inside AddAppFoundation) since it builds on Radzen's DialogService.
builder.Services.AddDesignBlazor(options => options.BrandName = "Finance");

builder.Services.AddFinanceApp();

builder.Services.Configure<AppFoundationLayoutOptions>(options =>
{
    options.BrandName = "Finance";
    options.Copyright = "Finance © 2026";
    options.Menu = typeof(FinanceNavMenu);
    // The finance dashboard lives at "/", not at the packaged /dashboard page.
    options.HomeUrl = "/";
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
