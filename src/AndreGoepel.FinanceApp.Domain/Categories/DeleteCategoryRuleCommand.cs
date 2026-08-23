using AndreGoepel.Core;
using AndreGoepel.FinanceApp.Domain.Resources;
using Marten;
using Microsoft.Extensions.Localization;

namespace AndreGoepel.FinanceApp.Domain.Categories;

public sealed record DeleteCategoryRuleCommand(Guid RuleId);

public static class DeleteCategoryRuleCommandHandler
{
    public static async Task<Result> Handle(
        DeleteCategoryRuleCommand command,
        IDocumentSession session,
        IStringLocalizer<DomainStrings> localizer,
        CancellationToken cancellationToken
    )
    {
        var rule = await session.LoadAsync<CategoryRule>(command.RuleId, cancellationToken);
        if (rule is null)
        {
            return Result.Fail(localizer["Error.RuleNotFound"]);
        }

        session.Delete(rule);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
