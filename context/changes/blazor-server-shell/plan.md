# Interactive Blazor Server Shell Implementation Plan

## Overview

Replace the untouched `dotnet new webapi` scaffold in `TenExCards/` with an interactive Blazor
Server host, prune the template's incidental decisions rather than inheriting them, and prove a
live circuit on the deployed B1 Linux instance — recording a latency baseline that gives `S-02`
a real budget to work against.

This is roadmap item **F-01** (`context/foundation/roadmap.md`). It establishes the host and
nothing else. Every later slice builds its own surface on top.

## Current State Analysis

`TenExCards/` is 100% unmodified `dotnet new webapi` output, and it is currently deployed and
serving:

- `TenExCards/Program.cs` — `AddOpenApi()`, a Development-only `MapOpenApi()`,
  `UseHttpsRedirection()`, one `MapGet("/weatherforecast")`, and a `WeatherForecast` record.
- `TenExCards/TenExCards.csproj` — `net10.0`, `Nullable`/`ImplicitUsings` enabled, one package
  reference: `Microsoft.AspNetCore.OpenApi` 10.0.11.
- `TenExCards/TenExCards.http` — a single GET against the route being deleted.
- No `Components/`, no `wwwroot/`, no Razor of any kind.

Toolchain verified locally: .NET SDK **10.0.400**, `Microsoft.AspNetCore.App` **10.0.11**,
Azure CLI **2.89.1**.

Live state verified at planning time: `https://tenexcards-ka.azurewebsites.net/weatherforecast`
returns **200**. The scaffold deployment from 2026-08-31 is still up, so this change replaces a
running app rather than deploying into a vacuum.

### Key Discoveries:

- **The .NET 10 Blazor Web App template ships `app.UseHttpsRedirection()` itself.** Removing it
  is therefore a deliberate deletion from freshly generated code, not merely a carry-over from
  the old scaffold. Verified by generating the template into a scratch directory.
- **The template also adds `app.UseHsts()`** in the non-Development branch — production
  behaviour the current app does not have. Kept; see Critical Implementation Details.
- **`ReconnectModal.razor` is a first-class component in .NET 10** (`Components/Layout/`, with its
  own `.css` and `.js`), where earlier versions inlined the reconnect UI in `App.razor`. This is
  the "never let the form freeze" NFR arriving pre-built.
- **Bootstrap ships as 44 files / 8.4 MB**, of which `Components/App.razor` references exactly
  one: `lib/bootstrap/dist/css/bootstrap.min.css` (~230 KB). The other 43 are RTL, grid,
  utilities, ESM and sourcemap variants that nothing loads.
- **`Counter.razor` already carries `@rendermode InteractiveServer`** — under per-page opt-in it
  is simultaneously the interactivity proof and the worked example of the annotation every later
  interactive page must carry.
- **The template csproj has zero `PackageReference` elements.** Dropping
  `Microsoft.AspNetCore.OpenApi` leaves the project with no NuGet dependencies at all.
- **New in .NET 10 and carried forward**: `<ResourcePreloader />`, `<ImportMap />`,
  `@Assets["..."]`, `app.MapStaticAssets()`, `Router NotFoundPage=`,
  `UseStatusCodePagesWithReExecute("/not-found")`, and the
  `<BlazorDisableThrowNavigationException>` csproj property.
- **`infra/main.bicep:96` declares `httpsOnly: true`** — platform HTTPS enforcement lives in the
  infrastructure source of truth, not just in a CLI flag someone once typed. This is what makes
  removing `UseHttpsRedirection()` safe rather than a coupling gamble.
- **`clientAffinityEnabled: true` at `infra/main.bicep:107`** — cookie-based ARR affinity is
  already declared. Irrelevant at one worker; recorded so `F-03` and any scale-out work does not
  rediscover it.

## Desired End State

