# finance-app

Private household finance app for two users (shared household view). Aggregates
accounts and transactions from Wise, Revolut, Crypto.com, DKB and Easy Bank via API
sync and CSV upload; every transaction gets a category (provider mapping → learned
rules → Claude API fallback with review queue).

Host app composing [`andregoepel/app-foundation`](https://github.com/andregoepel/app-foundation)
NuGet packages — identity, management shell, mail, service defaults and OTel come
from the foundation.

See [PLAN.md](PLAN.md) for the implementation plan (phases 0–5),
[CLAUDE.md](CLAUDE.md) for project conventions, and
[docs/data-protection.md](docs/data-protection.md) for key ring encryption,
backup and rotation.

## Getting started

Requires the .NET SDK pinned in [global.json](global.json) and Docker (Postgres and
MailHog run as containers via .NET Aspire).

```
dotnet run --project src/FinanceApp.AppHost
```

On first launch, visit `/Setup` to create the administrator account.

## Development

- Build: `dotnet build`
- Test: `dotnet test`
- Format: `dotnet csharpier format .`
