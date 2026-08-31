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
pairs with an Azure SQL free offer that is GA (answering the co-location preference and the
still-open persistence decision in `tech-stack.md`), and matches the platform familiarity
recorded in the interview — which carries real weight against a three-week, after-hours
deadline. It was already the `deployment_target` in `tech-stack.md`; this research confirms
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

1. **.NET 10 is formally Preview on App Service and patch versions lag.** Verified
   2026-02-12: App Service had only 10.0.0 deployed while 10.0.2/10.0.3 had shipped, with no
   published ETA for patch availability. A runtime CVE cannot be patched on your schedule.
   The workaround — custom container or self-contained publish — discards the zip-deploy
   simplicity that makes App Service attractive in the first place.
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
5. **$12.41/mo is the compute floor, not the bill.** Egress, Azure SQL beyond the free grant,
   and Application Insights ingestion are separately metered. Fly and Render quote numbers
   much closer to all-in.

### Pre-Mortem — How This Could Fail

Six months on, the deploy that mattered was the one that didn't happen. The team shipped on
B1 because slots looked like a later problem, and for a while it was fine — a handful of
users, circuits cheap, the free SQL grant untouched. Then a runtime CVE landed in .NET
10.0.4 and App Service still served 10.0.1. The documented fix was moving to a custom
container: writing the Dockerfile this platform had been chosen to avoid, and re-learning
deployment three weeks before a demo.

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
  database migrations; EF Core migrations must be forward-only or paired with a tested down
  migration.
- **Approval**: The agent may deploy to staging, read logs, list runtimes, and set non-secret
  app settings unattended. Human-only: publishing to production, rotating the LLM API key or
  any Key Vault secret, deleting the App Service plan, and any operation against the database
  server (drop, scale, restore).
- **Logs**: `az webapp log tail --name <app> --resource-group <rg>` streams live;
  `az webapp log download` for archives; `az webapp log config --level Verbose` raises
  verbosity. Azure MCP Server 2.0 (GA, verified 2026-08-30) exposes App Service among 40+
  services for structured queries when parsing CLI output becomes the bottleneck.

## Risk Register