`https://tenexcards-ka.azurewebsites.net/` serves an interactive Blazor Server page over a live
SignalR circuit. Clicking the circuit-check button increments a server-held counter with no page
reload. `/weatherforecast` returns 404 — the scaffold is gone from production, not merely from the
working tree. The startup log no longer contains the `HttpsRedirectionMiddleware[3]` warning.
Page-load, circuit-establishment and round-trip timings from the live B1 instance are recorded in
`context/changes/blazor-server-shell/baseline.md`, which `context/deployment/deploy-plan.md`
points to.

Verify by: loading the live root URL, clicking the button, observing the `_blazor` WebSocket in
browser devtools, and `curl.exe` returning 404 for `/weatherforecast`.

## What We're NOT Doing

- **No ASP.NET Core Identity.** That is `S-01` (`accounts-and-sessions`), and it brings the Data
  Protection key-persistence requirement with it.
- **No database, no EF Core, no connection string.** That is `F-02` (`persistence-spine`), which
  runs in parallel with this change.
- **No LLM client, no generation, no triage UI.** That is `S-02`.
- **No test project.** `TenExCards/AGENTS.md` scopes `TenExCards.Tests` to the first change that
  touches generation, triage, or persistence, and the roadmap places it in `S-01`. This change
  touches none of the three, and a test asserting that a Counter increments tests the framework.
- **No GitHub Actions pipeline.** That is `F-03` (`deploy-pipeline`), which this change unblocks.
- **No health endpoint, no telemetry, no Application Insights.** "Telemetry beyond the platform's
  own logs" is explicitly Parked on the roadmap.
- **No `ASPNETCORE_HTTPS_PORT` and no `ASPNETCORE_FORWARDEDHEADERS_ENABLED`.** Together they
  produce an infinite redirect; the platform already supplies `X-Forwarded-Proto`.
- **No changes to `infra/main.bicep`.** No infrastructure change is required — the app's runtime
  contract (framework-dependent `net10.0` on `DOTNETCORE|10.0`) is unchanged.
- **No custom styling or design work.** The template's Bootstrap layout is scaffolding for later
  slices to replace, not a design decision being made here.

## Implementation Approach

Generate the template into a scratch directory, then copy and merge deliberately — nothing in
`TenExCards/` is overwritten by a tool without review. This is chosen specifically against F-01's
named secondary risk: *"carrying the scaffold's incidental decisions forward untouched while
rewriting around them."* Each file that lands does so because someone decided it should.

Render mode is **per-page opt-in** (the template default): pages are static-rendered unless marked
`@rendermode InteractiveServer`. This matches Microsoft's own Blazor Web App + Identity layout, so
`S-01`'s sign-in pages need no carve-out — `SignInManager` must write cookies to the HTTP response,
which a circuit cannot do. It also keeps circuit memory off pages that do not need it, which
matters against the 1.75 GB B1 ceiling that `TenExCards/AGENTS.md` warns about.

The change then deploys by the manual path already recorded in `context/deployment/deploy-plan.md`,
because F-01's outcome is about the *deployed* app and the roadmap states the 2s acknowledgement
"cannot be measured against a local run."

## Critical Implementation Details

**`UseHsts()` is kept, and that is a decision rather than an inheritance.** The reasoning — *not* a
measured fact — is that HSTS and platform `httpsOnly` address different moments: `httpsOnly`
redirects at the edge after a plain-HTTP request has already been made, while HSTS instructs the
browser not to make that request next time. ASP.NET Core's default HSTS options exclude
`localhost`, so local development is unaffected, and the header scopes to the exact host without
`includeSubDomains`.

**Unverified**: whether `azurewebsites.net` is already covered by browser HSTS preloading, which
would make the header a no-op on *this* hostname while still mattering on a custom domain later.
Nobody has checked, and the decision to keep it does not depend on the answer — but this repo
separates measured facts from reasoning, so it is flagged rather than asserted. One practical
consequence either way: the default 30-day `max-age` means plain HTTP cannot be tested against
that hostname for 30 days after the first response. This is new production behaviour compared to
today's scaffold and should be recorded as such.

