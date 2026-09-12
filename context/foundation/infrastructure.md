---
project: 10xCards
researched_at: 2026-08-30
recommended_platform: Azure App Service (Linux, B1)
runner_up: Render
context_type: mvp
tech_stack:
  language: C#
  framework: ASP.NET Core / Blazor Server + Identity
  runtime: .NET 10
---

## Recommendation

**Deploy on Azure App Service (Linux, B1 tier).**

Blazor Server keeps each learner's circuit state in memory on one instance, which makes
session affinity structural rather than optional. App Service is the only researched
platform where affinity (ARR) is a first-class, documented, on-by-default toggle — Render
and Railway do not support it at all, and Fly.io documents its equivalent as "an
optimization, not a guarantee". App Service also took all five agent-friendly criteria,
pairs with a co-located managed relational database — Azure SQL, whose free offer is GA and
whose provisioned tiers sit comfortably inside the trial credit — answering the co-location
preference. That co-location bet paid out: `F-02` chose **Azure SQL, S0 provisioned**, on
2026-09-10 — see `## Getting Started` item 2 for the decision and its reasoning. This paragraph
described the choice as still open until then; it is not, and the provider question is closed.
It matches the platform familiarity recorded in the interview — which carries real
weight against a three-week, after-hours deadline. It was already the `deployment_target` in `tech-stack.md`; this research confirms
that default rather than inheriting it.

## Platform Comparison

Hard filters applied before scoring:

- **Persistent connections required** (Blazor Server SignalR circuit) — drops platforms that
  cannot hold a long-lived WebSocket.
- **.NET 10 server-side runtime required** — drops platforms with no path to running an
  ASP.NET Core image.

| Platform | CLI-first | Managed | Agent docs | Deploy API | MCP | Raw |
|---|---|---|---|---|---|---|
| Azure App Service | Pass | Pass | Pass | Pass | Pass | 5P |
| Cloudflare Containers | Pass | Pass | Pass | Partial | Pass | 4P / 1p |
| Render | Partial | Pass | Pass | Partial | Pass | 3P / 2p |
| Fly.io | Pass | Pass | Pass | Partial | Partial | 3P / 2p |
| Railway | Partial | Pass | Pass | Partial | Partial | 2P / 3p |
| Vercel | — | — | — | — | — | dropped (hard filter) |
| Netlify | — | — | — | — | — | dropped (hard filter) |

**Azure App Service** — Full `az webapp` coverage with nothing critical portal-only; docs in
`MicrosoftDocs/azure-docs`, an `llms.txt`, and Markdown via `Accept: text/markdown`; Azure
MCP Server 2.0 is GA (verified 2026-08-30) and covers App Service. Deploy is
`az webapp deploy`; slot swap gives deterministic rollback but requires Standard tier.

**Cloudflare Containers** — GA 2026-04-13; runs any `linux/amd64` OCI image and proxies
WebSockets via `Container.fetch()` with no documented duration cap. Scores second on raw
criteria and drops to fourth on weighting: no co-located relational database is reachable
from EF Core (D1 is HTTP-API-only with no EF Core provider; Hyperdrive is a Worker binding,
and container outbound handlers intercept only ports 80/443, never 5432), cold starts
conflict with the 2-second acknowledgement requirement, and `wrangler rollback` reverts
Worker code only — no container-image rollback exists. Near-zero .NET precedent: no official
guide, and `dotnet/aspnetcore#20358` tracks Blazor-Server-on-Cloudflare unresolved.

**Render** — Best agent integration researched: official MCP server with OAuth plugins for
Claude Code, plus `render skills install` shipping 21 official skills. Docs offer `llms.txt`,
`llms-full.txt`, and a `.md` suffix on any page. Marked down because the load balancer
"assigns each incoming WebSocket connection to a random instance… regardless of past
connection history" — sticky sessions are explicitly unsupported — and rollback is absent
from the CLI (REST API or dashboard only). No native .NET runtime; Docker-only with no
official guide.

