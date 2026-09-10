# Persistence Spine — Plan Brief

> Full plan: `context/changes/persistence-spine/plan.md`

## What & Why

Roadmap item **F-02**. Stand up a provisioned Azure SQL database the deployed app can read and
write, with its connection string delivered outside infrastructure-as-code and a repeatable
migration path in place. It unblocks `S-01` (accounts) and every persisting slice after it, and
closes Open Roadmap Question 1, which has sat under all of them since the roadmap was written.

## Starting Point

A Blazor Server shell is live on B1 Linux in `polandcentral` with a working circuit, and
`infra/main.bicep` is the declared source of truth for its infrastructure. There is no database
anywhere in the subscription, no ORM package, no `DbContext`, and no connection string in either
settings file. `Microsoft.Sql` and `Microsoft.KeyVault` are not even registered as resource
providers, and `dotnet-ef` is not installed.

## Desired End State

`https://tenexcards-ka.azurewebsites.net/db-check` writes a row to an Azure SQL S0 database and
reads it back, and those rows survive a container restart. The connection string exists in no
tracked file and in no template — the app resolves it from Key Vault through its own managed
identity. Data Protection keys live in the same database, so auth cookies and antiforgery tokens
will no longer break on every restart once Identity arrives. A separate development database on the
same server carries local work, so nothing a developer runs can reach the schema the live site
serves from.

## Key Decisions Made

| Decision | Choice | Why |
| --- | --- | --- |
| Provider and tier | Azure SQL **S0** (provisioned, 10 DTU) for the app, plus a **second, cheaper database on the same server** for local development | Auto-pausing tiers are ruled out by the risk register; trial credit expires worthless, so buying out of a throttling ceiling costs nothing. The second database exists so a local `dotnet run` can never migrate the live one |
| Database auth | Connection string as a **Key Vault reference** | Follows the prescription already recorded in `infrastructure.md`; passwordless was considered and declined |
| Where the DB is declared | **`infra/main.bicep`**, plus a what-if characterisation | Keeps the declared source of truth true, and learns what `Microsoft.Sql` what-if looks like now, on an empty database rather than later on one holding real accounts |
| Migrations | **`Database.Migrate()` at startup** | Deployed code and schema can never drift; accepted cost is that a bad migration blocks boot |
| Proof of "reads and writes" | Throwaway **probe entity + `/db-check`** page, deleted by `S-01` | Mirrors `CircuitCheck.razor`; a health check would prove connectivity but not write |
| Local development | Points at the **development database** on the same Azure server, via user-secrets, authenticating as a **contained database user** scoped to that database — never at the app's, never as the server admin | Zero dialect divergence; Docker is not installed and SQLite would produce migrations that do not apply. Same server keeps the dialect identical while `Database.Migrate()` on the boot path can no longer mutate live schema from a dev machine, and the Data Protection key ring (one table, no per-application partition) stays off that machine. The contained user is what makes this a boundary rather than a naming convention: it has no server-level login and cannot address another database on the server, so the admin password never leaves the vault |
| Data Protection keys | **Persisted now**, not in `S-01` | `UseAntiforgery()` already depends on the key ring today, and this is the change that creates the key store |
| Test project | **Deferred to `S-01`**; `AGENTS.md` corrected here | No deterministic rule exists to test yet, which is `AGENTS.md`'s own stated bar |
| If a SKU is refused, or time runs out | **Stop and ask** | No architecture or scope decision gets made unilaterally inside a failure |

## Scope

**In scope:** SQL server + S0 app database + a development database + Key Vault + managed identity +
role assignment in Bicep · two vault secrets and one app setting · a contained
development-database user · EF Core with startup migration, pinned through a
`.config/dotnet-tools.json` manifest · Data Protection key persistence · a disposable, statically
rendered probe form at `/db-check` · deployment and live verification · correcting the claims this
change falsifies across four repository records.

**Out of scope:** Identity and accounts (`S-01`) · card entity and domain schema (`S-02`) · test
project (`S-01`) · CI and automated packaging (`F-03`) · private endpoints · down migrations ·
health endpoint · the LLM key · scaling past one worker.

## Architecture / Approach

The whole plan is ordered around one constraint: **no step debugs two variables at once.** Wiring
the Key Vault reference from the start and migrating at startup compound badly — a silently
unresolved reference hands the app the literal string `@Microsoft.KeyVault(...)`, `Migrate()` throws
during boot, and the app does not start, on a tier with no slot rollback.