| Risk | Source | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| .NET 10 patch versions lag App Service; CVE unpatchable on demand | Devil's advocate | M | H | Publish self-contained or use a custom container image so the runtime version is yours to pin; verify the deployed patch level after each release |
| B1 memory exhausted by circuits holding passages plus candidates | Devil's advocate / Pre-mortem | M | H | Enforce the passage-length bound before generation begins; load-test concurrent triage sessions on B1 before launch; alert on memory above 70% |
| No slot-based rollback at B1 | Devil's advocate | H | M | Retain every deploy artifact in GitHub Actions; document and rehearse the redeploy-previous-zip path once, before it is needed |
| ARR affinity fails silently for cookie-blocking clients | Unknown unknowns | L | H | Stay single-instance for MVP; before scaling out, configure Data Protection keys in shared storage and test affinity with cookies disabled |
| Azure SQL free-offer auto-pause breaks the 2s acknowledgement budget | Unknown unknowns | M | M | Measure cold-resume latency before committing; if it exceeds budget, move to PostgreSQL Flexible Server B1ms (~$12/mo), which does not auto-pause |
| Deploys drop live circuits mid-triage, losing untriaged candidates | Pre-mortem / Research finding | H | M | Deploy outside usage windows; customise the Blazor reconnect UI to explain what happened rather than showing the default grey overlay |
| Agent reaches for deprecated `az webapp up` | Unknown unknowns | H | L | Record the deprecation in `TenExCards/AGENTS.md`; the correct command is `az webapp deploy --src-path` |
| Actual bill exceeds the $12.41 compute floor | Devil's advocate | M | L | Set an Azure budget alert before the first deploy; egress and Application Insights ingestion are metered separately |
| F1 tier assumed viable for a demo (5 WebSocket connections) | Research finding | M | M | Do not provision F1 at any point; B1 is the minimum for this architecture |
| Chosen Azure region refuses new customers or has 0 quota for the SKU | Deployment run 2026-08-31 | M | M | **A Free Trial constraint, not an Azure-wide fact — do not read this as "West Europe is broken".** On this trial subscription West Europe returns `RequestDisallowedByAzure` (region closed to *new* customers; existing tenants there are unaffected) and North Europe returns `Current Limit (B1 VMs): 0`. Both reproduced identically ~2h apart, so neither is transient — but both are **requestable**: region access and quota increases are free support tickets, generally granted only on Pay-As-You-Go. A paid subscription may hit neither. Quota is region-specific: Poland Central and Sweden Central accepted B1 unchanged |
| `az appservice list-locations` names regions that then fail at create time | Deployment run 2026-08-31 | H | M | It reports where the SKU *exists*, not where *you* may deploy it — it listed both West Europe and North Europe and both failed. There is no reliable read-only preflight for "can I provision here"; validate a region by attempting a provision (a failed create bills nothing) **before** committing an architecture to it |
| Free Trial subscription has `spendingLimit: On` — paid resources are disabled when credit runs out | Deployment run 2026-08-31 | H | H | The subscription is `FreeTrial_2014-09-01` with the spending limit ON. B1 provisions today against credit, but the app **stops** rather than bills when credit is exhausted. Free credits are a **deliberate choice for the course**, not a misconfiguration — this row is a note for whoever takes the project past it, not a defect to fix now. Upgrade to Pay-As-You-Go before anything depends on uptime |
| IaC declaring `appSettings` silently deletes secrets set out of band | Deployment run 2026-08-31 | M | H | A Bicep/ARM template that declares `appSettings` becomes authoritative over them, so a routine, successful-looking deployment removes the LLM API key and connection string. `infra/main.bicep` deliberately omits the block; keep it omitted and set secrets with `az webapp config appsettings set` / Key Vault references |
| `az deployment group create --mode Complete` deletes resources absent from the template | Deployment run 2026-08-31 | L | H | Complete mode is a delete wearing an update's name and would destroy the plan and web app. Only Incremental (the default) is used here; preview any template change with `az deployment group what-if` first |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED=false` turns `UseHttpsRedirection()` into an infinite redirect | Empirical test 2026-08-31 | L | H | The port alone returns `200`; the port **plus** disabled forwarded headers returns `307` with `Location` equal to the request URL. The Linux container supplies `X-Forwarded-Proto` by default, so neither setting should ever be added. Prefer deleting `UseHttpsRedirection()` from `Program.cs` — `--https-only` already redirects at the platform |
| Nested zip deploys successfully, then the site 503s | Deployment run 2026-08-31 | M | H | The trailing `*` in `Compress-Archive -Path <publish>/*` is load-bearing. Assert the first entry is `TenExCards.dll`, not `publish/TenExCards.dll`, before every upload |
| A deployment reporting `Succeeded` silently cleared a read-only plan property | Empirical test 2026-08-31 | M | M | `az deployment group create` on `infra/main.bicep` changed `properties.freeOfferExpirationTime` from `2026-09-30` to `null`; `what-if` predicted it correctly as `- Delete` and it was waved through because two genuine phantoms (`siteConfig.localMySqlEnabled`, `siteConfig.netFrameworkVersion`) sat in the same output. No `az` command restores it. Snapshot and diff around every template deployment |

## Getting Started

Commands verified against .NET 10 and the current Azure CLI as of 2026-08-30, then **executed
end to end on 2026-08-31** (Azure CLI 2.89.1); corrections from that run are folded in below.
Note that `az webapp up` is **deprecated** — do not use it.

**Region: `polandcentral`.** West Europe is rejected by ARM with `RequestDisallowedByAzure` — "the
selected region is currently not accepting new customers". North Europe passes that check and then
fails on B1 quota (`Current Limit (B1 VMs): 0`). Both were tested twice, ~2 hours apart, with
identical results — neither is a transient blip.

**Read both as Free Trial constraints, not as Azure-wide facts.** This project deliberately runs
on free course credits. Region access and quota increases are each *requestable* — free support
tickets — but are generally granted only on Pay-As-You-Go, which makes them effectively closed on
a trial. On a paid subscription West Europe or North Europe may work immediately, or be one
ticket away. Neither region is broken, and neither should be struck off a future architecture.

The B1 quota block is **region-specific, not subscription-wide** — Poland Central and Sweden
Central both accept B1 on the same subscription. Poland Central is the choice: lowest latency for
a Poland-based developer and EU data residency.