**`curl` is an alias for `Invoke-WebRequest` in Windows PowerShell 5.1.** Every verification
command below must call `curl.exe` explicitly, or the `-o` / `-w` flags are misparsed as
`Invoke-WebRequest` parameters and fail. `-SkipHttpErrorCheck` is PowerShell 7+ only and is not
available here — this already bit the 2026-08-31 deploy (deviation 4 in `deploy-plan.md`).

**The trailing `*` in `Compress-Archive -Path <publish>/*` is load-bearing.** A nested zip deploys
*successfully* and then 503s at runtime, which is the nastiest failure mode available on this
platform. Assert the archive's first entry is `TenExCards.dll`, not `publish/TenExCards.dll`,
before uploading — not after the deploy reports success.

**Ordering: publish before zipping, and assert before deploying.** The zip is built from
`bin/Release/net10.0/publish/`, which `TenExCards/.gitignore:12:[Bb]in/` already ignores. Do not
add `-o ./publish` — that writes to a path git does *not* ignore, as recorded in `deploy-plan.md`.

**Blazor's `_blazor` WebSocket is the only honest proof of interactivity.** A rendered page proves
static SSR worked; only a server round-trip after page load proves the circuit exists. WebSockets
are always enabled on App Service Linux, so no site configuration is needed.

## Phase 1: Blazor Server host replaces the API scaffold

### Overview

Turn the project into a Blazor Web App with per-page interactivity, delete every trace of the
webapi scaffold, and prune the template's unreferenced weight. Ends with a working circuit on
localhost. This phase is deliberately not split — the pipeline rewrite and the component tree must
land together or the project does not build.

### Changes Required:

#### 1. Scratch generation (no repo files touched)

**File**: scratch directory outside the repo

**Intent**: Produce a canonical .NET 10 Blazor Web App to copy from, so the merge is a review of
real template output rather than a reconstruction from memory.

**Contract**: `dotnet new blazor -n TenExCards -int Server` — server interactivity, per-page
opt-in (no `-ai`), sample pages retained (no `-e`), no auth (`-au None` is the default). The
project name must be `TenExCards` so generated namespaces match the existing project.

#### 2. Component tree

**File**: `TenExCards/Components/**`

**Intent**: Bring in the Blazor host, router, layout, and reconnect UI, dropping the sample pages
that demonstrate nothing this app needs.

**Contract**: Copy `App.razor`, `Routes.razor`, `_Imports.razor`, `Layout/MainLayout.razor(.css)`,
`Layout/NavMenu.razor(.css)`, `Layout/ReconnectModal.razor(.css|.js)`, `Pages/Home.razor`,
`Pages/Error.razor`, `Pages/NotFound.razor`. **Do not copy** `Pages/Weather.razor` — it exists to
demo streaming rendering against sample data and would be a second scaffold to delete later.

> **Added during implementation (2026-09-08).** `MainLayout.razor` ships a top-row `About` anchor
> pointing at `https://learn.microsoft.com/aspnet/core/`. Removed: a link to Microsoft's docs in
> the product's own header is exactly the incidental template decision this phase exists to prune,
> and it was invisible in the file list — it only surfaced when the page was rendered in a
> browser. The empty `top-row` div is left in place for a later slice to fill.

#### 3. Circuit-check page

**File**: `TenExCards/Components/Pages/CircuitCheck.razor`

**Intent**: Provide the one thing this change can actually verify — a server round-trip proving the
circuit is live — under a name that states its purpose, so it does not read as leftover sample code
to the next agent.

**Contract**: The template's `Counter.razor` renamed, routed at `@page "/circuit-check"`, keeping
its `@rendermode InteractiveServer` line verbatim. Add a one-line comment recording that it exists
to prove the circuit and **is deleted by `S-01`** — once real interactive surfaces exist the proof
page has no job, and `S-01` is the slice that adds an auth boundary this page would otherwise sit
outside of, publicly reachable. (It exposes only a counter, so this is hygiene rather than a hole —
but the deletion trigger belongs on the slice that changes the security model, not on `S-02`.) The
`@rendermode` line is the worked example of the annotation every later interactive page must carry
under per-page opt-in.

