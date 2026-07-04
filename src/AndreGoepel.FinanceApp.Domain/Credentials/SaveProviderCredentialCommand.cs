namespace AndreGoepel.FinanceApp.Domain.Credentials;

/// <summary>Stores or rotates a provider secret. The payload is never logged.</summary>
public sealed record SaveProviderCredentialCommand(string Key, string Secret);

public static class SaveProviderCredentialCommandHandler
{
    public static async Task<Result> Handle(
        SaveProviderCredentialCommand command,
        ICredentialStore credentialStore,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(command.Key))
        {
            return Result.Fail("Credential key is required.");
        }
        if (string.IsNullOrWhiteSpace(command.Secret))
        {
            return Result.Fail("The secret must not be empty.");
        }

        await credentialStore.SaveSecretAsync(
            command.Key,
            command.Secret.Trim(),
            cancellationToken
        );
        return Result.Ok();
    }
}
