# Persistence Spine Implementation Plan

## Overview

Provision an Azure SQL S0 database co-located with the app in `polandcentral`, deliver its
connection string to the deployed app through a Key Vault reference, and prove the deployed app
both reads and writes it. Data Protection keys are persisted to the same database in this change
rather than in `S-01`. No domain schema is designed: Identity tables arrive with `S-01`, the card
entity with `S-02`.

This is roadmap item **F-02** (`persistence-spine`), whose stated outcome is "the deployed app
reads and writes a provisioned database, with the connection string set outside
infrastructure-as-code and a repeatable migration path in place."

## Current State Analysis

**What exists.** A Blazor Web App with per-page interactivity on `net10.0`, live at
`https://tenexcards-ka.azurewebsites.net` in resource group `rg-tenexcards-plc`, region
`polandcentral`, on a B1 Linux App Service plan. `infra/main.bicep` declares the plan, the site
(`httpsOnly: true`, `clientAffinityEnabled: true`), the `web` siteConfig child and the `logs`
child. `TenExCards/Program.cs` has no `UseHttpsRedirection()`, keeps `UseHsts()` in the
non-Development branch, and calls `UseAntiforgery()`.

**What is missing.** Everything persistence: no ORM package, no `DbContext`, no entity, no
migration, no connection string in either settings file, and no database resource anywhere in the
subscription. `dotnet tool list --global` is empty, so `dotnet-ef` is not installed.

**Constraints discovered during planning, verified rather than assumed:**

- `Microsoft.Sql`, `Microsoft.DBforPostgreSQL` and `Microsoft.KeyVault` are all **`NotRegistered`**
  on this subscription. Registration is a subscription-level mutation that `az group delete` does
  not undo — `deploy-plan.md` records exactly this happening silently for `Microsoft.Web` during
  F-01's first `az appservice plan create`.
- Azure SQL availability in `polandcentral` **cannot currently be read**:
  `az sql db list-editions -l polandcentral` returns `SubscriptionNotFound`, which is what ARM
  returns when the resource provider does not know the subscription. It becomes readable only
  after registration.
- `az postgres flexible-server list-skus -l polandcentral` does list Burstable `Standard_B1ms`.
  This was a data point for the provider choice, not a reason to take it — and F-01's recorded
  lesson stands regardless: a listing reports where a SKU *exists*, not where you may deploy it.
- `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Design`,
  `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` and the `dotnet-ef` tool are all at
  **10.0.12** at planning time — re-confirm before pinning, since the only patch level evidenced in
  this repository or on this machine is 10.0.11 (SDK 10.0.400; the deployed container reports
  ASP.NET Core 10.0.11). A package version ahead of the shared framework is not a conflict either
  way, because all four ship as NuGet packages rather than in `Microsoft.AspNetCore.App`.
- Docker is not installed (`docker: command not found`), which removed a local-container database
  from consideration.

## Desired End State

The deployed app at `https://tenexcards-ka.azurewebsites.net/db-check` writes a row to an Azure
SQL S0 database and reads it back, across a container restart. Its connection string is never
stored in the repository, never declared in `infra/main.bicep`, and reaches the app as a Key Vault
reference resolved through the site's system-assigned managed identity. Data Protection keys live
in that same database, so the key ring survives a restart. `infra/main.bicep` declares the SQL
server, the database, the vault, the identity and the role assignment, and its comments record
what `Microsoft.Sql` and `Microsoft.KeyVault` what-if output looks like on this template.

Verified by: the KV reference reporting `Resolved`; `/db-check` incrementing on the live host;
a Data Protection key row surviving `az webapp restart`; and the four repository records
(`AGENTS.md`, `deploy-plan.md`, `infrastructure.md`, `roadmap.md`) agreeing about what exists.

### Key Discoveries:

- **`infra/main.bicep` must never declare `appSettings`** (the comment block above the `logs`
  resource). Declaring them makes the template authoritative, so a routine successful deployment
  deletes the connection string. The same principle extends cleanly to this change: the *vault* is
  infrastructure and goes in the template; the *secret values* are data-plane and are set with
  `az keyvault secret set`.
- **A deployment of this template reporting `Succeeded` has already destroyed a property once.**
  On 2026-08-31 it cleared `properties.freeOfferExpirationTime` on the plan; `what-if` predicted
  it correctly as `- Delete` and it was waved through because two genuine phantoms sat in the same
  output. Nothing in the output separates the two.
- **F-01's foundation pattern**: `Components/Pages/CircuitCheck.razor` is a throwaway proof-of-life
  surface with a scheduled death (`S-01` deletes it). `/db-check` is the same pattern for
  persistence, and inherits the same deletion trigger.
- **The archive trap is still live.** Review finding F4 on `blazor-server-shell` recorded that no
  pack-and-verify script was committed; it was accepted as risk and handed to `F-03`, which has not
  happened. This change deploys again and re-runs the three shape assertions by hand.
- **`UseAntiforgery()` is already in the pipeline** (`TenExCards/Program.cs`), so the ephemeral
  Data Protection key ring already protects something today. That is why key persistence is pulled
  forward into this change rather than left to `S-01`.
- **The 2s acknowledgement budget's current floor is ~0.152 s TTFB** on a warm instance
  (`context/changes/blazor-server-shell/baseline.md`). Database round-trips are the first real
  addition to it and are measured in Phase 3.

## What We're NOT Doing

- **No Identity, no accounts, no login** — `S-01`. This change creates no user table.
- **No card entity and no domain schema** — `S-02`. The only tables created are a throwaway probe
  table and the Data Protection key table.
- **No test project.** `TenExCards.Tests` is deferred to `S-01` by explicit decision: there is no
  deterministic rule to test here, and `AGENTS.md`'s own instruction is to test the deterministic
  rules. Phase 4 corrects the `AGENTS.md` wording that currently points at this change.
- **No passwordless (Entra ID) database authentication.** Considered and declined in favour of the
  Key Vault reference already prescribed in `infrastructure.md`. Recorded so the decision is not
  re-litigated later as an oversight.
- **No CI, no automated packaging, no deployment pipeline** — `F-03`. The archive is hand-built
  again under the three assertions.
