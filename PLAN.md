# Plan — Household Finance App ("finance-app")

Private finance overview for André & wife across Wise, Revolut, Crypto.com, DKB and
Easy Bank. Transactions arrive via API sync or file upload; every transaction gets a
category (provider → rules → AI). Built as a **host app** composing
`andregoepel/app-foundation` NuGet packages.

Decisions made (2026-07-02):
- **Import:** hybrid from V1 — APIs where feasible + CSV upload for the rest
- **Categorization:** rules first, Claude API fallback, suggestions confirmed in a review queue
- **Users:** shared household view — both log in, see everything; accounts tagged with owner (André / wife / joint)
- **Dashboard scope:** spending by category, net worth over time, budgets, recurring detection
- **Planned costs (replaces wife's Excel):** planned items maintained in the app, auto-matched against actual transactions (plan vs. actual); manual re-entry, no Excel importer
- **Credentials:** stored in Postgres, encrypted with .NET DataProtection, managed via Settings UI — no secrets on disk except the DB connection string
- **Enable Banking:** confirmed — DKB + Revolut in catalog; restricted mode (own linked accounts), no contract/license needed for private use

---

## 1. Architecture

New repo (suggested: `andregoepel/finance-app`), same shape as other hosts of
app-foundation (cf. andregoepel.dev):

```
src/
  AndreGoepel.FinanceApp/                    # Blazor host — App.razor, Routes.razor, feature UI
  AndreGoepel.FinanceApp.AppHost/            # .NET Aspire host (Postgres container, app)
  AndreGoepel.FinanceApp.Domain/             # events, aggregates, commands, handlers (Wolverine)
  AndreGoepel.FinanceApp.Connectors/         # provider connectors (API clients + CSV parsers)
  AndreGoepel.FinanceApp.Categorization/     # rules engine + Claude API client
tests/
  AndreGoepel.FinanceApp.Domain.Tests/
  AndreGoepel.FinanceApp.Connectors.Tests/
  AndreGoepel.FinanceApp.Categorization.Tests/
```

- `AndreGoepel.FinanceApp` references `AndreGoepel.AppFoundation.Hosting` + `AndreGoepel.AppFoundation`
  → identity (marten-identity), management shell (Radzen layout, NavMenu, admin section),
  mail, service defaults, OTel come for free.
- Feature pages injected into the shell via `AppFoundationLayoutOptions.AdminMenu` /
  own nav entries; branding via `BrandName`/`LogoPath`.
- All conventions from app-foundation CLAUDE.md apply (Result<T>, records for
  commands/DTOs, sealed internal, Radzen UI, xUnit v3 + bUnit, csharpier).

## 2. Data model (Marten)

Event-sourced where history matters, documents where it doesn't.

**Documents**
- `Provider` (static config: Wise, Revolut, CryptoCom, DKB, EasyBank)
- `Account` — provider, type (checking/credit card/crypto/multi-currency balance),
  currency, owner (André/wife/joint), sync method, external ids
- `Category` — hierarchical (e.g. Living > Groceries), seed with a sensible default tree
- `CategoryRule` — matcher (merchant/counterparty/description pattern, amount range) → category; source: manual | learned-from-correction
- `Budget` — category, monthly limit, start/end month (a category can have several budget periods over time)
- `ImportBatch` — file/API sync run: source, hash, row count, result (audit + idempotency)
- `ProviderCredential` — provider, credential payload (DataProtection-encrypted), created/rotated timestamps, consent expiry
- `PlannedItem` — description, amount (± income/expense), category, schedule (one-time date | monthly/quarterly/yearly recurrence), expected account, matching hints (counterparty pattern, amount tolerance)

**Event-sourced aggregate: `Transaction`**
- `TransactionImported` (raw provider data, normalized fields, dedup hash)
- `TransactionCategorized` (category, source: provider | rule | ai | manual, confidence)
- `TransactionCategoryCorrected` (feeds rule learning)
- `TransactionLinkedAsTransfer` (Wise→DKB etc. — exclude from spending)
- `TransactionMatchedToPlannedItem` (auto or manual; unmatch possible)

**Projections**
- Flat `TransactionView` for the grid (Marten projection)
- `MonthlyCategorySpend`, `DailyBalance`/net-worth series, `RecurringSeries`
- `PlannedOccurrence` — expanded schedule per month with status: pending | matched | overdue | skipped

**Normalization** — one canonical transaction: bookingDate, valueDate, amount + currency,
amount in EUR (ECB rate at booking date), counterparty, description, external id,
dedup hash `(account, date, amount, normalized description)`.

## 3. Provider connectors

Common interface: `IProviderConnector` (API sync) and `IStatementParser` (file upload).
Every import goes through the same pipeline: parse → normalize → dedup → `TransactionImported` → categorization.

| Provider | V1 method | Detail |
|---|---|---|
| Wise | **API** | Personal API token + SCA signed requests (RSA key pair) for EU statement reads; balances + statements per currency |
| Revolut | **API via Enable Banking** | PSD2 AISP aggregator, free for personal use; consent renewal every 90/180 days |
| DKB | **API via Enable Banking** | Same integration; FinTS as fallback option if coverage disappoints |
| Crypto.com | **CSV upload** | App transaction export; plus daily price fetch (e.g. CoinGecko) for portfolio valuation |
| Easy Bank | **CSV upload** | Statement export; parser per format version |

- Scheduled sync via Quartz.NET (daily), manual "sync now" button, upload page with
  drag & drop, preview and per-row dedup status.
- Provider credentials (Wise token + private key, Enable Banking app key, Claude API key)
  stored in Postgres as a `ProviderCredential` document, encrypted with .NET DataProtection
  (`IDataProtector`, purpose string per provider). Entered and rotated via the Settings page —
  no secrets on disk. **Requirement:** the DataProtection key ring must be persisted
  (e.g. `PersistKeysToDbContext`/Marten-backed or a mounted volume) and backed up — a lost
  key ring makes all stored credentials unrecoverable. Only the DB connection string remains
  an infrastructure secret (key-per-file seam).
- Enable Banking consent flow needs a redirect endpoint in the app.

## 4. Categorization pipeline

```
provider category? ──→ map to own tree ──→ done (source: provider)
else: CategoryRule match? ──→ apply (source: rule)
else: batch to Claude API ──→ suggestion + confidence
        high confidence → auto-apply, flagged "AI" in UI
        low confidence  → review queue
manual correction ──→ TransactionCategoryCorrected ──→ offer new/updated rule
```

- Claude API (structured output): batches of ~50 transactions, prompt contains category
  tree + few-shot examples from confirmed history. Model: Haiku-class (cheap, sufficient).
- Review queue page: confirm/override in bulk.

## 5. UI (pages)

- **Dashboard** — net worth (total + sparkline), balances per account, current month
  spending by category, budget progress bars
- **Transactions** — RadzenDataGrid: filter by account/owner/category/date/text, inline
  category edit, transfer linking
- **Review queue** — uncategorized + low-confidence AI suggestions
- **Import** — file upload + sync status/history per account
- **Budgets** — set limits, monthly view
- **Recurring** — detected subscriptions/recurring payments (interval + amount tolerance heuristic); one-click "convert to planned item"
- **Planning** — planned costs & income (recurring fixed, one-time, irregular annual): monthly plan-vs-actual view, pending/overdue items, match/unmatch transactions manually where auto-match fails
- **Settings** — accounts, category tree, rules, provider credentials (enter/rotate keys, consent status)

## 6. Phases (Claude Code milestones)

**Phase 0 — Scaffold** (small)
Repo, solution, Aspire AppHost + Postgres, app-foundation wired, login working, empty dashboard. DataProtection key ring persisted (Marten/Postgres-backed). CI like app-foundation (locked-mode restore, vulnerability gate).

**Phase 1 — Core domain + CSV import** (the foundation)
Transaction aggregate, accounts, category tree, import pipeline with dedup,
CSV parsers for all 5 providers (fixtures from real exports — anonymized), transactions grid, manual categorization.

**Phase 2 — Categorization intelligence**
Rules engine, Claude API integration, review queue, rule learning from corrections.

**Phase 3 — API sync**
Wise connector (token + SCA signing), Enable Banking (consent flow, DKB + Revolut), Quartz scheduled sync, sync status UI.

**Phase 4 — Insights**
Currency normalization to EUR, dashboard charts, net worth history, budgets, recurring detection, crypto price valuation.

**Phase 5 — Planning (Excel replacement)**
Planned items (recurring/one-time/annual, income + expenses), occurrence expansion, auto-matching against imported transactions (counterparty pattern + amount tolerance + date window), plan-vs-actual view, dashboard tile for upcoming/overdue items. Recurring detection (Phase 4) feeds "convert to planned item".

Each phase = shippable; hand to Claude Code one phase at a time, feature branches per app-foundation git rules.

## 7. Risks / open points

- **Enable Banking coverage** — ✅ confirmed: DKB + Revolut available. Restricted mode (own linked accounts) requires no contract/KYB/AISP license — private use is the intended use case. Only whitelisted accounts are fetchable.
- **Transaction history window** — banks typically expose full history only ~1 hour after fresh authorisation, then ~90 days. Plan: on first consent, sync immediately and deep; backfill older history via CSV upload once.
- **Session-specific account IDs** — Enable Banking account IDs change per session/re-authorisation; match accounts via the stable `identification_hash` field.
- **Wise SCA signing** — needs key-pair setup and signature implementation; well-documented but fiddly.
- **CSV format drift** — providers change export formats; parsers must be versioned and fail loudly into the import UI, never silently drop rows.
- **PSD2 consent expiry** — Enable Banking consents expire (90–180 days); surface expiry prominently on dashboard.
- **DataProtection key ring** — credentials in DB are only as safe/durable as the key ring; persistence + backup is a Phase 0 task, not an afterthought.
- **Real export samples needed** — one recent export per provider to build parsers against (Phase 1 prerequisite).

## 8. Prerequisites from André before coding starts

1. Create empty `finance-app` repo — **blocks Phase 0**
2. One CSV/export sample per provider (may be anonymized) — **blocks Phase 1**
3. Wise: create personal API token + generate/register key pair for SCA — **blocks Phase 3**
4. Enable Banking: register production application, export private key, both of you link DKB + Revolut accounts (restricted-mode activation) — **blocks Phase 3**
5. Claude API key — **blocks Phase 2**
6. Rough draft of desired category tree, or approve the default seed — **blocks Phase 1**

All credentials (3–5) are entered via the Settings page once the app runs; nothing goes into config files.