#### 4. Static assets

**File**: `TenExCards/wwwroot/**`

**Intent**: Bring in the stylesheet and favicon the template's `App.razor` actually references,
without committing 8.4 MB of Bootstrap variants nothing loads.

**Contract**: Copy `app.css`, `favicon.png`, and `lib/bootstrap/dist/css/bootstrap.min.css` only.
Omit the remaining 43 Bootstrap files (RTL, grid, utilities, reboot, ESM bundles, and all
sourcemaps). `App.razor` references exactly one Bootstrap path, so nothing else is reachable; a
sourcemap request from an open devtools panel 404s harmlessly. If a later slice needs a Bootstrap
variant, it is one file to restore.

#### 5. Application pipeline

**File**: `TenExCards/Program.cs`

**Intent**: Replace the minimal-API pipeline wholesale with the Blazor Server pipeline, dropping
OpenAPI, the weather route, the `WeatherForecast` record, and the HTTPS redirect.

**Contract**: The template's `Program.cs` verbatim **minus** `app.UseHttpsRedirection()`. Retains
`AddRazorComponents().AddInteractiveServerComponents()`, the non-Development
`UseExceptionHandler("/Error", createScopeForErrors: true)` + `UseHsts()` branch,
`UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true)`,
`UseAntiforgery()`, `MapStaticAssets()`, and
`MapRazorComponents<App>().AddInteractiveServerRenderMode()`. Requires
`using TenExCards.Components;`. No `WeatherForecast` type survives anywhere in the project.

#### 6. Project file

**File**: `TenExCards/TenExCards.csproj`

**Intent**: Drop the now-purposeless OpenAPI package and adopt the template's Blazor property.

**Contract**: Keep `Microsoft.NET.Sdk.Web`, `net10.0`, `Nullable`, `ImplicitUsings`. Add
`<BlazorDisableThrowNavigationException>true</BlazorDisableThrowNavigationException>`. Remove the
`Microsoft.AspNetCore.OpenApi` `PackageReference` and the now-empty `ItemGroup` — the project ends
with **zero** package references.

#### 7. Navigation

**File**: `TenExCards/Components/Layout/NavMenu.razor`

**Intent**: Point the nav at the pages that exist.

**Contract**: Keep the brand link and the Home `NavLink`. Delete the `weather` `NavLink`. Repoint
the `counter` `NavLink` to `circuit-check` with a matching label.

#### 8. Home page

**File**: `TenExCards/Components/Pages/Home.razor`

**Intent**: Replace "Hello, world!" so the deployed root does not read as an unconfigured template.

**Contract**: Static-rendered (no `@rendermode`), routed at `/`. Names the product and states that
the shell is in place. Deliberately minimal — `S-02` owns this route's real content.

#### 9. Launch profiles

**File**: `TenExCards/Properties/launchSettings.json`

**Intent**: Adopt the template's profiles, replacing the webapi scaffold's.

**Contract**: The generated file verbatim. Note the local ports change from `5158`/`7270` to the
template's `5218`/`7209`, and `launchBrowser` becomes `true`. Nothing in the repo references the
old ports once `TenExCards.http` is deleted.

#### 10. Scaffold artifact removal

**File**: `TenExCards/TenExCards.http`

**Intent**: Delete a file whose entire content is a GET against a route that no longer exists.

**Contract**: File removed. Nothing references it.

### Success Criteria:

#### Automated Verification:

