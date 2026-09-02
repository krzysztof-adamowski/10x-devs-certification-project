# Repository Guidelines

10xCards is a `net10.0` C# web app that turns a passage the learner pastes into flashcard
candidates they triage one at a time. Product spec: `../context/foundation/prd.md`.
Stack rationale: `../context/foundation/tech-stack.md`. Deployment: `## Deployment` below —
platform research and the risk register are in `../context/foundation/infrastructure.md`.

## The scaffold is not the target

**Delete this whole section once Blazor Server and Identity are in place** — it describes a
transient state and goes stale the moment it is acted on.

This directory is still unmodified `dotnet new webapi` output — minimal APIs and a
`WeatherForecast` sample in `Program.cs`. It does not reflect the chosen architecture.
Delete the sample when you first touch it, and build toward:

- **Blazor Server** for the UI — not minimal APIs, not a separate SPA. One project, no
  hand-maintained API contract.
- **ASP.NET Core Identity** for email + password accounts. Not scaffolded.
- An **LLM client** for generation. Not scaffolded; no package or configuration exists.

Adding the above is expected work, not scope creep. **Persistence is undecided** — EF Core
is named in the stack rationale only as an ASP.NET Core ecosystem strength, and no database
provider was ever chosen. Ask before picking one.

When it is picked, the rule is **the cheapest option that removes a risk or saves real time —
not the cheapest option.** The subscription is a Free Trial with `spendingLimit: On`, so the
ceiling is a hard stop rather than a bill and a paid tier inside the credit costs nothing extra;
unspent credit expires worthless. Prefer a provisioned tier over an auto-pausing one — the first
query after a pause can exceed the whole 2s acknowledgement budget. Out of scope either way:
anything whose *ongoing* cost matters after the trial. Full reasoning in
`../context/foundation/infrastructure.md` under `## Budget Posture`.

## Never do these

Product:

- **Never persist a submitted passage.** It is unrecoverable once its candidates exist.
- **Never persist untriaged candidates, or add batch resume.** They are discarded when the
  session ends.
- **Never add password recovery.** A forgotten password is a dead account in v1.
- **Never add roles, sharing, admin views, decks, tags, or export.** The user model is flat;
  scope every query to the owning account.
- **Never let the form freeze.** Acknowledge a submission within 2s with continuous visible
  progress; generation is bounded at 30s.
- **Never hold more in a circuit than you must.** Blazor Server memory is per-user — roughly
  250 KB per circuit *before* application state — and this app deliberately keeps a whole passage
  plus its candidates there until triage ends. B1 gives 1.75 GB total and there is no
  back-pressure: the ceiling arrives as OOM restarts that look like random disconnects, and every
  restart drops every circuit. Enforce the passage-length bound *before* generation begins.
- **Never use FluentAssertions.** Assertions use AwesomeAssertions; see `## Testing`.

Deployment — each bullet is the rule; `## Deployment` holds what an agent will encounter and
could misread: silent failure modes, false positives, and the exact strings they appear as.
A new fact goes in one or the other, never both:

- **Never run `az webapp up`.** It is deprecated. Deploy with
  `az webapp deploy --src-path <zip> --type zip`.
- **Never zip the publish folder itself.** Assert the archive's first entry is `TenExCards.dll`,
  not `publish/TenExCards.dll`. The trailing `*` in `Compress-Archive -Path <publish>/*` is
  load-bearing; a nested zip deploys **successfully** and then 503s.
- **Never provision the F1 App Service tier.** It caps WebSockets at five connections and has no
  Always On. B1 Linux is the floor.
- **Never ship Identity without persisting Data Protection keys.** Keys are **not** persistent
  by default — a warning appears at startup. This bites at **one** instance, not just when
  scaling: every container restart (deploy, platform maintenance, recycle) rotates the key ring,
  logging users out and rejecting antiforgery tokens. Configure persistence in the same change
  that adds Identity. See `## Deployment`.