- **No private endpoint, VNet integration, or `publicNetworkAccess: Disabled`.** The database is
  reachable over public networking. Be precise about what the firewall rules do and do not buy: the
  "allow Azure services" rule is a `0.0.0.0`–`0.0.0.0` entry, which admits traffic originating
  anywhere in Azure — any subscription, any tenant — not just this one. For Azure-originating
  traffic there is therefore **no network boundary**; the boundary is the SQL admin password. Pinning
  to the app's `possibleOutboundIpAddresses` was considered and declined: App Service outbound IPs
  are shared across a scale unit, so it narrows "all of Azure" only to "every app on this scale
  unit", while adding a rule set that silently breaks the app when the IP set rotates on a tier or
  scale operation. Real isolation needs a private endpoint, which is out of scope here.
- **No down migrations.** Migrations are forward-only, per `infrastructure.md`'s
  `## Operational Story`.
- **No health-check endpoint.** Considered as the proof surface and rejected: it proves
  connectivity, not write.
- **No LLM API key in the vault** — `S-02`. The vault exists for it, but this change puts nothing
  there but database secrets.
- **No second worker, no shared-storage Data Protection keys.** Keys go to the database; scaling
  past one worker stays parked.
- **No change to `Program.cs`'s HTTPS posture.** `UseHsts()` stays, `UseHttpsRedirection()` stays
  absent, and neither `ASPNETCORE_FORWARDEDHEADERS_ENABLED` nor `ASPNETCORE_HTTPS_PORT` is added.

## Implementation Approach

The change is ordered so that **no step debugs two variables at once**. That constraint comes from
two choices made during planning that compound if taken naively: the Key Vault reference is wired
from the start, and migrations are applied by `Database.Migrate()` at startup. If the reference
silently fails to resolve, the app receives the literal string `@Microsoft.KeyVault(...)` as its
connection string, `Migrate()` throws during boot, and the app does not start at all — on a tier
with no slot rollback.

The separation is temporal. **Phase 1 provisions everything and proves the reference resolves while
the deployed app still contains no EF Core code at all.** The resolution check
(`configreferences/appsettings`) needs no application involvement. Only once that reports
`Resolved` does Phase 2 write code that depends on it, and Phase 3 deploy that code.

Phase 2 is verified entirely against the Azure database from the development machine, so by the
time Phase 3 deploys, the only new variables are the archive and the platform — the same variables
F-01 already characterised.

Phase 4 updates the repository's own records, following F-01's Phase 3 precedent. It is a separate
phase because its failure mode is different: nothing breaks, the records just quietly stop being
true.

## Critical Implementation Details

**Ordering — prove the reference before anything depends on it.** The `configreferences/appsettings`
check in Phase 1 is the gate for the whole change. Do not begin Phase 2 until it reports `Resolved`.

**A malformed Key Vault reference fails silently.** App Service stores an unparseable reference as
its literal string value; nothing logs a failure. A versionless reference — which is what
auto-picks-up a rotated secret — requires a trailing slash after the secret name, and omitting it
changes the meaning rather than erroring:

```
@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/sql-connection-string/)
```

In Windows PowerShell 5.1 a leading `@` with parentheses is array-subexpression syntax, so this
value must be quoted when passed to `az webapp config appsettings set`.

**RBAC role assignments are eventually consistent.** After granting yourself
`Key Vault Secrets Officer`, expect `az keyvault secret set` to return `Forbidden` for a minute or
two before it starts working. Creating a vault does **not** grant its creator data-plane access
under the RBAC permission model. Set `enableRbacAuthorization: true` explicitly in the template so
the permission model is a declared fact rather than a default that has changed over time.

**Key Vault soft-delete makes the name unavailable after deletion.** Do **not** enable purge
protection — with it, the vault cannot be removed for the full retention window, which would break
the single-command teardown recorded in `deploy-plan.md`. Set retention to its 7-day minimum, and
know that recreating a deleted vault under the same name requires `az keyvault purge` first.

**The role assignment's resource name must be a deterministic GUID** derived from scope, principal
and role definition (`guid(...)` in Bicep). A non-deterministic name makes every redeployment
attempt to create a duplicate assignment and fail.

**The SQL admin password is a `@secure()` template parameter, not template content.** It must not be
written to a `.parameters.json` file, and every future deployment of this template must supply it
again — retrievable from the vault. This is the one place where a template deployment needs a secret
as input, and it is why the password is stored in the vault as its own secret in addition to being
embedded in the connection string.

**Local development gets its own database, not the live one.** `Database.Migrate()` on the boot path
means a local `dotnet run` applies whatever migrations sit in the working tree to whatever database
the connection string points at — forward-only, with no down migration and a rollback path (the
deployment archive) that does not reverse schema. Pointing local development at the live database
would let a half-written migration mutate live schema from a development machine, and would put the
production Data Protection key ring on that machine, since the EF key repository stores every key
row in one table with no per-application partition. Two databases on the same server keep dialect
divergence at zero while removing both. `infrastructure.md` separately places any operation against
the database server in the human-only approval bucket; a local `dotnet run` against the live
database would route around that.

**Two databases alone are a default, not a boundary.** They differ by `Initial Catalog`, so if local
development authenticates as the server admin, one edited token in user-secrets reaches the live
database with full rights — and the development machine then holds the credential for the drop,
scale and restore operations that same human-only bucket covers. The boundary is the *contained*
database user created in Phase 1 change 8: no server-level login, authenticating against the
development database named in its own connection string, unable to address another database on that
server. The admin password stays in the vault.

**A failed startup migration means no app.** `Database.Migrate()` runs on the boot path, and B1 has
no deployment slots. The rollback path is redeploying the retained previous archive, which is why
Phase 3 preserves one before publishing.

---

## Phase 1: Provision the persistence infrastructure

### Overview

Register the resource providers, declare the SQL server, database, vault, managed identity and role
assignment in `infra/main.bicep`, deploy under the snapshot → what-if → deploy → diff ritual, write
the secrets, and prove the Key Vault reference resolves. No application code is touched and nothing
is deployed to the app in this phase.

### Changes Required:

#### 1. Resource provider registration

**File**: n/a — subscription-level operation

**Intent**: Register `Microsoft.Sql` and `Microsoft.KeyVault`, without which neither resource can be
created and Azure SQL availability cannot even be queried. Record it as a subscription-level
mutation, because `az group delete` does not undo it.

**Contract**: `az provider register --namespace Microsoft.Sql` and the same for
`Microsoft.KeyVault`; both must report `Registered` before the template deployment is attempted.
Registration is asynchronous — poll rather than assume.

#### 2. Region and SKU proof

**File**: n/a — read-only checks against the subscription

**Intent**: Establish, before editing the template, that `polandcentral` will actually take an S0
database on this Free Trial subscription. F-01 lost an evening to a region that was listed and then
refused at create time.