**Fly.io** — Best documentation of the field (`llms.txt`, Apache-2.0 Markdown source on
GitHub, per-page copy-as-markdown) and cheapest compute at ~$3–7/mo always-on. `flyctl`
detects `Microsoft.NET.Sdk.Web` and templates the target framework into a generated
Dockerfile. Marked down for no rollback command (redeploy by image digest, which does not
revert `fly.toml` or secrets), an experimental MCP server, and Managed Postgres starting at
$38/mo — roughly ten times the compute cost, making co-location the most expensive line
item.

**Railway** — Weakest agent story: rollback is dashboard-only. Its docs state three times
that "Railpack does not yet support .NET, so a Dockerfile is required", while upstream
Railpack ships a working `dotnet` provider — an unresolved discrepancy that would need an
empirical test rather than another doc read. Sticky sessions unsupported with no timeline.
MCP is explicitly beta with a breaking-changes banner. Networking docs promise WebSockets
"can stay open indefinitely" while community reports show drops at ~10min and ~50min.

**Vercel** — Dropped by hard filter. Can run ASP.NET Core via Container Images, but WebSocket
connections close when a function reaches max duration (300s Hobby / 800s Pro); the 1800s
extended limit is beta and Node/Bun/Python only, not containers. A multi-minute Blazor
circuit is force-dropped mid-session.

**Netlify** — Dropped by hard filter. Node.js and Go function runtimes only, no container
support, no WebSocket hosting. Blazor WebAssembly as a static site is a different
architecture and does not satisfy the requirement.

### Shortlisted Platforms

#### 1. Azure App Service (Recommended)

Won on the convergence of four independent signals: the only first-class session affinity
for an architecture that requires it; five passes on the agent-friendly criteria; a GA
managed relational database with a free grant, answering both the co-location preference and
the open persistence question; and existing familiarity, which is the cheapest risk
reduction available on a three-week budget.

#### 2. Render

Closest on agent operability and the most honest pricing (~$13/mo all-in for compute plus
Postgres). The gap is architectural, not operational: no session affinity means a
single-replica ceiling the platform will not warn you about, and .NET has no supported path
— a Dockerfile you write and maintain inside the deadline.

#### 3. Fly.io

Cheapest compute, best docs, and the only non-Azure platform with any session-affinity
story. The gap is the managed-Postgres floor at $38/mo against a stated co-location
preference, plus the absence of a rollback command and autostop defaults that stop machines
on spare capacity rather than connection liveness — killing live WebSockets until
reconfigured.

## Anti-Bias Cross-Check: Azure App Service

### Devil's Advocate — Weaknesses

1. **The built-in runtime's patch level is Microsoft's to choose, not yours.**
   `DOTNETCORE|10.0` pins major.minor only; App Service decides which 10.0.x the image carries
   and rolls it on its own cadence. Verified 2026-09-01 via
   `az webapp list-runtimes --os linux`: .NET 10 is `Active`/LTS, EOL 2028-12-01 — it is **GA,
   not Preview**. The residual risk is narrower than a support-status risk and still real: when
   a runtime CVE lands you wait for the platform image. The workaround — self-contained publish
   or a custom container — buys the pin and discards the zip-deploy simplicity that makes App
   Service attractive in the first place. **Not measured here:** how far App Service's image
   actually trails a dotnet patch release. Measure before relying on either answer.
2. **Deployment slots require Standard tier, roughly 5× B1.** Slot swap is Azure's one
   genuine rollback advantage over every competitor, and at the tier actually being run it
   does not exist. At B1, rollback is "redeploy the previous artifact" — the same story Fly
   and Render were marked down for.
3. **B1's 1.75 GB is a real ceiling and Blazor Server's memory is per-user.** ~250 KB per
   circuit is a floor before application state, and this app deliberately holds a
   multi-thousand-word passage plus its candidate cards in circuit memory until triage ends.
   There is no back-pressure; the limit surfaces as OOM restarts that present as random
   disconnects.