- Release build succeeds: `dotnet build TenExCards/TenExCards.csproj -c Release`
- No scaffold identifiers remain:
  `grep -rn "WeatherForecast\|AddOpenApi\|MapOpenApi\|app\.UseHttpsRedirection()" TenExCards/ --include=*.cs --include=*.csproj --include=*.razor`
  returns nothing. **The type filter is load-bearing, not tidiness**: `TenExCards/AGENTS.md`
  legitimately discusses all four identifiers (lines 14, 115, 119), Phase 3 deliberately keeps a
  `UseHttpsRedirection()` mention there, and stale `bin/`/`obj/` binaries match as well — an
  unfiltered grep can never return nothing, so it would fail on a correct implementation.

  > **Adapted during implementation (2026-09-08).** The fourth term was widened from the bare
  > identifier to the `app.UseHttpsRedirection()` *call*. `Program.cs` carries a deliberate
  > comment naming the middleware to explain why it is absent — AGENTS.md warns that removing it
  > couples HTTPS posture to the platform setting, so an unexplained absence invites a future
  > agent to re-add it. The gate's intent is "no scaffold **code** remains"; matching the call
  > tests that, while matching the bare word also flags the comment that exists to prevent a
  > regression. Same class of defect as F1 in the plan review, found one level deeper.
- `TenExCards/TenExCards.http` and `TenExCards/Components/Pages/Weather.razor` do not exist
- The csproj contains zero `PackageReference` elements
- `wwwroot/lib/bootstrap` contains exactly one file
- App starts, the root route returns 200 locally, **and every `.css`/`.js` URL the rendered page
  references also returns 200** — including the fingerprinted `_framework/blazor.web.js`, the
  scoped-CSS bundle `TenExCards.styles.css`, and `Components/Layout/ReconnectModal.razor.js`

  > **Adapted during implementation (2026-09-08).** The original criterion stopped at "root route
  > returns 200" and passed on a page whose every asset was returning 500 — a 200 on the HTML
  > shell proves almost nothing for a Blazor app, since the shell only contains *references* to
  > the scripts that make it work. Run the app with `ASPNETCORE_ENVIRONMENT=Development` (or
  > against `dotnet publish` output): `--no-launch-profile` alone leaves the environment unset,
  > which defaults to Production, where Static Web Assets are not wired up and every framework and
  > scoped-CSS asset 500s with `FileNotFoundException`. That is a harness artefact, not an
  > application defect — published output materializes these files into `wwwroot/`, verified.

#### Manual Verification:

- `/circuit-check` increments the counter on click with no page reload — the circuit is live
- The reconnect modal appears when the running server is stopped mid-session
- The nav shows Home and Circuit check, and no Weather link
- No new build warnings compared to the pre-change baseline

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before
proceeding to the next phase.

---

## Phase 2: Deploy and record the live baseline

### Overview

Publish, package with the archive-shape assertion, deploy to the existing B1 Linux app, and prove
the circuit works on the real platform. Capture timing numbers so `S-02` inherits a budget rather
than an assumption.

Target infrastructure is already provisioned and unchanged: resource group `rg-tenexcards-plc`,
plan `asp-tenexcards-linux` (B1 Linux, `polandcentral`), app `tenexcards-ka`.

### Changes Required:

#### 1. Preserve the rollback artifact — before anything else

**File**: `TenExCards/bin/publish-scaffold-rollback.zip` (git-ignored)

**Intent**: Keep a redeployable copy of what is currently live, because the next step destroys it.

**Contract**: Copy `TenExCards/bin/publish.zip` — 372,646 bytes, dated 2026-08-31 20:17, the exact
archive currently serving production — to `bin/publish-scaffold-rollback.zip` **before** running
publish or `Compress-Archive`. Step 3 writes `bin/publish.zip` with `-Force` and would otherwise
overwrite the only copy, while Phase 1 has already deleted the source it was built from. B1 has no
deployment slots, so this file is the entire rollback story. If it is ever lost, the rebuild point
is commit `035e064` — `git checkout 035e064 -- TenExCards/`, publish, zip, redeploy.

#### 2. Release publish

**File**: `TenExCards/bin/Release/net10.0/publish/` (build output, git-ignored)

**Intent**: Produce the framework-dependent deployable.