**Contract**: `az sql db list-editions -l polandcentral --edition Standard` must list the `S0`
service objective. Per the decision recorded for this change, **a refusal stops the change and comes
back to the user with the verbatim error text** — do not silently switch region, tier or provider.

**Also confirm a SQL client exists before Phase 1 ends.** Criteria 1.12, 1.15, 2.5 and 3.8 all
depend on one, and nothing in this plan has verified it. Every other tool state was checked and
recorded during planning — `docker: command not found`, `dotnet tool list --global` empty, three
providers `NotRegistered` — which is what makes this omission conspicuous rather than harmless:
`sqlcmd` ships with neither the .NET SDK nor Windows. The Azure Portal Query Editor needs no
install and reaches the server through the same "allow Azure services" rule the template declares,
so it is the default; `winget install sqlcmd` is the alternative if a local client is wanted.

#### 3. Infrastructure template

**File**: `infra/main.bicep`

**Intent**: Declare the SQL logical server, the S0 database, the Key Vault, the web app's
system-assigned identity, and the role assignment that lets that identity read secrets — so the
declared source of truth stays true. Secret *values* stay out, matching the reasoning already
recorded for `appSettings`.

**Contract**: Adds `Microsoft.Sql/servers` and **two** `Microsoft.Sql/servers/databases` — the app's
database at `sku.name: 'S0'`, `sku.tier: 'Standard'`, and a second, separately named database for
local development at the cheapest tier that shares the dialect (Basic is sufficient; it is never on
a user path, so its throughput does not matter). Also `Microsoft.KeyVault/vaults`
(`enableRbacAuthorization: true`, `enablePurgeProtection` unset, `softDeleteRetentionInDays: 7`),
`Microsoft.Sql/servers/firewallRules` for Azure services, and a
`Microsoft.Authorization/roleAssignments` granting `Key Vault Secrets User` to the site's principal.
Modifies the existing `site` resource to add `identity: { type: 'SystemAssigned' }`. Takes a new
`@secure()` parameter for the SQL admin password with no default. `appSettings` stays undeclared.

Verify the API version for each new resource type with
`az provider show --namespace Microsoft.Sql --query "resourceTypes[?resourceType=='servers'].apiVersions"`
after registration rather than copying one from memory — this template already pins verified
versions elsewhere and its opening comment sets that expectation.

#### 4. Snapshot, what-if, deploy, diff

**File**: n/a — deployment operation, output recorded in Phase 4

**Intent**: Apply the template without repeating the 2026-08-31 incident where a real deletion was
waved through because phantoms sat beside it in the same output.

**Contract**: Snapshot `az appservice plan show`, `az webapp config show` and
`az webapp config appsettings list` to files before deploying. Run `az deployment group what-if` and
read every line. Deploy with `az deployment group create` (Incremental — never `--mode Complete`).
Re-snapshot and diff. Every `- Delete` or `~ Modify` on an existing resource must be reconciled
against the post-deploy diff and the result recorded.

#### 5. What-if characterisation

**File**: `infra/main.bicep` (comments)

**Intent**: Record what `Microsoft.Sql/*`, `Microsoft.KeyVault/*` and
`Microsoft.Authorization/roleAssignments` what-if output looks like on this template, so the next
person can tell a phantom from a real deletion. The existing `Microsoft.Web` characterisation above
the `site` resource is the model and the reason this is worth the effort.

**Contract**: A comment block per new resource type naming which predicted lines proved to be noise
and which were real, dated, and stating that the characterisation was taken against an **empty**
database.

#### 6. Secrets and the app setting

**File**: n/a — data-plane operations against the vault and the app

**Intent**: Put the credential where the app can reach it without it existing in the repository or
the template, and point the app at it.

**Contract**: Two secrets in the vault — `sql-admin-password` (raw, for future template
deployments) and `sql-connection-string` (the full connection string the app consumes). Grant your
own principal `Key Vault Secrets Officer` first and expect RBAC propagation delay. Then one app
setting, `ConnectionStrings__DefaultConnection`, set via `az webapp config appsettings set` to the
versionless Key Vault reference.

**Then `az webapp restart` and read the reference again.** The RBAC delay above is not only the
human's problem: the `Key Vault Secrets User` assignment granted to the *site's* identity in change 3
propagates on the same eventual-consistency schedule, and App Service resolves Key Vault references
at app start and caches the outcome. A reference set before that assignment has propagated reports
unresolved and does not re-heal on the timescale this check is run at. Treat the first
non-`Resolved` reading as expected: restart, wait, read it again, and escalate to the template only
after that. Criterion 1.6 is the gate for the whole change, so a benign false negative costs more
here than anywhere else in the plan.

**Never pass a secret as an argument value.** Use `az keyvault secret set --file <path>` rather than
`--value` (the CLI warns about `--value` for this reason), supply the `@secure()` template parameter
by prompt or from a parameters file kept outside the repository, and delete the scratch files
afterwards. Under Windows PowerShell 5.1, PSReadLine appends every interactive command line verbatim
to `%APPDATA%\Microsoft\Windows\PowerShell\PSReadLine\ConsoleHost_history.txt` and keeps it
indefinitely, so a secret typed as an argument outlives the session in plaintext where `git grep`
will never find it. The app setting itself is safe to pass inline — a Key Vault *reference* is a
pointer, not a secret.

The double underscore is the configuration hierarchy separator, so the app reads it as
`ConnectionStrings:DefaultConnection` through `GetConnectionString("DefaultConnection")`. Use an
**app setting**, not App Service's separate "Connection strings" section — the latter applies a
`SQLAZURECONNSTR_` prefix and type mapping that adds nothing here and one more thing to get wrong.

#### 7. Development machine access

**File**: n/a — firewall rule

**Intent**: Let the development machine reach the database, since local development runs against the
Azure database by decision.

**Contract**: A named `Microsoft.Sql/servers/firewallRules` entry for the current public IP, set via
CLI rather than in the template — it is a property of where you happen to be sitting, not of the
infrastructure. Record in Phase 4 that it must be re-added when the home IP changes.

#### 8. Development database login

**File**: n/a — T-SQL against the development database

**Intent**: Make the two-database split an actual boundary rather than a naming convention. Without
it the development connection string differs from the live one by `Initial Catalog` alone and
carries server-wide administrative rights, so a one-token edit in user-secrets reaches the database
the live site serves from — and the development machine holds the credential for exactly the
operations `infrastructure.md` places in its human-only approval bucket (drop, scale, restore).

