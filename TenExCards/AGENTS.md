# Repository Guidelines

10xCards is a `net10.0` C# web app that turns a passage the learner pastes into flashcard
candidates they triage one at a time. Product spec: `@../context/foundation/prd.md`.
Stack rationale: `@../context/foundation/tech-stack.md`. Deployment:
`@../context/foundation/infrastructure.md`.

## The scaffold is not the target

**Delete this whole section once Blazor Server and Identity are in place** — it describes a
transient state and goes stale the moment it is acted on.

This directory is still unmodified `dotnet new webapi` output — minimal APIs and a
`WeatherForecast` sample in `@Program.cs`. It does not reflect the chosen architecture.
Delete the sample when you first touch it, and build toward:

- **Blazor Server** for the UI — not minimal APIs, not a separate SPA. One project, no
  hand-maintained API contract.
- **ASP.NET Core Identity** for email + password accounts. Not scaffolded.
- An **LLM client** for generation. Not scaffolded; no package or configuration exists.

Adding the above is expected work, not scope creep. **Persistence is undecided** — EF Core
is named in the stack rationale only as an ASP.NET Core ecosystem strength, and no database
provider was ever chosen. Ask before picking one.

## Never do these

- **Never persist a submitted passage.** It is unrecoverable once its candidates exist.
- **Never persist untriaged candidates, or add batch resume.** They are discarded when the
  session ends.
- **Never add password recovery.** A forgotten password is a dead account in v1.
- **Never add roles, sharing, admin views, decks, tags, or export.** The user model is flat;
  scope every query to the owning account.
- **Never let the form freeze.** Acknowledge a submission within 2s with continuous visible
  progress; generation is bounded at 30s.
- **Never use FluentAssertions.** Assertions use AwesomeAssertions; see `## Testing`.
- **Never run `az webapp up`.** It is deprecated. Deploy with
  `az webapp deploy --src-path <zip> --type zip`.
- **Never provision the F1 App Service tier.** It caps WebSockets at 5 per instance — five
  concurrent Blazor circuits — and has no Always On. B1 Linux is the floor.
- **Never set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=false`, and never add `ASPNETCORE_HTTPS_PORT`.**
  The Linux container already supplies `X-Forwarded-Proto`, so `Request.IsHttps` is correct and
  `UseHttpsRedirection()` finds no port and no-ops. Empirically verified 2026-08-31 against the
  live app: `ASPNETCORE_HTTPS_PORT=443` **alone** yields `200` and no redirect; that port **plus**
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED=false` yields `307` whose `Location` is the request's own
  URL — an infinite loop, i.e. `ERR_TOO_MANY_REDIRECTS`. It takes **both**; the port by itself is
  not the hazard. HTTPS is enforced by `--https-only` at the platform, which makes
  `UseHttpsRedirection()` in `@Program.cs` redundant — removing it is preferable to configuring
  around it. Note the startup log line
  `HttpsRedirectionMiddleware[3] Failed to determine the https port for redirect`: it appears at
  startup from App Service's internal plain-HTTP warm-up probe, **not** once per user request, and
  is not a defect.
- **Never run `az deployment group create` with `--mode Complete`.** It deletes every resource
  in the group absent from the template — the plan and web app included. Incremental is the
  default and the only mode used here. Preview with `az deployment group what-if` first.
- **Never declare `appSettings` in `@../infra/main.bicep`.** Declaring it makes the template
  authoritative and a routine, successful-looking deployment then deletes every setting applied
  out of band — the LLM API key and connection string among them. Secrets are set with
  `az webapp config appsettings set` / Key Vault references and stay outside IaC on purpose.
- **Never dismiss an `az deployment group what-if` deletion as noise.** The output carries a
  "may contain false positive predictions" banner and on `Microsoft.Web/*` it earns it:
  `siteConfig.localMySqlEnabled` and `siteConfig.netFrameworkVersion` are permanent phantoms
  that change nothing. But on 2026-08-31 a predicted `- properties.freeOfferExpirationTime` on
  the plan was **real** — a deployment reporting `Succeeded` cleared it, and no `az` command
  restores it. Nothing in the output separates the real deletion from the phantoms. Snapshot
  (`az appservice plan show`, `az webapp config show`, `az webapp config appsettings list`),
  deploy, then diff.

These are recorded decisions, not omissions. The product rules trace to `## Non-Goals` and
`## Non-Functional Requirements` in `@../context/foundation/prd.md`; the deployment rules to
the risk register in `@../context/foundation/infrastructure.md`.

## Deployment

Live at `https://tenexcards-ka.azurewebsites.net` — resource group `rg-tenexcards-plc`, App
Service plan `asp-tenexcards-linux` (B1 Linux), **region `polandcentral`**. Declarative
equivalent in `@../infra/main.bicep`; what was actually run is in
`@../context/deployment/deploy-plan.md`.

The region is not the one `deploy-plan.md` opens with. West Europe is closed to new customers
and North Europe has zero B1 quota **on this Free Trial subscription** — both re-verified, both
requestable on a paid plan. Read them as constraints of this subscription, not as facts about
Azure. Note that `az appservice list-locations` named both regions anyway: it reports where a
SKU exists, not where you may deploy it, so a region is only proven by attempting a provision.

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
`## Business Logic` in `@../context/foundation/prd.md` before touching generation or triage.
A candidate must test one load-bearing claim, be reformulated rather than copied, admit one
defensible answer, and not duplicate another card in the set.

Commits so far track course milestones (`Completed M1L3`). Ask which convention to use for
product commits rather than extending that style.