4. **The free tier is not a stepping stone.** F1 caps WebSockets at 5 concurrent connections
   per instance, has no Always On, and no custom domain or SSL. There is no "try free, then
   upgrade" path — B1 from day one.
5. **$13.15/mo is the compute floor, not the bill.** Egress, Azure SQL beyond the free grant,
   and Application Insights ingestion are separately metered. Fly and Render quote numbers
   much closer to all-in.

### Pre-Mortem — How This Could Fail

Six months on, the deploy that mattered was the one that didn't happen. The team shipped on
B1 because slots looked like a later problem, and for a while it was fine — a handful of
users, circuits cheap, the free SQL grant untouched. Then a runtime CVE landed and the
built-in image still carried the previous patch. Nobody had ever measured how long App
Service takes to roll one, so there was no estimate to give anyone. The documented fix was
moving to a custom container: writing the Dockerfile this platform had been chosen to avoid,
and re-learning deployment three weeks before a demo.

Meanwhile the memory ceiling arrived early and disguised. Learners pasting long articles
pushed circuits well past the 250 KB estimate; the app restarted under load, and because
every restart drops every circuit, users hit the reconnect overlay mid-triage and lost
untriaged candidates — which by design are never persisted and cannot be recovered. It read
as flakiness, not capacity. Scaling out meant ARR affinity plus Data Protection keys in
shared storage, neither configured, both discovered during an incident rather than before
one.

None of it was unknowable. All of it was deferred.

### Unknown Unknowns

- **ARR affinity is cookie-based.** A client blocking cookies falls back to random routing.
  This fails silently and only manifests above one instance — so it surfaces the first time
  you scale, under load.
- **The Azure SQL free offer auto-pauses.** The first query after a pause pays a resume
  latency that can exceed the entire 2-second acknowledgement budget. The cheapest database
  option and the tightest requirement are in direct tension.
- **`az webapp up` is deprecated**, and training data is saturated with it. An agent will
  reach for it unprompted. This belongs in the project rules file, not only here.
- **Every deploy drops every live circuit** — true on all platforms researched. What differs
  is that Blazor's default reconnect UX is a grey modal overlay, making deploys visible to
  users mid-session unless customised.
- **Multi-instance additionally requires Data Protection keys in shared storage**, or auth
  cookies and antiforgery tokens break on instance switch. This is a second prerequisite,
  documented separately from the affinity toggle.

## Decision After Cross-Check

**2026-09-01 — proceed with Azure App Service (Linux, B1). Leader kept, no swap.**

The deciding reason is operational, not architectural: Azure meets every requirement, and it
is the platform the author knows well enough to hit the deadline **and to unblock an agent
that gets stuck on it**. The second half is the load-bearing part. An agent failing on a
familiar platform is a delay; on an unfamiliar one it is a hard stop, because nobody can tell
whether the agent or the platform is at fault. `## Recommendation` above calls familiarity
"the cheapest risk reduction available" — that framing is about speed and undersells it. It is
really about recoverability.

The architecture corroborates rather than drives. Blazor Server circuits need real session
affinity; Render does not support sticky sessions at all, and Fly documents its equivalent as
"an optimization, not a guarantee". Both would also require a hand-maintained Dockerfile.

Four things changed between the research and this decision, each re-checked rather than
assumed:

- Devil's-advocate #1 is void — .NET 10 is GA/LTS on App Service, not Preview.
- Cost sensitivity was overstated; see `## Budget Posture`. The competitors' pricing advantage
  was scored against a constraint that did not exist.
- A constraint the research missed: region availability on this subscription (see the risk
  register). Resolved by Poland Central.
- Azure is now the only candidate backed by evidence rather than research — the deploy ran and
  was re-verified live on 2026-09-01. Every other row in the scoring matrix is still
  single-sourced.

**Risks knowingly accepted:** the B1 memory ceiling against per-circuit state; no slot-based
rollback at B1; and `spendingLimit: On`, which stops the app rather than billing when trial
credit runs out.

