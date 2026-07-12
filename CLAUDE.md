# Project Instructions — finance-app

## Project Overview

Private household finance app for two users (shared household view). Aggregates
accounts and transactions from Wise, Revolut, Crypto.com, DKB and Easy Bank via API sync
(Wise personal token, Enable Banking PSD2) and CSV upload. Every transaction gets a
category: provider mapping → learned rules → Claude API fallback with review queue.
Insights: spending by category, net worth history, budgets, recurring detection, planned
costs with plan-vs-actual matching.

**The full implementation plan lives in `PLAN.md` — read it before starting any phase.**
Work proceeds phase by phase (0–5); do not pull work from later phases forward without
being asked.

Host app composing [`andregoepel/app-foundation`](https://github.com/andregoepel/app-foundation)
NuGet packages (`AndreGoepel.AppFoundation.Hosting` + `AndreGoepel.AppFoundation`).
Identity, management shell, mail, service defaults and OTel come from those packages —
never re-implement what the foundation provides.

**Solution projects:**
- `AndreGoepel.FinanceApp` — Blazor host (App.razor, Routes.razor, feature UI)
- `AndreGoepel.FinanceApp.AppHost` — .NET Aspire host (Postgres container)
- `AndreGoepel.FinanceApp.Domain` — events, aggregates, commands, Wolverine handlers
- `AndreGoepel.FinanceApp.Connectors` — provider API clients + CSV statement parsers
- `AndreGoepel.FinanceApp.Categorization` — rules engine + Claude API client
- `tests/` — one test project per src project

## Tech Stack

- .NET 10, Blazor InteractiveServer, .NET Aspire
- Marten + PostgreSQL (documents + event-sourced `Transaction` aggregate + projections)
- Wolverine (durable messaging), Quartz.NET (scheduled sync)
- Radzen (UI components)
- xUnit v3, bUnit, NSubstitute

## Commands

- Build: `dotnet build`
- Test: `dotnet test`
- Format: `csharpier format .` (run after every change)

## Git Workflow

- Branches: `feature/`, `bugfix/`, `hotfix/`
- Commits: `type: description` (feat, fix, refactor, test, docs)
- **Always create a branch before making any file edits.** Never edit files on `main`.
- **Never commit without explicit user confirmation.** Ask before every commit, no exceptions.
- **Never push to `main` or `master`.**
- **Never add a `Co-Authored-By` trailer to commits.**
- Run tests before committing

## Code Conventions

Identical to app-foundation — see its CLAUDE.md for the full set. Highlights:

- Commands `Create[Entity]Command`, queries `Get[Entity]Query`, handlers `[Command]Handler`, DTOs `[Entity]Dto`
- `Result<T>` for error handling — no exceptions for flow control
- Primary constructors for DI, records for DTOs/commands, `sealed internal` by default
- File-scoped namespaces; async/await with `CancellationToken` for all I/O
- Blazor: Radzen components, `@rendermode InteractiveServer` on pages, `<PageTitle>` on
  every routed page, `[Authorize]` attributes not conditionals, `_Imports.razor` for
  shared usings, `IDisposable` when subscribing to events
- Tests: `[Method]_[Scenario]_[ExpectedResult]`, `// Arrange` / `// Act` / `// Assert`

## Domain Rules (finance-specific)

- **Money:** `decimal` only — never `double`/`float`. Store original amount + currency AND
  the EUR amount (ECB rate at booking date). Round only at display time.
- **Idempotent imports:** every import (CSV or API) must be re-runnable without creating
  duplicates. Dedup hash: `(accountId, bookingDate, amount, normalized description)`.
  Track every run as an `ImportBatch`.
- **Never silently drop rows.** Parser failures surface in the import UI with row numbers.
  CSV parsers are versioned per provider format; unknown formats fail loudly.
- **Category changes are events**, never in-place updates — `TransactionCategorized` /
  `TransactionCategoryCorrected`. Corrections feed rule learning.
- **Transfers between own accounts** must be linkable (`TransactionLinkedAsTransfer`) and
  excluded from spending aggregations.
- **Credentials** live in the `ProviderCredential` document, encrypted via
  `IDataProtector` with a per-provider purpose string. Never log credential payloads,
  never write them to config files. The DataProtection key ring is persisted in Postgres.
- **Enable Banking:** account IDs are session-specific — always match accounts via
  `identification_hash`. Surface consent expiry; sync deep immediately after fresh consent
  (history window shrinks to ~90 days afterwards).
- **Claude categorization:** batch ~50 transactions per request, structured output,
  temperature 0. High confidence auto-applies (flagged "AI" in UI), low confidence goes to
  the review queue. Categorization must degrade gracefully when the API is unavailable —
  transactions stay in the queue, imports never fail because of it.

## Testing

- Scope: domain logic, handlers, parsers, rules engine, matching logic
- CSV parsers are tested against fixture files per provider (`tests/fixtures/<provider>/`);
  add a new fixture whenever a provider format changes
- Claude API and provider APIs are always mocked in tests (NSubstitute); no network in tests
- **E2E tests are the one exception to "no network / mocked".** `tests/AndreGoepel.FinanceApp.E2ETests`
  boots the real app (Postgres + MailHog via Aspire) and drives the Blazor UI in Chromium
  (Playwright). It needs a running Docker/Podman + installed Playwright browsers, so it is
  **excluded from the default `dotnet test`** (`--filter "FullyQualifiedName!~E2ETests"`) and runs
  in the dedicated `e2e.yml` workflow. See that project's `README.md`.
