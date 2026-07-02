using AndreGoepel.AppFoundation;
using AndreGoepel.AppFoundation.Hosting;
using AndreGoepel.Marten.Identity.Blazor.Components.Account;
using FinanceApp;
using FinanceApp.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddAppFoundation(options =>
{
    options.DatabaseConnectionName = "financeapp-database";
    options.WolverineServiceName = "FinanceApp";
});

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddFinanceApp(builder.Configuration);

builder.Services.Configure<AppFoundationLayoutOptions>(options =>
{
    options.BrandName = "Finance";
    options.Copyright = "Finance © 2026";
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