**Contract**: `dotnet publish -c Release` run from `TenExCards/`, with **no** `-o` flag — the
default output path is what `.gitignore` covers. Output must contain `TenExCards.dll`,
`TenExCards.runtimeconfig.json` requesting `Microsoft.AspNetCore.App` 10.0.x, and no native
executable or `*.so` pile (either would mean a self-contained publish, which the csproj rules out).

#### 3. Flat archive with pre-upload assertion

**File**: `TenExCards/bin/publish.zip` (git-ignored)

**Intent**: Package the publish output flat, and prove it is flat before anything is uploaded.

**Contract**: `Compress-Archive -Path (Join-Path $PUB '*') -DestinationPath $ZIP -Force`. The
trailing `*` is load-bearing. Then read the archive with
`[IO.Compression.ZipFile]::OpenRead($ZIP)` and assert the first entry is `TenExCards.dll`, not
`publish/TenExCards.dll`. **This assertion gates the deploy** — a nested archive deploys
successfully and then 503s, so a passing deploy command is not evidence of a correct archive.

#### 4. Deploy

**File**: n/a — Azure App Service `tenexcards-ka`

**Intent**: Push the archive to the running app.

**Contract**: `az webapp deploy -g rg-tenexcards-plc -n tenexcards-ka --src-path <zip> --type zip
--track-status true --enriched-errors true`. Never `az webapp up` (deprecated). If
`--track-status` hangs on "Pending", Ctrl+C and verify directly — a known Linux CLI issue where the
site is already live.

#### 5. Baseline measurement

**File**: `context/changes/blazor-server-shell/baseline.md` (new)

**Intent**: Record what the platform costs before any application logic exists, so `S-02` knows how
much of the 2-second acknowledgement budget is already spent.

**Contract**: Three numbers against the live host, recorded with the date and captured on a warm
instance (Always On is enabled, so a cold-start reading would not represent steady state):
`curl.exe -o NUL -w "%{time_starttransfer} %{time_total}"` against `/` for time-to-first-byte and
total page load; and, from browser devtools, the elapsed time from navigation start to the
`_blazor` WebSocket reaching open state, plus the round-trip latency of one circuit-check click.
These are a **floor**, not a pass on the NFR — no generation call exists yet.

`baseline.md` is the single home for these numbers, and Phase 3 points `deploy-plan.md` at it
rather than restating them. Deliberately **not** written into `plan.md`: free-form prose near the
`## Progress` block risks a malformed line in the region `/10x-implement` parses for execution
state. Keeping it in its own file also means the measurement survives if Phase 3 is dropped under
time pressure — which the roadmap explicitly anticipates for deploy-adjacent work.

### Success Criteria:

#### Automated Verification:

- `dotnet publish -c Release` completes and emits `TenExCards.dll` at the publish root
- The archive's first entry is `TenExCards.dll` (nested-zip assertion passes)
- `az webapp deploy` reports success
- Live root returns 200
- Live `/weatherforecast` returns 404 — the scaffold is gone from production
- TTFB and total-load timings captured for `/`
- `TenExCards/bin/publish-scaffold-rollback.zip` exists and is 372,646 bytes — the rollback
  artifact was preserved before `Compress-Archive -Force` overwrote `bin/publish.zip`

#### Manual Verification:

- `/circuit-check` increments on the live instance — a circuit works on B1 Linux
- The `_blazor` WebSocket is visible in devtools; establishment time recorded
- `az webapp log tail` shows no `HttpsRedirectionMiddleware[3]` warning at startup, confirming the
  removal took effect on the platform
- Round-trip click latency recorded

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before
proceeding to the next phase.

---

## Phase 3: Update the repo's own record

### Overview

Three documents make claims this change falsifies. Correcting them is part of the change, not
follow-up work — `TenExCards/AGENTS.md` is the first thing every future agent reads, and it
currently opens by describing the project as an unmodified webapi scaffold.

### Changes Required:

#### 1. Agent onboarding rules

**File**: `TenExCards/AGENTS.md`

**Intent**: Shrink the transient scaffold section to what is still true, and settle two questions
this change answered.