1. **Create the plan and app on Linux, pinned to .NET 10.** Confirm the runtime string first
   with `az webapp list-runtimes --os linux`. .NET 10 is **GA and LTS** on App Service, not
   Preview-tagged (verified 2026-08-31: `DOTNETCORE|10.0`, support `Active`, EOL 2028-12-01).
   Note that `--query "[?starts_with(@,'DOTNETCORE')]"` fails on CLI 2.89+ — the command now
   returns objects, not strings; filter with `grep -i dotnet` instead.
   `az appservice plan create -g <rg> -n tenexcards-plan --location polandcentral --is-linux --sku B1`
   `az webapp create -g <rg> -p tenexcards-plan -n tenexcards --runtime "DOTNETCORE:10.0" --https-only true`
   The colon form is accepted on input and normalizes to `DOTNETCORE|10.0` in `linuxFxVersion`.
   Verify `reserved: true` on the plan — that, not `kind`, is the real signal the plan is Linux.
2. **Do not set `--web-sockets-enabled`; it is a no-op on Linux.** Per the App Service on Linux
   FAQ, *"the `webSocketsEnabled` ARM setting doesn't apply to Linux apps since WebSockets are
   always enabled for Linux."* The default-Off behaviour is Windows-only, so Blazor Server needs
   nothing enabled here. Session affinity defaults to On; verify rather than assume:
   `az webapp config set -g <rg> -n tenexcards --always-on true --min-tls-version 1.2 --ftps-state Disabled`
3. **Enforce HTTPS with `--https-only true` at the platform. Add neither `ASPNETCORE_HTTPS_PORT`
   nor `ASPNETCORE_FORWARDEDHEADERS_ENABLED`.** The Linux .NET container supplies
   `X-Forwarded-Proto`, so `Request.IsHttps` is already true for real traffic and
   `UseHttpsRedirection()` finds no port and no-ops.

   The redirect loop needs **both** settings, and comes from the missing forwarded scheme rather
   than from the port (verified against the live app, 2026-08-31):

   - `ASPNETCORE_HTTPS_PORT=443` alone → `200`, zero redirects.
   - that port **plus** `ASPNETCORE_FORWARDEDHEADERS_ENABLED=false` → `307` whose `Location` is the
     request's own URL, i.e. `ERR_TOO_MANY_REDIRECTS`.

   `HttpsRedirectionMiddleware[3] Failed to determine the https port for redirect` appears once at
   startup, from App Service's internal plain-HTTP warm-up probe. It is not emitted per request and
   is not a defect.

   Because the platform already redirects (`http://` → `301` → `https://`), `UseHttpsRedirection()`
   in `Program.cs` is redundant — per Microsoft, *"If the proxy also handles HTTPS redirection,
   there's no need to use HTTPS redirection middleware."* Remove it rather than configuring around
   it.
4. **Publish and deploy**, running `dotnet` from `TenExCards/` per the project rules:
   `dotnet publish -c Release` — do **not** pass `-o ./publish`. The default output,
   `bin/Release/net10.0/publish/`, is already covered by `[Bb]in/` in `.gitignore`; `./publish/`
   is **not** ignored (the `# Publish Web Output` heading covers `*.pubxml` / `*.publishproj` /
   `*.publishsettings` — publish *settings*, not publish *output*). Zip the contents **flat**,
   with a trailing `*` so entries sit at the archive root — a nested zip deploys *successfully*
   and then 503s:
   `Compress-Archive -Path bin/Release/net10.0/publish/* -DestinationPath bin/publish.zip -Force`
   `az webapp deploy -g <rg> -n tenexcards --src-path ./bin/publish.zip --type zip --track-status true`
   Leave `WEBSITES_PORT` unset (custom containers only — Oryx exports `ASPNETCORE_URLS=http://*:$PORT`,
   confirmed in logs as `Now listening on: http://[::]:8080`) and `SCM_DO_BUILD_DURING_DEPLOYMENT`
   unset (we ship compiled binaries).
5. **Provision the database** using the Azure SQL free offer (GA), then set the connection
   string as an app setting referencing Key Vault rather than inline. Measure cold-resume
   latency against the 2-second acknowledgement budget before committing to it.
6. **Verify the circuit end to end**: load the app, submit a passage, and confirm the
   acknowledgement lands under 2s with progress visible for the full generation window. Run
   `az webapp log tail` in a second terminal while doing it. Enable logging *before* deploying so
   first startup is captured; note `--level` only takes effect alongside `--application-logging`:
   `az webapp log config -g <rg> -n <app> --docker-container-logging filesystem --application-logging filesystem --level information`

## Out of Scope

The following were not evaluated in this research:

- Docker image configuration
- CI/CD pipeline setup
- Production-scale architecture (multi-region, HA, DR)