**Contract**: Connect to the **development** database as the server admin and create a *contained*
database user there — one with its own password and **no server-level login** — then grant it
`db_owner` within that database only. A contained user authenticates against the database named in
its own connection string and cannot address another database on the same server; that is the
property that makes the split hold. Local development uses this credential (Phase 2 change 4). The
admin password stays in the vault and never reaches the development machine.

This is data-plane T-SQL, so it lives in no template and must be recreated after any teardown —
record that in Phase 4 alongside the firewall rule. Confirm `db_owner` is sufficient for EF Core to
create the schema before relying on it; if a migration is refused for want of a permission, widen
the grant *inside the development database* rather than falling back to the admin login.

### Success Criteria:

#### Automated Verification:

- Both providers report `Registered`: `az provider show --namespace Microsoft.Sql --query registrationState -o tsv`
- `az sql db list-editions -l polandcentral --edition Standard` lists the `S0` service objective
- Both databases are online: `az sql db show` returns `S0`/`Online` for the app database and `Online` for the development database
- The site has a principal: `az webapp identity show -g rg-tenexcards-plc -n tenexcards-ka --query principalId -o tsv` returns a GUID
- Both secrets exist: `az keyvault secret show` succeeds for `sql-connection-string` and `sql-admin-password`
- The Key Vault reference resolves: `az resource show --resource-type Microsoft.Web/sites/config --name tenexcards-ka/configreferences/appsettings -g rg-tenexcards-plc` reports status `Resolved`
- `az deployment group create` reported `Succeeded` in Incremental mode
- Post-deploy diff of the plan and web app snapshots shows no unintended property change
- `git grep` finds no password or connection string in any tracked file
- The PowerShell history file (`$env:APPDATA\Microsoft\Windows\PowerShell\PSReadLine\ConsoleHost_history.txt`) contains neither the admin password nor the connection string, and no scratch secret file remains on disk
- `infra/main.bicep` contains the dated what-if characterisation for all three new resource types

#### Manual Verification:

- A SQL client connects using the connection string read from the vault and runs `SELECT 1` successfully
- Every `- Delete` and `~ Modify` line in the what-if output was read and reconciled against the post-deploy diff, and the reconciliation is written down
- The vault's permission model is RBAC and purge protection is off
- The contained development-database user connects to the development database and is **refused** by the app database — the check that the split is a boundary rather than a naming convention

**Implementation Note**: Do not start Phase 2 until the Key Vault reference reports `Resolved`. That
check is the gate this whole plan is ordered around. Pause here for manual confirmation.

---

## Phase 2: EF Core spine, probe entity, and Data Protection keys

### Overview

Add EF Core and the DataProtection EF provider, define the `DbContext` with a throwaway probe entity
and the Data Protection key set, create the first migration, apply migrations at startup, and add
the `/db-check` page. Verified locally against the Azure database before anything is deployed.

### Changes Required:

#### 1. Package references

**File**: `TenExCards/TenExCards.csproj`

**Intent**: Add EF Core with the SQL Server provider, the design-time package `dotnet-ef` needs, and
the DataProtection EF provider.

**Contract**: `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Design`
(`PrivateAssets="all"`), and `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`, plus a
`UserSecretsId` property.

**Re-confirm the version before pinning it.** `10.0.12` was read during planning; the only patch
level evidenced in this repository or on this machine is `10.0.11` (SDK 10.0.400, and the deployed
container). Take whatever `dotnet package search` reports as the current 10.0.x and pin all three to
one version rather than carrying a planning-time number forward. The reasoning that a package
version ahead of the shared framework is safe still holds — all four ship as NuGet packages, not in
`Microsoft.AspNetCore.App`.

`dotnet-ef` goes in a **`.config/dotnet-tools.json` local manifest**, not a global install, so the
version is a repository fact restorable with `dotnet tool restore` — which `F-03` will need when CI
builds migrations. It is not currently installed either way.

#### 2. Database context

**File**: `TenExCards/Data/AppDbContext.cs` (new)

**Intent**: Provide the single context every later slice extends, carrying only what this change
needs: the disposable probe entity and the Data Protection key set.

