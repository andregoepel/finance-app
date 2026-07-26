using AndreGoepel.AppFoundation.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

// The E2E suite starts this AppHost with E2E=true so each run gets a clean, throwaway
// database and no fixed host ports — never the developer's persistent local data.
var isE2E = string.Equals(builder.Configuration["E2E"], "true", StringComparison.OrdinalIgnoreCase);

var databaseUser = builder.AddParameter("database-user", "db-user");
var databasePassword = builder.AddParameter("database-password", secret: true);

var mailhog = builder.AddStandardMailHog();

var (_, financeAppDb) = builder.AddStandardPostgres(
    isE2E,
    databaseResourceName: "financeapp-database",
    userName: databaseUser,
    password: databasePassword
);

builder
    .AddProject<Projects.AndreGoepel_FinanceApp>("financeapp")
    .WithReference(financeAppDb)
    .WithEnvironment("EmailSender__SenderName", "Finance")
    .WithEnvironment("EmailSender__SenderEmail", "no-reply@localhost.dev")
    .WithEnvironment("EmailSender__Username", "test-mail")
    .WithEnvironment("EmailSender__Password", "12345678")
    .WithEnvironment("EmailSender__Port", () => mailhog.GetEndpoint("smtp").Port.ToString())
    .WithEnvironment("EmailSender__Server", () => mailhog.GetEndpoint("smtp").Host)
    .WaitFor(financeAppDb)
    .PublishAsDockerFile();

builder.Build().Run();