**Contract**: Four edits. (a) `## The scaffold is not the target` — its deletion condition is
"Blazor Server **and** Identity in place", and only Blazor Server has landed, so the section
shrinks rather than disappearing: drop the "unmodified `dotnet new webapi` output" framing and the
Blazor Server bullet; keep the Identity bullet, the LLM-client bullet, and the
undecided-persistence paragraph with its budget rule. (b) `### HTTPS: why neither environment
variable belongs here` — the sentence "**Whether to delete it is undecided** — it is still
present" is now false; record that `UseHttpsRedirection()` was removed, and that
`infra/main.bicep:96` declaring `httpsOnly: true` is what makes the platform the sole enforcement
point. (c) `## Conventions` — replace "Ask which convention to use for product commits" with the
settled answer: Conventional Commits. (d) Add `UseHsts()` as a recorded production behaviour so it
is not later mistaken for drift.

#### 2. Deployment record

**File**: `context/deployment/deploy-plan.md`

**Intent**: Record the second deployment, so this file stays the ground truth for what is live.

**Contract**: Append a dated record: what was deployed (Blazor Server shell replacing the weather
route), the resulting live behaviour (`/` 200, `/weatherforecast` 404), a pointer to
`context/changes/blazor-server-shell/baseline.md` for the measured timings rather than a copy of
them, and confirmation that the `HttpsRedirectionMiddleware[3]` warning is gone now
that the middleware is removed. Note that the "404 at `/`" entry under the existing "looks like a
failure but is not" list no longer applies — Always On's 5-minute ping to `/` now gets a real page.

#### 3. Roadmap open question

**File**: `context/foundation/roadmap.md`

**Intent**: Close the open question that was recorded as blocking this item.

**Contract**: Open Roadmap Question 4 (HTTPS redirect) is resolved: the application's redirect was
removed in favour of platform enforcement. Mark it resolved in place with the rationale, matching
how the PRD records resolved questions. Also clear the matching `Unknowns` entry on item `F-01`.
Do **not** set F-01's status to `done` — `/10x-archive` owns that transition.

### Success Criteria:

#### Automated Verification:

- `TenExCards/AGENTS.md` no longer contains the string "unmodified `dotnet new webapi` output"
- `TenExCards/AGENTS.md` no longer contains "Whether to delete it is undecided"
- `context/deployment/deploy-plan.md` contains a dated record for this deployment
- `context/foundation/roadmap.md` marks Open Roadmap Question 4 resolved

#### Manual Verification:

- Read `TenExCards/AGENTS.md` start to finish as a fresh agent would: no instruction contradicts
  the code, and the remaining scaffold section describes only Identity, the LLM client, and
  undecided persistence
- The commit convention stated in AGENTS.md matches the commits this change actually produced

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful.

---

## Testing Strategy

No test project is created by this change — see `## What We're NOT Doing`. `TenExCards/AGENTS.md`
scopes `TenExCards.Tests` to the first change touching generation, triage, or persistence, and the
roadmap places it in `S-01`. A test asserting a Counter increments would test the framework.

Verification here is the build, the deployed smoke checks, and the manual circuit checks listed per
phase.

### Manual Testing Steps:

1. Run locally; load `/`; confirm the layout renders and the nav has no Weather link.
2. Navigate to `/circuit-check`, click the button, confirm the count increments without a reload.
3. Stop the running server with the browser open; confirm the reconnect modal appears.
4. After deploying, repeat steps 1-2 against `https://tenexcards-ka.azurewebsites.net/`.
5. With devtools open on the live site, confirm the `_blazor` WebSocket reaches open state and
   record the elapsed time from navigation start.
6. Confirm `https://tenexcards-ka.azurewebsites.net/weatherforecast` returns 404.
7. Tail the live log and confirm no `HttpsRedirectionMiddleware[3]` warning at startup.

## Performance Considerations

The measured baseline from Phase 2 is the deliverable, not a target. Blazor Server holds per-user
state in a circuit — roughly 250 KB before application state — against B1's 1.75 GB with no
back-pressure, and the ceiling arrives as OOM restarts that look like random disconnects. Per-page
opt-in helps directly: a statically rendered page holds no circuit.

