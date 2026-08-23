using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Resources;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Credentials;

/// <summary>Stores or rotates a provider secret. The payload is never logged.</summary>
public sealed record SaveProviderCredentialCommand(string Key, string Secret);

public static class SaveProviderCredentialCommandHandler
{
    public static async Task<Result> Handle(
        SaveProviderCredentialCommand command,
        ICredentialStore credentialStore,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(command.Key))
        {
            return Result.Fail(localizer["Error.CredentialKeyRequired"]);
        }
        if (string.IsNullOrWhiteSpace(command.Secret))
        {
            return Result.Fail(localizer["Error.SecretMustNotBeEmpty"]);
        }

        await credentialStore.SaveSecretAsync(
            command.Key,
            command.Secret.Trim(),
            cancellationToken
        );
        return Result.Ok();
    }
}
