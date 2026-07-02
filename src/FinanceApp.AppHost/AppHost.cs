var builder = DistributedApplication.CreateBuilder(args);

var databaseUser = builder.AddParameter("database-user", "db-user");
var databasePassword = builder.AddParameter("database-password", secret: true);

var mailhog = builder
    .AddContainer("mailhog", "mailhog/mailhog")
    .WithEndpoint(1025, 1025, name: "smtp")
    .WithHttpEndpoint(8025, 8025, name: "web");

var postgresServer = builder
    .AddPostgres("postgres-server", databaseUser, databasePassword)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithHostPort(5432)
    .WithDataVolume();
var financeAppDb = postgresServer.AddDatabase("financeapp-database");

builder
    .AddProject<Projects.FinanceApp>("financeapp")
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