The cross-check did not change the platform, but it was not a ritual: it changed the risk
register, and the memory ceiling, the rollback gap, and the affinity-plus-Data-Protection
prerequisite are now rules in `TenExCards/AGENTS.md`. Noted because `CLAUDE.md` warns that a
cross-check which never flips a decision has degraded into theatre — here it did its work on
the register rather than on the choice.

## Operational Story

- **Preview deploys**: Deployment slots produce a preview URL per slot, but slots require
  Standard tier — unavailable at B1. At B1 the practical preview is a second App Service
  instance (e.g. `tenexcards-staging`) deployed from a branch by GitHub Actions, matching the
  `ci_default_flow: auto-deploy-on-merge` already recorded in `tech-stack.md`.
- **Secrets**: App settings via `az webapp config appsettings set --settings K=V`, readable
  by anyone with Contributor on the resource group. The LLM API key belongs in Azure Key
  Vault referenced from app settings, not stored inline. GitHub Actions authenticates with a
  federated credential (OIDC), not a long-lived publish profile. Rotation is manual and
  human-only.
- **Rollback**: At B1, `az webapp deploy --src-path <previous>.zip` — deterministic only if
  build artifacts are retained per release, so keep them as GitHub Actions artifacts. At
  Standard+, `az webapp deployment slot swap -s staging` reverts in seconds. Neither reverts
  database migrations. **EF Core migrations here are forward-only — full stop.** The earlier
  wording allowed "or paired with a tested down migration"; `F-02` took the first branch flatly and
  `TenExCards/AGENTS.md` records it as a rule, so that latitude is withdrawn rather than left for a
  later change to pick whichever branch suits it. No down migration is authored or relied on, and
  the `Down()` method EF generates is not a rollback story. `Database.Migrate()` runs on the **boot
  path**, so a migration that throws means the container does not serve at all.
- **Approval**: The agent may deploy to staging, read logs, list runtimes, and set non-secret
  app settings unattended. Human-only: publishing to production, rotating the LLM API key or
  any Key Vault secret, deleting the App Service plan, and any operation against the database
  server (drop, scale, restore).
- **Logs**: `az webapp log tail --name <app> --resource-group <rg>` streams live;
  `az webapp log download` for archives; `az webapp log config --level Verbose` raises
  verbosity. Azure MCP Server 2.0 (GA, verified 2026-08-30) exposes App Service among 40+
  services for structured queries when parsing CLI output becomes the bottleneck.

## Budget Posture

New-customer Free Trial: `quotaId: FreeTrial_2014-09-01`, `spendingLimit: On`, `freetier`
promotion to 2027-09-30 — verified 2026-09-01 via
`az rest --method get --url "https://management.azure.com/subscriptions/<id>?api-version=2022-12-01"`.
Note `az account show` does **not** return `subscriptionPolicies`; it reports `null` for both
fields. Per the account owner it carries the standard $200 / 30-day credit (the balance is not
exposed by that GET), and this course is its only workload.

The selection rule is therefore **the cheapest option that removes a risk or saves real time —
not the cheapest option.** Unspent credit expires worthless, and `spendingLimit: On` makes the
ceiling a hard stop rather than a bill, so a paid tier inside the credit costs nothing extra.
**Prefer a provisioned tier over an auto-pausing one** — the first query after a pause can exceed
the whole 2s acknowledgement budget, which is the concrete case this rule was written to decide.
Out of scope: anything whose *ongoing* cost matters after the trial, and anything overkill for a
single-developer MVP — no Premium/Isolated App Service, no multi-region, no reserved capacity.

**This paragraph is the rule's only home**, as of 2026-09-12. `TenExCards/AGENTS.md` carried a
summary of it inside a section explicitly written to be deleted once `S-01` landed; `S-01` moved it
here rather than letting a section-wide delete drop it silently. An agent reading only `AGENTS.md`
will not find the rule — that file points here for it.

Recorded because it is not the assumption the research above ran under, and it changes the
database recommendation. The B1 App Service tier is left as-is for now and may be relaxed later
if the circuit-memory ceiling in the Devil's Advocate list proves real.

