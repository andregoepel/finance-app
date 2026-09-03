# fincance-app

Private household finance app for two users (shared household view).
Aggregates accounts and transactions from Wise, Revolut, Crypto.com, DKB
and Easy Bank via API sync (Wise personal token, Enable Banking PSD2) and
CSV upload; every transaction gets a category: provider mapping → learned
rules → Claude API fallback with review queue. Host app composing
`AndreGoepel.AppFoundation` NuGet packages.

**The full implementation plan lives in `PLAN.md` — read it before starting
any phase.** Work proceeds phase by phase (0–5); do not pull work from
later phases forward without being asked.

## Solution Projects
- `AndreGoepel.FinanceApp` — Blazor host (App.razor, Routes.razor, feature UI)
- `AndreGoepel.FinanceApp.AppHost` — .NET Aspire host (Postgres container)
- `AndreGoepel.FinanceApp.Domain` — events, aggregates, commands, Wolverine handlers
- `AndreGoepel.FinanceApp.Connectors` — provider API clients + CSV statement parsers
- `AndreGoepel.FinanceApp.Categorization` — rules engine + Claude API client
- `tests/` — one test project per src project

## Domain Rules
- **Money:** `decimal` only — never `double`/`float`. Store original amount
  + currency AND the EUR amount (ECB rate at booking date). Round only at
  display time.
- **Idempotent imports:** every import (CSV or API) must be re-runnable
  without creating duplicates. Dedup hash: `(accountId, bookingDate,
  amount, normalized description)`. Track every run as an `ImportBatch`.
- **Never silently drop rows.** Parser failures surface in the import UI
  with row numbers. CSV parsers are versioned per provider format; unknown
  formats fail loudly.
- **Category changes are events** (`TransactionCategorized` /
  `TransactionCategoryCorrected`), never in-place updates. Corrections feed
  rule learning.
- **Transfers between own accounts** must be linkable
  (`TransactionLinkedAsTransfer`) and excluded from spending aggregations.
- **Credentials** live in the `ProviderCredential` document, encrypted via
  `IDataProtector` with a per-provider purpose string. Never log credential
  payloads, never write them to config files. The DataProtection key ring
  is persisted in Postgres.
- **Enable Banking:** account IDs are session-specific — always match
  accounts via `identification_hash`. Surface consent expiry; sync deep
  immediately after fresh consent (history window shrinks to ~90 days).
- **Claude categorization:** batch ~50 transactions per request, structured
  output, temperature 0. High confidence auto-applies (flagged "AI" in UI),
  low confidence goes to the review queue. Must degrade gracefully when the
  API is unavailable — imports never fail because of it.
- **Cash:** one cash account per user (`ProviderKind.Cash`, `AccountType.Cash`,
  `SyncMethod.Manual`). Transactions are typed in by hand
  (`RecordManualTransactionCommand`) and take the normal import shape (one
  `TransactionImported` stream + one-row `ImportBatch`, parser id
  `manual-entry`), so they flow through categorization, transfers and net
  worth like any other. The account's balance anchor is always the ledger
  balance (opening balance + every entry). Only manual entries may be deleted
  one at a time (`DeleteManualTransactionCommand`); imported history never is.

## Testing
- Scope: domain logic, handlers, parsers, rules engine, matching logic
- CSV parsers are tested against fixture files per provider
  (`tests/fixtures/<provider>/`); add a new fixture whenever a provider
  format changes
- E2E: `tests/AndreGoepel.FinanceApp.E2ETests` boots the real app (Postgres
  + MailHog via Aspire) and drives the Blazor UI in Chromium (Playwright).
  Needs Docker/Podman + installed Playwright browsers; excluded from the
  default `dotnet test` (`--filter "FullyQualifiedName!~E2ETests"`), runs
  in `e2e.yml`.
