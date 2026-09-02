using AndreGoepel.FinanceApp.Categorization.Claude;
using AndreGoepel.FinanceApp.Domain.Imports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Runtime;

namespace AndreGoepel.FinanceApp.Tests;

/// <summary>
/// Guards the Wolverine handler wiring the host relies on. A command whose
/// handler Wolverine does not discover (e.g. an <c>internal</c> handler class)
/// is published into the void without any error, so this is the only place
/// such a regression shows up before production.
/// </summary>
public sealed class HandlerDiscoveryTests
{
    [Fact]
    public async Task UseWolverine_WithFinanceHandlerAssemblies_DiscoversHandlerForEveryCommand()
    {
        // Arrange — the same assemblies Program.cs opts into discovery.
        var handlerAssemblies = new[]
        {
            typeof(ImportStatementCommand).Assembly,
            typeof(IClaudeCategorizer).Assembly,
        };
        var commandTypes = handlerAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.Name.EndsWith("Command", StringComparison.Ordinal))
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .OrderBy(type => type.Name)
            .ToList();
        Assert.NotEmpty(commandTypes);

        using var host = Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                foreach (var assembly in handlerAssemblies)
                {
                    options.Discovery.IncludeAssembly(assembly);
                }
            })
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        // Act
        var runtime = (WolverineRuntime)host.Services.GetRequiredService<IWolverineRuntime>();
        var withoutHandler = commandTypes
            .Where(type => runtime.Handlers.ChainFor(type) is null)
            .Select(type => type.Name)
            .ToList();

        await host.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(
            withoutHandler.Count == 0,
            $"No Wolverine handler discovered for: {string.Join(", ", withoutHandler)}"
        );
    }
}