## Risk Register

**On the `Source` column.** The skill's template offers four lenses — `Devil's advocate`,
`Pre-mortem`, `Unknown unknowns`, `Research finding` — and this register deliberately extends
them with two more: `Deployment run 2026-08-31` and `Empirical test 2026-08-31`. Those eight
rows were added after the first deploy, and they record things learned by *doing* rather than
by reading. The skill writes this file once and has no update mode, so it never anticipated
post-deployment amendment; the extra labels are the honest name for where those rows came from.
The stated purpose of the column is that "a future reader can see *why* each item is on the
list", and a dated run satisfies that better than folding them into `Research finding` would.
Rows carrying a date were observed on that date and should be re-verified rather than assumed.

2026-09-12 added one more label in the same spirit — `Change <change-id> <date>`, for a risk a
slice *created* rather than discovered. It also introduced the register's first closure: a
**struck-through** risk is closed, and its mitigation cell records the date, the mechanism, and how
the closure was verified. Closed rows stay in the table rather than being deleted, because the
reasoning is what tells a later reader whether a change reopens them.

| Risk | Source | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| Built-in runtime patch level is Microsoft's to choose; a CVE fix arrives on the platform's cadence, not yours | Devil's advocate | M | M | Measure how far App Service's image trails a dotnet patch release before relying on it; if the gap is unacceptable, publish self-contained or use a custom container image so the version is yours to pin; verify the deployed patch level after each release |
| B1 memory exhausted by circuits holding passages plus candidates | Devil's advocate / Pre-mortem | L | M | **Re-rated 2026-09-01 from M/H against the PRD's stated scale.** ~300 KB per active circuit (250 KB baseline plus a few-thousand-word passage and its candidates) against ~1.3 GB usable leaves headroom for thousands of concurrent circuits; the PRD records `target_scale.qps: low` and "at most a handful of concurrent requests". The original M/H came from the platform's general characteristics rather than this project's numbers. Enforce the passage-length bound before generation begins (a PRD requirement regardless). **Revisit — and move to B2 — if concurrent triage sessions reach the low tens, or if circuit state grows beyond the passage plus its candidates.** No load test is warranted at this scale |
| No slot-based rollback at B1 | Devil's advocate | H | M | Retain every deploy artifact in GitHub Actions; document and rehearse the redeploy-previous-zip path once, before it is needed |
| ARR affinity fails silently for cookie-blocking clients | Unknown unknowns | L | H | Stay single-instance for MVP; test affinity with cookies disabled before trusting it above one worker. **The Data Protection half of this row closed 2026-09-10 in `F-02`** — the ring persists to the `DataProtectionKeys` table (see Getting Started item 3), and it was never a scaling concern in the first place: an ephemeral ring broke antiforgery at **one** instance on every restart. Its *encryption* half closed 2026-09-12 in `S-01`, in the row below. Affinity is what remains scaling-shaped |
| Auto-pausing database tiers blow the 2s acknowledgement budget on the first query after idle | Unknown unknowns | M | M | Do not take this risk to conserve credit that expires unspent (see `## Budget Posture`) — choose a provisioned tier that does not auto-pause (Azure SQL Basic/S0, or PostgreSQL Flexible Server B1ms). Measure cold-resume latency only if the free offer is chosen anyway |
| Deploys drop live circuits mid-triage, losing untriaged candidates | Pre-mortem / Research finding | H | M | Deploy outside usage windows; customise the Blazor reconnect UI to explain what happened rather than showing the default grey overlay |
| Agent reaches for deprecated `az webapp up` | Unknown unknowns | H | L | Record the deprecation in `TenExCards/AGENTS.md`; the correct command is `az webapp deploy --src-path` |
| Actual bill exceeds the $13.15 compute floor | Devil's advocate | M | L | Set an Azure budget alert before the first deploy; egress and Application Insights ingestion are metered separately |
| F1 tier assumed viable for a demo (5 WebSocket connections) | Research finding | M | M | Do not provision F1 at any point; B1 is the minimum for this architecture |
| Chosen Azure region refuses new customers or has 0 quota for the SKU | Deployment run 2026-08-31 | M | M | **A Free Trial constraint, not an Azure-wide fact — do not read this as "West Europe is broken".** On this trial subscription West Europe returns `RequestDisallowedByAzure` (region closed to *new* customers; existing tenants there are unaffected) and North Europe returns `Current Limit (B1 VMs): 0`. Both reproduced identically ~2h apart, so neither is transient — but both are **requestable**: region access and quota increases are free support tickets, generally granted only on Pay-As-You-Go. A paid subscription may hit neither. Quota is region-specific: Poland Central and Sweden Central accepted B1 unchanged |
| `az appservice list-locations` names regions that then fail at create time | Deployment run 2026-08-31 | H | M | It reports where the SKU *exists*, not where *you* may deploy it — it listed both West Europe and North Europe and both failed. There is no reliable read-only preflight for "can I provision here"; validate a region by attempting a provision (a failed create bills nothing) **before** committing an architecture to it |
| Free Trial subscription has `spendingLimit: On` — paid resources are disabled when credit runs out | Deployment run 2026-08-31 | H | H | The subscription is `FreeTrial_2014-09-01` with the spending limit ON. B1 provisions today against credit, but the app **stops** rather than bills when credit is exhausted. Free credits are a **deliberate choice for the course**, not a misconfiguration — this row is a note for whoever takes the project past it, not a defect to fix now. Upgrade to Pay-As-You-Go before anything depends on uptime |
| IaC declaring `appSettings` silently deletes secrets set out of band | Deployment run 2026-08-31 | M | H | A Bicep/ARM template that declares `appSettings` becomes authoritative over them, so a routine, successful-looking deployment removes the LLM API key and connection string. `infra/main.bicep` deliberately omits the block; keep it omitted and set secrets with `az webapp config appsettings set` / Key Vault references |
| `az deployment group create --mode Complete` deletes resources absent from the template | Deployment run 2026-08-31 | L | H | Complete mode is a delete wearing an update's name and would destroy the plan and web app. Only Incremental (the default) is used here; preview any template change with `az deployment group what-if` first |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED=false` turns `UseHttpsRedirection()` into an infinite redirect | Empirical test 2026-08-31 | L | H | The port alone returns `200`; the port **plus** disabled forwarded headers returns `307` with `Location` equal to the request URL. The Linux container supplies `X-Forwarded-Proto` by default, so neither setting should ever be added. Prefer deleting `UseHttpsRedirection()` from `Program.cs` — `--https-only` already redirects at the platform |
| Nested zip deploys successfully, then the site 503s | Deployment run 2026-08-31 | M | H | The trailing `*` in `Compress-Archive -Path <publish>/*` is load-bearing. Assert the first entry is `TenExCards.dll`, not `publish/TenExCards.dll`, before every upload |
| A deployment reporting `Succeeded` silently cleared a read-only plan property | Empirical test 2026-08-31 | M | M | `az deployment group create` on `infra/main.bicep` changed `properties.freeOfferExpirationTime` from `2026-09-30` to `null`; `what-if` predicted it correctly as `- Delete` and it was waved through because two genuine phantoms (`siteConfig.localMySqlEnabled`, `siteConfig.netFrameworkVersion`) sat in the same output. No `az` command restores it. Snapshot and diff around every template deployment |
| A malformed Key Vault reference stores its literal string and never errors | Deployment run 2026-09-10 | M | H | App Service stores an unparseable `@Microsoft.KeyVault(...)` value verbatim and logs nothing; the app then receives the literal string as its connection string and fails at first use, not at configuration time. A versionless reference needs a **trailing slash** after the secret name — omitting it changes the meaning rather than erroring. Verify with an `az rest` GET on `config/configreferences/appsettings` expecting `"status": "Resolved"`; **not** `az resource show`, which cannot address that collection endpoint and answers `Not Found` — indistinguishable from a genuine failure |
| RBAC role assignments propagate with a delay that presents as a permissions bug | Deployment run 2026-09-10 | M | M | Creating a vault does not grant its creator data-plane access under the RBAC permission model, and a fresh `Key Vault Secrets Officer` assignment can return `Forbidden` for a minute or two. The same delay applies to the *site's* identity: App Service resolves Key Vault references at app **start** and caches the outcome, so a reference set before propagation reports unresolved and does not re-heal. Treat the first non-`Resolved` reading as expected — restart, wait, read again — and escalate to the template only after that. Both `az keyvault secret set` calls in fact succeeded first time on 2026-09-10; the retry stays because the failure mode is silent, not because it fired |
| A failed startup migration takes the app down with no slot to roll back to | Deployment run 2026-09-10 | L | H | `Database.Migrate()` runs on the boot path, so a migration that throws means the container does not serve — and B1 has no deployment slots. The rollback path is redeploying the retained previous archive, which does **not** reverse schema. Mitigations in force: migrations are forward-only, every migration is applied to `sqldb-tenexcards-dev` before the app's database sees it, the outcome is logged explicitly rather than left to an unhandled exception, and an archive is preserved before each publish. `F-02` deliberately fired the first real boot-path migration against an **empty** database so the risk was exercised while it was cheap, rather than first in `S-01` against a database holding accounts |
| The "allow Azure services" firewall rule admits any Azure tenant | Deployment run 2026-09-10 | M | H | `AllowAllWindowsAzureIps` is a `0.0.0.0`–`0.0.0.0` entry, and that pair is a magic value rather than a range: it admits traffic originating anywhere in Azure — any subscription, any tenant. For Azure-originating traffic there is therefore **no network boundary**; the boundary is the SQL admin password, which is why it lives in the vault and never on a development machine. Pinning to the app's `possibleOutboundIpAddresses` was considered and declined: those IPs are shared across a scale unit, so it narrows "all of Azure" only to "every app on this scale unit" while breaking the app whenever the IP set rotates. Real isolation needs a private endpoint — out of scope at this tier and budget |
| ~~The Data Protection key ring is stored unencrypted, so database read access buys token — then session — forgery~~ | Deployment run 2026-09-10 | M | H | **CLOSED 2026-09-12 in `S-01`.** Mechanism: `infra/main.bicep` declares the RSA vault key `dataprotection-key` and a second vault-scoped role assignment granting the site's identity `Key Vault Crypto User`; `Program.cs` chains `.ProtectKeysWithAzureKeyVault(...)` onto the existing `PersistKeysToDbContext<AppDbContext>()`, guarded to skip in Development. Enabling encryption does **not** encrypt keys already written, so the existing plaintext row (`Id = 1`, 887 bytes) was deleted and the container restarted, minting `Id = 2` at 1,942 bytes wrapped with the vault key. Done before any account existed, which is the only reason it was free. Verified in the data, not the log: the row carries `AzureKeyVaultXmlDecryptor` and carries neither `<masterKey>` nor the plaintext writer's `Warning: the key below is in an unencrypted form.` comment — **not** by testing for `<value>`, which appears in both forms and reports a false failure |
| Database read access still returns every `AspNetUsers.PasswordHash` | Change `S-01` 2026-09-12 | M | M | **Open, and deliberately not closed by the row above.** Encrypting the key ring removed *session forgery* from what database read access buys; it removed neither the access nor the password hashes that `S-01` began writing. What carries the residual risk is the **password policy**, not the key ring: Identity stores PBKDF2-HMAC-SHA512 with a per-user 128-bit salt and a high iteration count, and `S-01` sets a **sixteen-character minimum** on top of that — long enough that a stolen hash is uneconomic to attack offline. That is the security argument for the length, and it is load-bearing: the other two reasons recorded for it (length beats composition; a forgotten password is a permanently dead account) are usability arguments. **Revisit this row if the password policy is ever loosened.** Real closure needs a network boundary on the database — see the firewall row above, and the private endpoint it rules out at this tier |
| Anyone who can push to `main` can mint a `Contributor` token for the resource group | Deployment run 2026-09-10/11 | M | H | **Open.** OIDC removed the stored credential; it did not narrow *who* can deploy. The federated credential is exact-match on an immutable subject scoped to `refs/heads/main`, so no other ref authenticates — but a push to `main` legitimately holds one. The controls are branch protection on `main` and a role narrower than `Contributor` on `gh-tenexcards-deploy`, and **neither is in place**. Restated here on 2026-09-12 because `S-01` closed the key-ring row above, and a closed security row invites the reading that the deployment trust boundary is settled. It is not |