So Phase 1 provisions everything and proves the reference resolves *while the deployed app still
contains no EF Core code at all* — that check needs no application involvement. Phase 2 writes the
code and verifies it against the **development** database. Phase 3 deploys, by which point the only
new variables are the archive and the platform, both already characterised by F-01.

Verifying against the development database has a second payoff: the app's database is still empty
when Phase 3 lands, so the first deploy applies `InitialSpine` on the boot path *for real* rather
than as a no-op. That is the only place this plan's most-guarded risk gets exercised, and it is
exercised while the database holds nothing.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Provision the infrastructure | Two databases, vault, identity, and a Key Vault reference proven to resolve | Deploying the template re-applies the live App Service; a real deletion can hide among what-if phantoms, as one did on 2026-08-31 |
| 2. EF Core spine | `DbContext`, probe entity, DP keys, first migration, `/db-check` — verified against the development database | Startup `Migrate()` is on the boot path, so a bad migration means no app |
| 3. Deploy and verify | Boot-path migration proven on the empty app database, live `/db-check` round-trip, key-ring survival across a restart, plus a measured DB round-trip | The hand-built archive: a wrong one deploys *successfully*, then every asset 404s |
| 4. Update the record | `AGENTS.md`, `deploy-plan.md`, `infrastructure.md`, `roadmap.md` made true again | Silent failure — nothing breaks, the records just stop matching reality |

**Prerequisites:** `az` authenticated (confirmed) · `Microsoft.Sql` and `Microsoft.KeyVault`
registered · `dotnet-ef` restored from a `.config/dotnet-tools.json` manifest, at a 10.0.x version
re-confirmed at Phase 2 rather than the 10.0.12 read during planning · a SQL client capable of
connecting to Azure SQL, **confirmed to exist before Phase 1 ends** — the Azure Portal Query Editor
is the no-install default, since `sqlcmd` ships with neither the .NET SDK nor Windows.
**Estimated effort:** ~3-4 sessions across four phases; Phase 1 is the heaviest and has nothing to
show in the app.

## Open Risks & Assumptions

- **Azure SQL availability in `polandcentral` is unproven** and cannot be read until the provider is
  registered. F-01 hit `RequestDisallowedByAzure` and a zero-quota block on this Free Trial. A
  refusal stops the change rather than triggering a silent region or provider switch.
- **Registering the two providers is a subscription-level mutation** that `az group delete` does not
  undo — the same class of change `Microsoft.Web` made silently during F-01.
- **Key Vault soft-delete reserves the vault name after deletion**, so a teardown-and-recreate needs
  `az keyvault purge` first. Purge protection is deliberately left off for this reason.
- **RBAC propagation is eventually consistent**, so the first `az keyvault secret set` after granting
  yourself access is expected to fail before it starts working.
- **The archive is still hand-built.** The pack-and-verify script was accepted as risk in F-01 and
  handed to `F-03`, which has not happened yet.
- **Data Protection key persistence is reasoned, not yet measured** — the consequence for Identity
  cookies is inferred from ASP.NET Core defaults, since Identity does not exist.
- **The "allow Azure services" firewall rule is not a network boundary.** It is a
  `0.0.0.0`–`0.0.0.0` entry admitting traffic from any Azure subscription in any tenant, so for
  Azure-originating traffic the SQL admin password is the only barrier. Pinning to the app's
  outbound IPs was declined — App Service outbound IPs are scale-unit-shared, so it narrows little
  while breaking the app whenever the IP set rotates. Real isolation needs a private endpoint.
- **`/db-check` is an anonymous, unbounded write endpoint** for as long as it exists, since Identity
  arrives in `S-01`. Accepted as risk: the probe table is short-lived and `S-01` drops it with the
  page and its nav entry.
- **Secrets must never be passed as CLI argument values.** PowerShell 5.1's PSReadLine keeps every
  interactive command line in plaintext indefinitely, where `git grep` will never find it.

## Success Criteria (Summary)

- The deployed app writes a row and reads it back at `/db-check`, and the rows survive
  `az webapp restart`.
- The first deploy applies `InitialSpine` on the boot path for real — proven from the startup log,
  not inferred from the absence of an exception.
- A `/db-check` form rendered *before* a restart is still accepted when submitted *after* it, which
  is the check that fails if Data Protection key persistence regresses. It is why `/db-check` is
  statically rendered: an interactive button mints no antiforgery token, so the check would have no
  subject. `S-01` inherits this check, not the weaker "no new key row" one.
- The connection string appears in no tracked file, no template, and no shell history, and the Key
  Vault reference reports `Resolved`.
- A database round-trip is measured against the 2-second acknowledgement budget and recorded, so
  `S-02` inherits a number rather than an assumption.