**Contract**: `AppDbContext : DbContext, IDataProtectionKeyContext`, exposing
`DbSet<DataProtectionKey> DataProtectionKeys` (the interface's required member) and a
`DbSet<SpineProbe>`. `IDataProtectionKeyContext` is what lets `PersistKeysToDbContext` target this
context, and is the reason the interface is implemented here rather than on a separate context.

#### 3. Probe entity

**File**: `TenExCards/Data/SpineProbe.cs` (new)

**Intent**: Give the spine something to write and read that is unmistakably disposable, so "reads
and writes" is demonstrable without designing domain schema.

**Contract**: An entity with an identity key and a UTC timestamp of when the row was written.
Deleted by `S-01` along with its table, in the same way `CircuitCheck.razor` is.

#### 4. Local configuration

**File**: user-secrets store (outside the repository)

**Intent**: Give local development a connection string of the same shape as the deployed one,
without it ever entering a tracked file, pointed at the **development** database rather than the one
the live site serves from, and authenticating as the contained user from Phase 1 change 8 rather
than as the server admin.

**Ordered ahead of the code that needs it.** `dotnet ef migrations add` builds the application host
to obtain the `DbContext`, and criteria 2.4 and 2.5 apply and then inspect that migration — all of
which read `GetConnectionString("DefaultConnection")`. Setting user-secrets last, as an afterthought
to the code, is how this phase stalls on itself.

**Contract**: `ConnectionStrings:DefaultConnection` set via `dotnet user-secrets`, with the
development database as its `Initial Catalog` and the **contained development user** as its
credential — never the app's database, never the server admin. Same server, so the SQL dialect and
the provider are identical and migrations that apply here apply there. Neither `appsettings.json`
nor `appsettings.Development.json` gains a connection string. Local runs use a launch profile, which
sets `ASPNETCORE_ENVIRONMENT=Development`; running with `--no-launch-profile` defaults to Production
and is not the intended local path.

#### 5. Service registration and startup migration

**File**: `TenExCards/Program.cs`

**Intent**: Register the context against the connection string, persist Data Protection keys to it,
and apply pending migrations at startup.

**Contract**: Register **both** shapes of the context, using the SQL Server provider with
`EnableRetryOnFailure()` — Azure SQL produces transient faults and the 2s acknowledgement budget has
no room for a retry the application does not make:

- `AddDbContextFactory<AppDbContext>` — what the verification page uses per operation (change 7).
- A **scoped `AppDbContext`** as well, e.g.
  `AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext())`.

Both are required, and this is not a stylistic choice. `AddDbContextFactory<AppDbContext>` does not
register `AppDbContext` itself, and `PersistKeysToDbContext<AppDbContext>` resolves the context from
a service scope with `GetRequiredService` — not from the factory. A factory-only registration
compiles, starts, and then throws the first time anything protects data, which with
`UseAntiforgery()` in the pipeline is the first rendered form.

Then `AddDataProtection().PersistKeysToDbContext<AppDbContext>()`, and a scoped
`Database.Migrate()` call before `app.Run()`.

The migration call is on the boot path: if it throws, the container does not serve. Log the outcome
explicitly rather than relying on an unhandled exception to explain it, and keep the call after the
logging pipeline is available.

#### 6. Initial migration

**File**: `TenExCards/Migrations/**` (new)

**Intent**: Create the two tables — the probe table and the Data Protection key table — as one
forward-only migration.

**Contract**: A single migration named `InitialSpine` covering `SpineProbes` and
`DataProtectionKeys`. No down migration is authored or relied on; the rollback path for this change
is the retained deployment archive, not a schema reversal.

#### 7. Verification page

**File**: `TenExCards/Components/Pages/DbCheck.razor` (new)

**Intent**: Prove read *and* write from the deployed app, which a connectivity check cannot — and
mint an antiforgery token, which is what Phase 3's restart-survival check needs a subject for. This
is the persistence counterpart to `CircuitCheck.razor` and carries the same scheduled deletion.

**Contract**: Route `/db-check`, **statically rendered — no `@rendermode` line** — with an
`<EditForm method="post" @formname="write-probe">` whose submit writes a `SpineProbe` row, plus a
display of the current row count and the most recent timestamp. Obtain a context per operation from
`IDbContextFactory<AppDbContext>` rather than holding one open across the request.

**Static rendering is the point here, not an omission.** A Blazor SSR form emits an antiforgery
token automatically; an interactive-server button dispatches over the circuit, posts no form and
mints no token — which would leave criterion 3.11, the only check that regresses if key persistence
breaks, with nothing to run against. It would also let criterion 2.10 pass incidentally, since
interactive component descriptors are signed with the same key ring whether or not an antiforgery
token was ever issued.

Nothing is lost by rendering this page statically: circuit liveness is already proven by
`CircuitCheck.razor`, which lives until `S-01` deletes both. That is why criterion 3.9 no longer
asks for the count to increment without a page reload.

#### 8. Navigation

**File**: `TenExCards/Components/Layout/NavMenu.razor`

**Intent**: Make the page reachable, matching how `/circuit-check` is exposed.

**Contract**: One nav entry for `/db-check`, removed by `S-01` with the page. Note that
`/circuit-check` is reachable from *two* places — the nav menu and an in-body link on
`Components/Pages/Home.razor` — so `S-01`'s deletion list is longer than one nav entry each. Give
`/db-check` a single entry point, so it does not inherit the same scatter.

### Success Criteria:

#### Automated Verification:

- Release build is clean: `dotnet build TenExCards/TenExCards.csproj -c Release` reports 0 warnings and 0 errors
- `.config/dotnet-tools.json` exists and `dotnet tool restore` succeeds
- `dotnet ef migrations list --project TenExCards/TenExCards.csproj` lists `InitialSpine`
- Running the app locally applies the migration to the **development** database, whose `__EFMigrationsHistory` then contains `InitialSpine`
- Both tables exist in the development database after the local run, and the app database is still empty
- `git grep` over tracked files finds no connection string, password, or vault secret value
- `appsettings.json` and `appsettings.Development.json` contain no `ConnectionStrings` section

#### Manual Verification:

- `/db-check` running locally writes a row and the displayed count increments
- Restarting the local app shows the previously written rows — the write was durable, not in-memory
- The `DataProtectionKeys` table contains at least one row after the `/db-check` form has been rendered and submitted, which is what issues an antiforgery token
- The startup warning about Data Protection keys not being persisted no longer appears in the local log

**Implementation Note**: Everything here is verified against the *development* database from the
development machine, which leaves the app database empty so Phase 3 exercises the boot-path
migration for real. The only new variables in Phase 3 are the archive and the platform. Pause for
manual confirmation before deploying.

---

## Phase 3: Deploy and verify on the live instance

### Overview

Preserve a rollback artifact, publish, build the flat archive under all three shape assertions,
deploy, and verify that the deployed app applies `InitialSpine` on the boot path, reads and writes
the database, and keeps its Data Protection keys across a container restart. The migration is a real
one here rather than a no-op, because Phase 2 ran against the development database and left the
app's database empty. Record the database round-trip cost against the 2s budget.

### Changes Required:

#### 1. Rollback artifact — before anything else

**File**: `TenExCards/bin/publish-shell-rollback.zip` (git-ignored)

**Intent**: Keep a redeployable copy of what is currently live, because the publish step destroys it
and a failed startup migration means the app does not serve at all. B1 has no deployment slots.

**Contract**: A copy of the archive currently deployed, preserved before `dotnet publish` runs, with
its byte size recorded. F-01 kept `publish-scaffold-rollback.zip` for exactly this reason and the
same convention applies.

#### 2. Release publish

**File**: `TenExCards/bin/Release/net10.0/publish/` (build output, git-ignored)

**Intent**: Produce the framework-dependent deployable.

**Contract**: `dotnet publish -c Release` with no `-o` override, so output lands where `[Bb]in/`
already ignores it.

#### 3. Flat archive with pre-upload assertions

**File**: `TenExCards/bin/publish.zip` (git-ignored)

**Intent**: Package the publish output flat and prove it is flat before anything is uploaded. A
wrong archive **deploys successfully** and then breaks at runtime — a nested zip 503s, and backslash
entries serve a page whose every asset 404s.

**Contract**: Built entry-by-entry with names normalised to `/`. **Not** `Compress-Archive`, and not
`ZipFile::CreateFromDirectory` — under Windows PowerShell 5.1 both write Windows separators into
nested entries. All three assertions must pass before upload: `TenExCards.dll` present at the
archive root, no entry prefixed `publish/`, no entry containing a backslash. A deploy command
reporting success is not evidence that these held.

`F-03` owns turning this into a committed script that exits non-zero; until then it is run by hand,
which is the risk that change's roadmap entry already carries.

#### 4. Deploy

**File**: n/a — Azure App Service `tenexcards-ka`

**Intent**: Push the archive to the running app.

**Contract**: `az webapp deploy --src-path <zip> --type zip --track-status true`. Never
`az webapp up`.

#### 5. Restart-survival check

**File**: n/a — verification against the live app

**Intent**: Prove the thing key persistence exists for. Before this change, every container restart
rotated the key ring; the check is that it no longer does.

**Contract**: Two checks, because the cheap one is not sufficient on its own.

1. `az webapp restart`, then confirm the `DataProtectionKeys` table gained no new row and the app
   still serves. A new key row after restart means persistence is not actually wired.
2. **The check that can actually fail.** Before restarting, load `/db-check` — statically rendered
   with an `<EditForm method="post">`, so the served HTML carries an antiforgery token minted by the
   pre-restart key ring — and leave the tab open without submitting. After the restart, submit that
   already-rendered form and confirm it is accepted rather than rejected with a `400`. Check 1
   alone cannot
   distinguish working persistence from a key ring that simply had no reason to rotate; this is the
   property key persistence exists for, and the only check that regresses if it breaks.

`S-01` is told to verify rather than implement key persistence, so it inherits check 2 as the
verification — not check 1.

#### 6. Boot-path migration proof

**File**: n/a — verification against the live app's startup log

**Intent**: Prove `Database.Migrate()` actually applies a migration on the deploy path, while the
database is still empty and disposable. This is the plan's most-guarded risk ("a failed startup
migration means no app", on a tier with no slot rollback), and it must not first fire in `S-01` on a
database holding accounts.

**Contract**: Because Phase 2 verifies against the *development* database, the app's database is
still empty when this deploy lands, so `InitialSpine` is applied for real on the boot path rather
than being a no-op. Confirm from the startup log that the migration was applied and that
`__EFMigrationsHistory` in the app's database went from absent to containing `InitialSpine` — do not
apply it out of band first. If a migration is ever applied to the app's database ahead of a deploy,
this proof is lost and has to be re-established with a throwaway follow-up migration instead.

#### 7. Measurement record

**File**: `context/changes/persistence-spine/baseline.md` (new)

**Intent**: Record what a database round-trip costs on this platform, so `S-02` inherits a number
rather than an assumption. The F-01 baseline deliberately excluded persistence and said so.

**Contract**: Round-trip timing for a `/db-check` write and read against the live host, the
conditions measured under (warm instance, region, whether the connection pool was cold), and how it
sits against the ~0.152 s TTFB floor already recorded. Same structure and honesty as
`context/changes/blazor-server-shell/baseline.md`, which stays the single home for its own figures.

### Success Criteria:

#### Automated Verification:

- The rollback archive exists and its byte size is recorded before `dotnet publish` runs
- All three archive assertions pass: `TenExCards.dll` at the archive root, no `publish/` prefix, no backslash in any entry
- `az webapp deploy` reports `RuntimeSuccessful` with 1/1 instances
- `GET /` returns 200 and `GET /db-check` returns 200 on the live host
- Every `.css` and `.js` URL referenced by the rendered `/db-check` page returns 200 on the live host
- The startup log shows the app started with no unhandled exception and no Data Protection key-persistence warning
- `az webapp log tail` shows no repeated restart loop after deployment
- The startup log shows `InitialSpine` applied on the boot path, and the app database's `__EFMigrationsHistory` went from absent to containing it

#### Manual Verification:

- `/db-check` on the live host writes a row and the count increments on the re-rendered page
- After `az webapp restart`, previously written probe rows are still present and no new Data Protection key row was created
- A `/db-check` form rendered *before* the restart is still accepted when submitted *after* it, rather than rejected with a `400` — the check that fails if key persistence regresses
- Database round-trip timings are recorded in `baseline.md` with their conditions
- The measured round-trip leaves the 2s acknowledgement budget intact with room for an LLM call

**Implementation Note**: If the app fails to start, the first hypothesis is the connection string —
redeploy the retained rollback archive rather than debugging against a dead site. Pause for manual
confirmation before Phase 4.

---

## Phase 4: Update the repository's own record

### Overview

Four documents make claims this change falsifies, and one open question it closes. This phase makes
them true. Its failure mode is silent: nothing breaks, the records just stop matching reality —
which is exactly what F-01's review found had happened in four places.

### Changes Required:

#### 1. Agent onboarding rules

**File**: `TenExCards/AGENTS.md`

**Intent**: Remove the claim that persistence is undecided, record the rules this change
establishes, and settle the test-project contradiction.

**Contract**: Seven edits, enumerated so the count can be checked against the file afterwards:

(a) `## The scaffold is not the target` — delete "**Persistence is undecided**… Ask before picking
one." and name what was chosen. Add `/db-check` and its probe table to what `S-01` deletes, alongside
`CircuitCheck.razor`.

(b) `## Never do these` — rewrite "**Never ship Identity without persisting Data Protection keys**"
to record that keys now persist to the database via `PersistKeysToDbContext`, so `S-01` verifies
rather than implements it.

(c) `## Never do these` — add a rule that connection strings and passwords never appear in
`appsettings*.json`, a Bicep parameters file, or any tracked file; the deployed value is a Key Vault
reference and local development uses user-secrets.

(d) `## Never do these` — extend the existing `appSettings` rule to state the general principle it is
an instance of: the vault is infrastructure and belongs in the template, secret *values* are
data-plane and never do.

(e) `## Deployment` — a subsection recording what is provisioned, **that there are two databases on
one server and which is which**: local development points at the development database and never at
the app's, because `Database.Migrate()` runs on the boot path and would otherwise let a local
`dotnet run` mutate live schema forward-only, and because the Data Protection key table has no
per-application partition. Also the firewall model including that the development-machine rule needs
re-adding when the home IP changes, and the `configreferences/appsettings` command for checking that
the reference resolves.

(f) `## Testing` — correct the trigger that currently reads "the first feature that touches
generation, triage, or persistence", which points at this change. Restate it as account-scoped
persistence in `S-01`, and say why this change created no test project.

(g) `## Never do these` — record the mechanism this change makes the sharpest edge in the
codebase. `Database.Migrate()` now runs on the boot path, so a migration that throws means the
container does not serve — on a tier with no deployment slots, and with a rollback path (redeploying
the retained archive) that does not reverse schema. Migrations are forward-only; no down migration is
authored. Every other trap of this weight already lives in this section — the three archive
assertions, the forwarded-headers variable, `--mode Complete` — and without this bullet an agent
reading the file after this change learns the persistence choice but not its one fatal failure mode.
Keep the wording consistent with the `## Operational Story` reconciliation in change 3.

Read the file start to finish afterwards as a fresh agent would. F-01's review found a renamed
heading had stranded the phrase "the two variables" with no antecedent; the same class of defect is
the risk here.

#### 2. Deployment record

**File**: `context/deployment/deploy-plan.md`

**Intent**: Keep this file the ground truth for what is live and what was actually run.

**Contract**: A new dated deployment record covering what was provisioned, the deployment id and
artifact size, the two resource-provider registrations as subscription-level mutations that
`az group delete` does not undo, the firewall rules, and an update to the **current** teardown
section — the one headed `## Teardown (supersedes …)`, not the earlier `## Teardown` block, which is
kept unedited as a record of what was run against the retired resource group. Note there that the
vault's soft-delete reserves its name, so recreating it needs `az keyvault purge` first, and that the
contained development-database user and the development-machine firewall rule are data-plane objects
declared in no template and recreated by hand. Measurements are referenced, not copied —
`baseline.md` is their single home.

#### 3. Infrastructure record

**File**: `context/foundation/infrastructure.md`

**Intent**: Close the persistence item and register the risks this change introduces.

**Contract**: Three edits.

(a) `## Getting Started` item 2 ("When persistence is decided…") marked resolved with the choice and
its reasoning.

(b) `## Getting Started`'s Data Protection item, which assigns key persistence to "the same change
that adds Identity" — that is, `S-01`. This change moves it to `F-02`, so the item is false the
moment Phase 2 lands. `AGENTS.md` edit (b) corrects the equivalent claim in that file; nothing
corrects it here unless this edit does.

(c) Four new rows in the risk register, in the file's existing
`| Risk | Source | Likelihood | Impact | Mitigation |` shape: a Key Vault reference that fails to
resolve stores its literal string with no error; RBAC role assignments propagate with a delay that
presents as a permissions bug; a failed startup migration takes the app down on a tier with no
slot rollback; and the "allow Azure services" firewall rule admits any Azure tenant, leaving the SQL
admin password as the only boundary until a private endpoint exists.

**Reconcile the forward-only wording while here.** `## Operational Story` currently allows that
migrations "must be forward-only **or paired with a tested down migration**". This change takes the
first branch flatly, and `AGENTS.md` edit (g) writes that down as a rule. Two records offering
different latitude is how a later change picks whichever branch suits it — settle on one wording and
make both files carry it.

#### 4. Roadmap open question

**File**: `context/foundation/roadmap.md`

**Intent**: Close Open Roadmap Question 1, which sits under every persisting slice.

**Contract**: Question 1 struck through and marked resolved with the date, the provider and tier
chosen, and the reasoning. Follow the **structure** of the resolved Question 4 — strikethrough of
the original text, a dated `**Resolved <date> in `<roadmap-id>`:**` line, the decision, the
reasoning, the verification, and the closing owner/was-blocking line — but **not** its citation
style: Question 4 cites `infra/main.bicep:96`, and criterion 4.9 forbids introducing a `file:NN`
reference a later edit would silently invalidate. Cite property names instead. `F-02`'s own status
line is left to `/10x-implement`.

**Also in the same file**, correct the `S-01` description, which says "This is also the first slice
to touch persistence, so the test project is created here." `F-02` touches persistence first, so
that sentence stops being true — restate it as the first slice to persist **account-scoped** data.
The test project stays assigned to `S-01`; only the justification changes. This is the sentence
`AGENTS.md` edit (f) is being aligned against, so leaving it would make criterion 4.8 false.

**And the `F-02` Risk bullet in the same file**, which says the change "Deliberately designs no
schema". Two tables now exist — `SpineProbes` and `DataProtectionKeys`. The intent behind the
sentence survives and should be kept: no *domain* schema is designed here, identity tables still
arrive with `S-01` and the card entity with `S-02`. Restate it that way rather than deleting it.

### Success Criteria:

#### Automated Verification:

- `grep -n "Persistence is undecided" TenExCards/AGENTS.md` returns nothing
- `TenExCards/AGENTS.md` contains all seven enumerated edits, countable against the list above
- `context/deployment/deploy-plan.md` contains a new dated deployment record naming the database and vault
- `context/foundation/roadmap.md` shows Open Question 1 as resolved, its `S-01` entry no longer claims to be the first slice to touch persistence, and its `F-02` Risk bullet no longer claims the change designs no schema
- `context/foundation/infrastructure.md` has four new risk-register rows in the existing 5-column shape, no longer assigns Data Protection key persistence to `S-01`, and its forward-only wording matches `AGENTS.md`'s
- The Release build is still clean — this phase changes no code

#### Manual Verification:

- `AGENTS.md` reads start to finish as a fresh agent would, with no dangling antecedents or references to deleted sections
- All four records agree about what exists, what it is called, and how the connection string reaches the app
- No line-number citation was introduced that a later edit would silently invalidate — cite property names, not `file:NN`

---

## Testing Strategy

No automated test project is created by this change; `TenExCards.Tests` is deferred to `S-01` by
decision, and Phase 4 corrects the `AGENTS.md` wording that currently points here. The reasoning is
that `AGENTS.md` instructs testing the deterministic rules, and this change contains none — a probe
entity round-trip is an integration test against a live database, and a `DbContext` registration test
mostly re-tests EF Core.

What replaces it is verification that is executable rather than merely visual, listed per phase
above. The three that carry the most weight:

### Integration checks:

- The Key Vault reference resolution check, which is the gate for the whole change and needs no application code
- The local `/db-check` round-trip against the Azure database, which proves read and write before any deployment
- The restart-survival check, which is the only evidence that Data Protection key persistence is actually wired rather than merely configured

### Manual Testing Steps:

1. Read the what-if output line by line, then diff the post-deploy snapshots and reconcile every prediction against what actually changed.
2. Connect to the database with a SQL client using the connection string from the vault; run `SELECT 1`.
3. Run the app locally, click through `/db-check`, restart the app, and confirm the rows are still there.
4. Confirm the `DataProtectionKeys` table has a row after the local app has issued an antiforgery token.
5. Deploy, then exercise `/db-check` on the live host.
6. `az webapp restart`, then confirm no new key row was created and the probe rows survive.
7. Re-read `AGENTS.md` end to end and check every claim it now makes about the database.

## Performance Considerations

The recorded floor is a ~0.152 s median TTFB on a warm B1 instance with no database, no auth and no
LLM call (`context/changes/blazor-server-shell/baseline.md`). Every database round-trip is spent
against the same 2-second acknowledgement budget that `S-02` will also have to fit an LLM call into,
which is why Phase 3 measures rather than assumes.

Two specific effects to watch. `Database.Migrate()` runs on the boot path, so the first request after
a deploy or restart waits behind it — Always On is enabled, so this is paid at restart rather than on
a user request, but it is not free. And S0 is a provisioned 10-DTU tier chosen precisely so the first
query after an idle period does not pay a resume latency; the auto-pausing free offer was ruled out
because that resume can exceed the entire acknowledgement budget.

`EnableRetryOnFailure()` is enabled because Azure SQL produces transient faults, and a retry the
application does not make becomes a user-visible failure.

## Migration Notes

There is no existing data and nothing to migrate. Migrations are forward-only: no down migration is
authored, and the rollback path for this change is redeploying the retained archive, not reversing
schema. `infrastructure.md` records that neither redeploy nor slot swap reverts database migrations,
which is why the forward-only discipline matters from the first migration rather than the first
painful one.

`S-01` inherits two cleanup obligations from this change: dropping the probe table and deleting
`/db-check`, and verifying rather than implementing Data Protection key persistence.

## References

- Roadmap item: `context/foundation/roadmap.md` — `### F-02: Persistence spine`, and Open Question 1
- Budget posture and risk register: `context/foundation/infrastructure.md` — `## Budget Posture`
- Deployment ground truth and archive assertions: `context/deployment/deploy-plan.md` — the 2026-09-08 record
- Phase pattern and record-correction precedent: `context/changes/blazor-server-shell/plan.md`
- Measurement style and the current performance floor: `context/changes/blazor-server-shell/baseline.md`
- Uncommitted pack script, accepted as risk and handed to `F-03`: `context/changes/blazor-server-shell/reviews/impl-review.md` — finding F4
- Infrastructure source of truth, including the `appSettings` omission reasoning: `infra/main.bicep`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Provision the persistence infrastructure

#### Automated

- [x] 1.1 Both providers report Registered — 2df6bf7
- [x] 1.2 az sql db list-editions lists the S0 service objective in polandcentral — 2df6bf7
- [x] 1.3 App database online at S0 and the development database online — 2df6bf7
- [x] 1.4 The site has a system-assigned principal id — 2df6bf7
- [x] 1.5 Both vault secrets exist — 2df6bf7
- [x] 1.6 The Key Vault reference resolves — 2df6bf7
- [x] 1.7 az deployment group create reported Succeeded in Incremental mode — 2df6bf7
- [x] 1.8 Post-deploy diff shows no unintended property change — 2df6bf7
- [x] 1.9 git grep finds no password or connection string in tracked files — 2df6bf7
- [x] 1.10 PSReadLine history contains no secret and no scratch secret file remains — 2df6bf7
- [x] 1.11 infra/main.bicep contains the dated what-if characterisation for all three new resource types — 2df6bf7

#### Manual

- [x] 1.12 A SQL client connects using the vault connection string and runs SELECT 1 — 2df6bf7
- [x] 1.13 Every what-if Delete/Modify line was reconciled against the post-deploy diff and written down — 2df6bf7
- [x] 1.14 Vault permission model is RBAC and purge protection is off — 2df6bf7
- [x] 1.15 The development-database user connects to the dev database and is refused by the app database — 2df6bf7

### Phase 2: EF Core spine, probe entity, and Data Protection keys

#### Automated

- [x] 2.1 Release build is clean at 0 warnings and 0 errors — e6d3949
- [x] 2.2 .config/dotnet-tools.json exists and dotnet tool restore succeeds — e6d3949
- [x] 2.3 dotnet ef migrations list shows InitialSpine — e6d3949
- [x] 2.4 Running locally applies the migration to the development database — e6d3949
- [x] 2.5 Both tables exist in the development database and the app database is still empty — e6d3949
- [x] 2.6 git grep finds no connection string, password, or secret value in tracked files — e6d3949
- [x] 2.7 Neither appsettings file contains a ConnectionStrings section — e6d3949

#### Manual

- [x] 2.8 /db-check locally writes a row and the count increments — e6d3949
- [x] 2.9 Restarting the local app shows previously written rows — e6d3949
- [x] 2.10 DataProtectionKeys contains a row after the /db-check form is rendered and submitted — e6d3949
- [x] 2.11 The Data Protection key-persistence startup warning no longer appears locally — e6d3949

### Phase 3: Deploy and verify on the live instance

#### Automated

- [x] 3.1 Rollback archive exists with its byte size recorded before publish runs — c2a7101
- [x] 3.2 All three archive shape assertions pass — c2a7101
- [x] 3.3 az webapp deploy reports RuntimeSuccessful with 1/1 instances — c2a7101
- [x] 3.4 GET / and GET /db-check both return 200 on the live host — c2a7101
- [x] 3.5 Every css/js URL referenced by the rendered /db-check page returns 200 — c2a7101
- [x] 3.6 Startup log shows no unhandled exception and no key-persistence warning — c2a7101
- [x] 3.7 No repeated restart loop after deployment — c2a7101
- [x] 3.8 Startup log shows InitialSpine applied on the boot path to the app database — c2a7101

#### Manual

- [x] 3.9 /db-check on the live host writes a row and the count increments on the re-rendered page — c2a7101
- [x] 3.10 After az webapp restart, probe rows survive and no new key row was created — c2a7101
- [x] 3.11 A /db-check form rendered before the restart is still accepted after it, not rejected with 400 — c2a7101
- [x] 3.12 Database round-trip timings recorded in baseline.md with their conditions — c2a7101
- [x] 3.13 The measured round-trip leaves the 2s budget intact with room for an LLM call — c2a7101

### Phase 4: Update the repository's own record

#### Automated

- [x] 4.1 grep for "Persistence is undecided" in AGENTS.md returns nothing
- [x] 4.2 AGENTS.md contains all seven enumerated edits
- [x] 4.3 deploy-plan.md contains a new dated deployment record naming the database and vault
- [x] 4.4 roadmap.md shows Open Question 1 resolved, the S-01 persistence claim corrected, and the F-02 no-schema claim restated
- [x] 4.5 infrastructure.md has four new risk rows, the DP-keys item reassigned, and forward-only wording matching AGENTS.md
- [x] 4.6 Release build is still clean

#### Manual

- [x] 4.7 AGENTS.md reads start to finish with no dangling antecedents
- [x] 4.8 All four records agree about what exists and how the connection string reaches the app
- [x] 4.9 No new file:line citation was introduced that a later edit would invalidate