## Getting Started

**The first deployment has already happened.** The scaffold is live at
`https://tenexcards-ka.azurewebsites.net` in `polandcentral` — not the originally planned West
Europe; see the region rows in the risk register. The commands that were actually run, with the
corrections that run produced and the settings deliberately left unset, are in
`context/deployment/deploy-plan.md`, which is **authoritative for command text**.

This section deliberately does not restate them. It used to, and the two copies drifted: after
the 2026-09-01 review corrected `az appservice plan create --enriched-errors` in the deployment
record, this section still taught the weaker command — the exact flag that would have diagnosed
the West Europe and North Europe failures.

What is left to do, in order:

1. **Prove a WebSocket, not just HTTP.** The deploy verified `GET /weatherforecast`. No WebSocket
   has ever been opened against this app, and the SignalR circuit is the reason this platform was
   chosen at all (see `## Recommendation`). Open one as part of the first Blazor change, before
   circuits carry user state.
2. ~~**When persistence is decided** — prefer a provisioned tier over the auto-pausing free
   offer.~~ **Resolved 2026-09-10 in `F-02` (`persistence-spine`): Azure SQL, S0 provisioned,**
   `polandcentral`, beside the app. The guidance was followed rather than merely noted: S0 is
   provisioned precisely so the first query after an idle period pays no resume latency against the
   2s budget. The connection string is an app setting holding a **Key Vault reference**, resolved
   through the site's system-assigned identity — never inline, never in the template. A second,
   Basic database (`sqldb-tenexcards-dev`) exists for local development; that split is a boundary,
   not tidiness, and `TenExCards/AGENTS.md` under `## Deployment` explains why. Measured cost of a
   round-trip: `context/changes/persistence-spine/baseline.md`.