Default `CircuitOptions` are kept (100 retained disconnected circuits, 3-minute retention, roughly
25 MB worst case — comfortably inside headroom). Revisit only when a real surface holds real state,
which is `S-02`.

## Migration Notes

No data migrates — nothing persists yet. The deployment replaces a running app in place and B1 has
no deployment slots, so rollback is entirely manual: redeploy
`TenExCards/bin/publish-scaffold-rollback.zip`, the copy Phase 2 step 1 makes of the archive
currently serving production.

That copy is not optional. Phase 2 step 3 writes `bin/publish.zip` with `-Force`, and Phase 1 has
by then deleted the source it was built from — so without step 1 there is no artifact to roll back
to. If the copy is lost anyway, the recovery path is `git checkout 035e064 -- TenExCards/`, then
publish, zip and redeploy.

ARR session affinity (`clientAffinityEnabled: true`, `infra/main.bicep:107`) is cookie-based and
cannot manifest at one worker. Recorded, not acted on.

## References

- Roadmap item F-01: `context/foundation/roadmap.md`
- Change identity: `context/changes/blazor-server-shell/change.md`
- Deployment ground truth and the archive-shape trap: `context/deployment/deploy-plan.md`
- Repository rules and the never-do list: `TenExCards/AGENTS.md`
- NFRs driving the 2s and 30s bounds: `## Non-Functional Requirements` in `context/foundation/prd.md`
- Stack rationale for Blazor Server + Identity: `context/foundation/tech-stack.md`
- Infrastructure source of truth: `infra/main.bicep`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Blazor Server host replaces the API scaffold

#### Automated

- [x] 1.1 Release build succeeds
- [x] 1.2 No scaffold identifiers remain (WeatherForecast, AddOpenApi, MapOpenApi, UseHttpsRedirection)
- [x] 1.3 TenExCards.http and Weather.razor do not exist
- [x] 1.4 csproj contains zero PackageReference elements
- [x] 1.5 wwwroot/lib/bootstrap contains exactly one file
- [x] 1.6 App starts and root route returns 200 locally

#### Manual

- [x] 1.7 /circuit-check increments on click with no page reload
- [x] 1.8 Reconnect modal appears when the server is stopped mid-session
- [x] 1.9 Nav shows Home and Circuit check, no Weather link
- [x] 1.10 No new build warnings versus the pre-change baseline

### Phase 2: Deploy and record the live baseline

#### Automated

- [ ] 2.1 dotnet publish -c Release emits TenExCards.dll at the publish root
- [ ] 2.2 Archive first entry is TenExCards.dll (nested-zip assertion passes)
- [ ] 2.3 az webapp deploy reports success
- [ ] 2.4 Live root returns 200
- [ ] 2.5 Live /weatherforecast returns 404
- [ ] 2.6 TTFB and total-load timings captured for /
- [ ] 2.11 Rollback artifact preserved at bin/publish-scaffold-rollback.zip before the overwrite

#### Manual

- [ ] 2.7 /circuit-check increments on the live B1 instance
- [ ] 2.8 _blazor WebSocket visible in devtools; establishment time recorded
- [ ] 2.9 No HttpsRedirectionMiddleware[3] warning in the live startup log
- [ ] 2.10 Round-trip click latency recorded

### Phase 3: Update the repo's own record

#### Automated

- [ ] 3.1 AGENTS.md no longer contains "unmodified dotnet new webapi output"
- [ ] 3.2 AGENTS.md no longer contains "Whether to delete it is undecided"
- [ ] 3.3 deploy-plan.md contains a dated record for this deployment
- [ ] 3.4 roadmap.md marks Open Roadmap Question 4 resolved

#### Manual

- [ ] 3.5 AGENTS.md reads correctly start to finish as a fresh agent would
- [ ] 3.6 Stated commit convention matches the commits this change produced
