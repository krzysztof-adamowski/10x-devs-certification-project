# Interactive Blazor Server Shell — Plan Brief

> Full plan: `context/changes/blazor-server-shell/plan.md`

## What & Why

Roadmap item **F-01**. The `TenExCards/` project is still unmodified `dotnet new webapi` output —
minimal APIs and a `WeatherForecast` sample — which is not the chosen architecture and cannot host
a single product surface. This change replaces it with an interactive Blazor Server host and proves
a live circuit on the deployed B1 instance. It unlocks `S-01` through `S-05` and `F-03`; nothing
renders until it lands.

## Starting Point

`Program.cs` is the template scaffold: `AddOpenApi()`, `UseHttpsRedirection()`, one
`MapGet("/weatherforecast")`, one `WeatherForecast` record, and a single package reference. No
`Components/`, no `wwwroot/`, no Razor. The scaffold is currently **deployed and live** —
`/weatherforecast` returns 200 today — so this replaces a running app rather than deploying into a
vacuum. Toolchain verified: SDK 10.0.400, ASP.NET Core 10.0.11, Azure CLI 2.89.1.

## Desired End State

`https://tenexcards-ka.azurewebsites.net/` serves an interactive Blazor page over a live SignalR
circuit; a button click round-trips to the server with no page reload. `/weatherforecast` returns
404 — the scaffold is gone from production, not just from the working tree. Page-load,
circuit-establishment, and round-trip timings from the real B1 instance are on record, giving
`S-02` a measured budget instead of an assumption about the 2-second acknowledgement.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Scaffolding method | Generate to a scratch dir, then merge deliberately | Nothing in `TenExCards/` is overwritten by a tool without review — aimed squarely at F-01's named risk of carrying incidental scaffold decisions forward. |
| Render mode | Per-page opt-in (`@rendermode InteractiveServer`) | Matches Microsoft's own Blazor + Identity layout, so `S-01`'s sign-in pages need no carve-out — `SignInManager` writes cookies a circuit cannot — and static pages hold no circuit memory. |
| Deploy scope | Deploy and verify live | F-01's outcome is about the *deployed* app, and the roadmap states the 2s requirement cannot be measured against a local run. |
| Template chrome | Prune, keep one proof page | The renamed Counter is the only thing this change can actually verify, and it doubles as the worked example of the per-page annotation. |
| HTTPS redirect (Open Q4) | Remove `UseHttpsRedirection()` | `infra/main.bicep:96` declares `httpsOnly: true`, so platform enforcement lives in the infrastructure source of truth rather than a drifting CLI flag. |
| HSTS | Keep `UseHsts()` | Complementary to `httpsOnly`, not redundant — the platform redirects after a plain-HTTP request is made, HSTS stops the browser making it. |
| Bootstrap | Ship only `bootstrap.min.css` | The template ships 44 files / 8.4 MB; `App.razor` references exactly one of them. |
| Commit convention | Conventional Commits | The repo interleaves course milestones with product work, and a prefix separates them at a glance. |
| Measurement | Record a baseline, don't instrument | Ongoing telemetry beyond platform logs is explicitly Parked; a one-off reading still gives `S-02` a floor. |

## Scope

**In scope:** Blazor Server host and component tree · pipeline rewrite in `Program.cs` · removal of
the weather route, `WeatherForecast`, OpenAPI package, and `TenExCards.http` · Bootstrap pruning ·
regenerated `launchSettings.json` · publish, flat-zip, deploy · live verification and baseline
timings · corrections to `AGENTS.md`, `deploy-plan.md`, and the roadmap's open question.

**Out of scope:** Identity (`S-01`) · database and EF Core (`F-02`) · LLM client and triage (`S-02`) ·
test project (arrives with `S-01`) · GitHub Actions pipeline (`F-03`) · health endpoint and
telemetry (Parked) · any change to `infra/main.bicep` · design and styling work.

## Architecture / Approach

One ASP.NET Core project hosting Razor components. `Program.cs` wires
`AddRazorComponents().AddInteractiveServerComponents()` and
`MapRazorComponents<App>().AddInteractiveServerRenderMode()`; pages render statically unless
individually marked interactive. Requests hit App Service Linux, which terminates TLS and supplies
`X-Forwarded-Proto`; interactive pages then hold a SignalR circuit over WebSockets, which are always
enabled on Linux App Service. Deployment stays the manual publish → flat zip → `az webapp deploy`
path until `F-03` automates it.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Blazor host replaces the scaffold | A working circuit on localhost, scaffold fully removed | Merge misses a template file, or the prune removes something `App.razor` references |
| 2. Deploy and record the baseline | Live interactive page on B1, timings on record | Nested zip — deploys "successfully" then 503s; the pre-upload assertion is what catches it |
| 3. Update the repo's own record | `AGENTS.md`, `deploy-plan.md`, roadmap made true again | Leaving stale instructions that misdirect the next agent |

**Prerequisites:** None. .NET 10 SDK, Azure CLI, and an authenticated `az` session are all present;
the target infrastructure is already provisioned and live.
**Estimated effort:** ~1-2 sessions across 3 phases. Phase 1 is the bulk; phases 2 and 3 are short.

## Open Risks & Assumptions

- **Per-page opt-in trades one failure mode for another.** A forgotten `@rendermode` produces a
  dead form, against a launch-gating "never freeze" NFR. The mitigation is that `CircuitCheck.razor`
  stays in the tree as the visible pattern until `S-02` replaces it.
- **The recorded baseline is a floor, not a pass.** No generation call exists yet, so a green
  number here does not mean `S-02` will meet the 2-second requirement.
- **Removing `UseHttpsRedirection()` couples HTTPS posture to `httpsOnly` staying declared.** The
  Bicep declaration makes that a reviewable change rather than silent drift, but it is real.
- **Deploying replaces a live app with no slot to roll back to** — B1 has no deployment slots, so
  recovery means redeploying the prior archive. Phase 2 step 1 preserves that archive before the
  publish overwrites it; without that step the rollback would not exist, since Phase 1 has already
  deleted the source it was built from.
- **Assumed:** `az` remains authenticated and the Free Trial credit still covers the B1 plan; the
  subscription's spending limit means exhaustion disables resources rather than billing.

## Success Criteria (Summary)

- Loading `https://tenexcards-ka.azurewebsites.net/` shows a real page, and clicking the
  circuit-check button updates it without a reload — proving a Blazor Server circuit works on B1.
- `/weatherforecast` returns 404 and no `WeatherForecast`, OpenAPI, or HTTPS-redirect code remains
  anywhere in the project.
- A future agent reading `AGENTS.md` finds no instruction contradicting the code, and the two
  questions this change settled are recorded as settled.