3. ~~**Persist Data Protection keys in the same change that adds Identity.**~~ **Done in two
   halves, and they are two guarantees rather than one.** *Persistence* landed 2026-09-10 in `F-02`,
   pulled forward because `UseAntiforgery()` was already in the pipeline and the ephemeral ring was
   already protecting something: `Program.cs` calls `PersistKeysToDbContext<AppDbContext>()`.
   *Encryption at rest* landed 2026-09-12 in `S-01` via `.ProtectKeysWithAzureKeyVault(...)`, guarded
   to skip in Development, against the vault key `dataprotection-key` the template now declares.
   `S-01` also re-verified the persistence half in its strongest form — sign in, restart the
   container, reuse the pre-restart cookie and confirm the session survives — which is what proves
   encryption did not break persistence. The key row was unchanged across that restart, so the app
   unwrapped the existing key through Key Vault rather than minting a replacement. Both rules, and
   how each must be verified, are in `TenExCards/AGENTS.md` under `## Never do these`.
4. **Verify the circuit end to end**: load the app, submit a passage, and confirm the
   acknowledgement lands under 2s with progress visible for the full generation window, with
   `az webapp log tail` running in a second terminal.

The deployment rules that must not be rediscovered the hard way — deprecated `az webapp up`, the
flat-zip requirement, the two HTTPS environment variables, `--mode Complete`, and `what-if`
deletions — are in `TenExCards/AGENTS.md` under `## Never do these`.

## Out of Scope

The following were not evaluated in this research:

- Docker image configuration
- CI/CD pipeline setup
- Production-scale architecture (multi-region, HA, DR)
