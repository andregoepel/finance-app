using AndreGoepel.Core;
using Marten;

namespace AndreGoepel.FinanceApp.Domain.Categories;

public sealed record DeleteCategoryRuleCommand(Guid RuleId);

public static class DeleteCategoryRuleCommandHandler
{
    public static async Task<Result> Handle(
        DeleteCategoryRuleCommand command,
        IDocumentSession session,
        CancellationToken cancellationToken
    )
    {
        var rule = await session.LoadAsync<CategoryRule>(command.RuleId, cancellationToken);
        if (rule is null)
        {
            return Result.Fail("Rule not found.");
        }

        session.Delete(rule);
        await session.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