- **Never set `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, and never add `ASPNETCORE_HTTPS_PORT`.**
  Together they produce an infinite redirect. The platform already supplies `X-Forwarded-Proto`
  and enforces HTTPS, so neither is needed.
- **Never run `az deployment group create` with `--mode Complete`.** It deletes every resource
  in the group absent from the template — the plan and web app included. Incremental is the
  default and the only mode used here.
- **Never declare `appSettings` in `infra/main.bicep`.** It makes the template authoritative, so
  a routine, successful-looking deployment then deletes the LLM API key and connection string.
  Secrets stay outside IaC, set with `az webapp config appsettings set` / Key Vault references.
- **Never dismiss an `az deployment group what-if` deletion as noise.** Snapshot, deploy, then
  diff. Some predictions on `Microsoft.Web/*` are phantoms and at least one was real; nothing in
  the output tells them apart.

These are recorded decisions, not omissions. The product rules trace to `## Non-Goals` and
`## Non-Functional Requirements` in `../context/foundation/prd.md`; the deployment rules to
the risk register in `../context/foundation/infrastructure.md`.

## Deployment

Live at `https://tenexcards-ka.azurewebsites.net` — resource group `rg-tenexcards-plc`, App
Service plan `asp-tenexcards-linux` (B1 Linux), **region `polandcentral`**. `infra/main.bicep`
is the source of truth for the infrastructure; what was actually run is in
`../context/deployment/deploy-plan.md`.

The region is not the one `deploy-plan.md` opens with. West Europe is closed to new customers
and North Europe has zero B1 quota **on this Free Trial subscription** — both re-verified, both
requestable on a paid plan. Read them as constraints of this subscription, not as facts about
Azure. Note that `az appservice list-locations` named both regions anyway: it reports where a
SKU exists, not where you may deploy it, so a region is only proven by attempting a provision.

### Scaling past one worker

**ARR session affinity** (`clientAffinityEnabled`, on by default and declared in
`infra/main.bicep`) is **cookie-based**. A client that blocks cookies gets routed at random, and
a Blazor circuit's state lives in memory on exactly one instance. This cannot manifest at one
worker, so it surfaces first under load. Test affinity with cookies disabled before trusting it.

**Data Protection keys are a separate concern, and it is not a scaling concern.** Keys are not
persisted by default — the app logs a warning about it at startup. Because the key ring is lost
on every container restart, auth cookies and antiforgery tokens break at **one** instance too,
on any deploy or platform recycle. Scaling out only adds a second way to hit the same failure.
Persist the keys when Identity lands, not when the worker count changes.

### HTTPS: why neither environment variable belongs here

The Linux .NET container supplies `X-Forwarded-Proto`, so `Request.IsHttps` is already correct
and `UseHttpsRedirection()` finds no port and no-ops. Setting the two variables together produces
a `307` to the request's own URL — an infinite redirect; the port alone is harmless. Measurements
are in `../context/deployment/deploy-plan.md` under `## Deliberately not set`.

HTTPS is enforced by `--https-only` at the platform, which makes `UseHttpsRedirection()` in
`Program.cs` redundant. **Whether to delete it is undecided** — it is still present. Removing it
couples the app's HTTPS posture to `--https-only` staying on in every environment it is ever
deployed to, so make that a deliberate change, not a drive-by.

The startup line `HttpsRedirectionMiddleware[3] Failed to determine the https port for redirect`
comes from App Service's internal plain-HTTP warm-up probe. It appears once at startup, **not**
once per user request, and is not a defect.

### what-if: telling a real deletion from a phantom

The output carries a "may contain false positive predictions" banner, and on `Microsoft.Web/*`
it earns it. `siteConfig.localMySqlEnabled` and `siteConfig.netFrameworkVersion` are permanent
phantoms that change nothing. But on 2026-08-31 a predicted
`- properties.freeOfferExpirationTime` on the plan was **real**: a deployment reporting
`Succeeded` cleared it, and no `az` command restores it.

Nothing in the output separates the two. Snapshot (`az appservice plan show`,
`az webapp config show`, `az webapp config appsettings list`), deploy, then diff.

## Working in this directory

Agent sessions are rooted at the repo root, so `dotnet` needs `TenExCards/` — `cd` here
or pass `--project TenExCards/TenExCards.csproj`.

## Testing

No test project exists. Create `TenExCards.Tests` (xUnit) with the first feature that touches
generation, triage, or persistence, and put the test in the same change as the code.
**Move this section to `TenExCards.Tests/AGENTS.md` once that project exists** — these rules
belong next to the tests. Leave the FluentAssertions bullet in `## Never do these`.

Assertions use **AwesomeAssertions**. FluentAssertions v8 moved to a paid commercial licence;
AwesomeAssertions is the Apache-2.0 fork of v7 with the same API, so the training-data reflex
compiles cleanly and introduces a licensing problem silently.

Test the deterministic rules, never the model's prose. Stub the LLM client — it is the only
test double — and assert on what the code does with a response: over-length submissions are
refused before generation begins, duplicate candidates are dropped, each candidate is triaged
exactly once, and every query is scoped to the owning account. Card quality is judged by the
learner at triage, not by a test; an assertion against generated card text is a flaky test,
not a quality gate.

## Conventions

Card quality is the product, and it is specified rather than delegated: read
`## Business Logic` in `../context/foundation/prd.md` before touching generation or triage.
A candidate must test one load-bearing claim, be reformulated rather than copied, admit one
defensible answer, and not duplicate another card in the set.

Commits so far track course milestones (`Completed M1L3`). Ask which convention to use for
product commits rather than extending that style.
