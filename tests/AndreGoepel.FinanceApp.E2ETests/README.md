# AndreGoepel.FinanceApp.E2ETests

End-to-end tests that drive the **real** finance app — the Blazor UI, the cookie-login
middleware, PostgreSQL, and the CSV import pipeline — through a Chromium browser.

## How it works

- **Aspire.Hosting.Testing** boots the `AppHost`
  (`src/AndreGoepel.FinanceApp.AppHost`) once per test run: PostgreSQL, MailHog, and the
  Blazor web app (Aspire resource `financeapp`). The AppHost is started with `E2E=true`, which
  drops Postgres' persistent volume and fixed host port so every run starts from an empty
  database on a dynamic port. The fixture waits for `financeapp` to become healthy, then reads
  its `https` endpoint and MailHog's `web` (HTTP) endpoint. The secret `database-password`
  parameter is supplied by the fixture.
- **Microsoft.Playwright** (Chromium) drives the browser. Each test gets a fresh
  `IBrowserContext` so cookies never leak between tests.
- **MailHog** captures every outgoing email. `MailHogClient` reads the inbox over MailHog's HTTP
  API so confirmation / reset links can be followed for real (used by the account-flow helpers).

The suite runs **serially** inside one xUnit collection because it shares a single app instance
and database. The first test that needs it provisions the root admin exactly once via the
`/Setup` flow (`E2EAppFixture.ProvisionAdminAsync`, idempotent).

The account pages under `/Account/*` and the `/Setup` page ship in the
**`AndreGoepel.Marten.Identity.Blazor`** NuGet package the app consumes; the finance feature
pages (`/`, `/transactions`, `/import`, `/settings/*`, …) live in `src/AndreGoepel.FinanceApp`.

## Prerequisites

1. **A container runtime** must be running — Docker **or** Podman. The tests start real
   containers; if none is reachable the fixture fails fast.
2. **.NET 10 SDK** (the version pinned in `global.json`).
3. **Playwright browsers** installed once, after a build:

   ```bash
   # from the repo root:
   pwsh tests/AndreGoepel.FinanceApp.E2ETests/bin/Debug/net10.0/playwright.ps1 install chromium
   ```

### Using Podman instead of Docker

Aspire's orchestrator auto-detects the runtime, but if Docker Desktop's `docker.exe` is on your
PATH (even with its daemon stopped) it may be picked first. Force Podman per-run:

```bash
dotnet test tests/AndreGoepel.FinanceApp.E2ETests --settings tests/AndreGoepel.FinanceApp.E2ETests/podman.runsettings
```

## Running

```bash
# from the repo root
dotnet test tests/AndreGoepel.FinanceApp.E2ETests
```

Watch the browser (debugging locally):

```bash
E2E_HEADED=true dotnet test tests/AndreGoepel.FinanceApp.E2ETests
```

The main `CI` workflow skips these (`--filter "FullyQualifiedName!~E2ETests"`); they run in the
dedicated `E2E` GitHub Actions workflow, which has Docker available.

## Coverage

| Area | Tests |
| --- | --- |
| Smoke | app boots, `/Setup` runs once, admin login → dashboard |
| Navigation | every finance page opens for an admin (route + heading); each page redirects an anonymous visitor to login |
| Import | create a CSV account → upload a DKB statement fixture → rows land on Transactions; re-import is idempotent (all duplicates, nothing new) |

## Tuning notes

The UI is built with **Radzen**, whose markup can shift between versions. Selectors are
centralized in `Infrastructure/PageExtensions.cs` and the flows in `E2ETestBase`, so if a
selector drifts you fix it in one place. The account-flow selectors target the current
`AndreGoepel.Marten.Identity.Blazor` package markup; the Accounts-create and Import selectors
target the Radzen form fields in `src/AndreGoepel.FinanceApp` — verify them on the first live
run after a package or UI change (`E2E_HEADED=true`). If a click seems to do nothing, check
`WaitForBlazorAsync` and the click-retry loops first — Blazor Server can drop a click landing in
the circuit-attach gap.
