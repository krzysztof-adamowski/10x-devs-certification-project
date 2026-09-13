# Pipeline Guarantees Under Test — Implementation Plan

## Overview

Rollout Phase 1 of `context/foundation/test-plan.md`. Two risks, one change, because they are one
mechanism: the app answers `302 → sign-in → 200 text/html` and the deploy gate reads only the
integer, so fixing either side alone leaves the hole open from the other.

This phase makes a removed authorization or transport guarantee go **red as a test** rather than
green as a deploy (risk #1), and proves a boot-path demand is present **before** the merge that
reads it, by a check itself proven capable of failing (risk #5).

It is not a build-out. `TenExCardsWebApplicationFactory` already boots the real `Program.cs` — real
authentication, real fallback policy, real antiforgery, real endpoint graph, no network. The work is
assertions, one new host shape, and two new oracles in `scripts/verify_deploy.py`.

## Current State Analysis

**What is already pinned.** `AuthBoundaryTests` covers anonymous `200` on `/`, `/Error`,
`/not-found`; `302 → /Account/Login`, never `401`, on `/generate`, `/cards`, `/cards/new` and an
unmatched route; plus a register → sign-out → gated-access round trip. `MigrationGuardTests` pins
that `Testing:SkipStartupMigration` defaults to *running* the migration.

**What is not.** Research classified sixteen guarantees and found the fragile surface is **metadata,
not ordering**. Ordering failures (G2 `UseAuthentication` before `UseAuthorization`, G3 before
`UseAntiforgery`) are loud and self-reporting — an infinite login loop, `400` on every form. Six of
sixteen guarantees are invisible to a status-code observer, and **two are enforced by an absence**:
no `.AllowAnonymous()` on the render-mode builder (guarded today by a human running `grep` against a
ticked checklist line in `context/archive/2026-09-13-manage-saved-cards/plan.md:611`), and
`ASPNETCORE_ENVIRONMENT` being unset on the live site.

**The deploy gate's hole is larger than `test-plan.md` §2 stated.** `scripts/verify_deploy.py:241`
is a single-integer oracle. `fetch()` (`:70-83`) returns from *outside* its `with` block, so the
`HTTPResponse` — and `Content-Type` with it — is destroyed at `:80` before the caller sees it. The
landed URL is already returned as the third element and thrown away by the `_, _` at `:236`. And
`fetch_root` (`:117-130`) fails only on a **different host**, so a same-host `/` → `/Account/Login`
is logged and accepted, and the sign-in page's assets become what is verified. A fully gated site
prints `DEPLOY VERIFIED`.

**Risk #5's sharpest fact.** `DataProtection:KeyIdentifier` (D6) is exercised in **none** of the
three environments: Development-skipped locally, Development-skipped in CI because
`TenExCardsWebApplicationFactory:44` pins `Environments.Development`, and live only in the
container. That pin is deliberate — its own comment says it exists so a framework change cannot
silently make tests reach Key Vault — so the guard is unreachable through the existing host by
construction.

### Key Discoveries

Six probes were run against the real host during planning and deleted afterwards. Each converts a
research open question into a measured fact.

- **A Production-shaped host with `DataProtection:KeyIdentifier` absent throws with the guard's own
  message, using no network.** Risk #5's cheapest layer is viable
  ([Program.cs:181-193](TenExCards/TenExCards/Program.cs#L181-L193)).
- **A Production-shaped host with the identifier *present* reaches Key Vault.** Measured: the
  operator's `az` credentials were used and `keys/wrap/action` returned `ForbiddenByRbac`. The boot
  itself succeeded and `GET /` returned `200` — the vault call is lazy, at key-ring read. **This
  rules out the control research proposed.** The obvious control ("present ⇒ boots") is exactly the
  wall the Development pin exists to hold, and it would fail differently on a dev machine, in CI,
  and on a machine with no `az login`.
- **The endpoint graph partitions cleanly and is small enough to pin exactly.** 59 endpoints: 44
  static-asset (44 anonymous) and 15 routable — 6 anonymous (`/`, `/Error`, `/not-found`,
  `/Account/Login`, `/Account/Register`, `POST /Account/Logout`) and 9 gated (`/generate`, `/cards`,
  `/cards/new`, the `{**path:file}` fallback, `/_blazor`, `/_blazor/negotiate`,
  `/_blazor/disconnect/`, `/_blazor/initializers/`, `/_framework/opaque-redirect`). The last group
  **is G6 made visible**: those endpoints are gated precisely because nobody marked the render-mode
  builder anonymous.
- **`UseHsts()` emits nothing in-process, even over an `https` scheme.** Its default excluded-hosts
  list contains `localhost`. G9 is not assertable through the test host without forging a `Host`
  header — which is why the environment assertion belongs in the deploy gate, where the request is
  genuinely `https` against a real hostname.
- **`RedirectToLogin` has no trigger.** `Routes.razor` carries no `@rendermode`, so the Router is
  statically rendered and there is no client-side routing; an enhanced-navigation request for
  `/generate` was measured returning `302 → /Account/Login?ReturnUrl=%2Fgenerate` from the HTTP
  pipeline. Dead code, **not** a gap.
- **An anonymous `403` is unreachable.** The fallback policy challenges with `302` rather than
  forbidding, so `UseStatusCodePagesWithReExecute("/not-found")` never renders "does not exist" for
  a permission failure. `F-01`'s forward-looking note has no live trigger.
- **The laundering reproduces in-process.** An anonymous `GET /nope-does-not-exist` with redirects
  followed returns `200 text/html` having landed at `/Account/Login?ReturnUrl=…`.

Constraints inherited from the record:

- **`lessons.md` — "A negative check needs a control, or it cannot fail"** and **"Prove the check
  before trusting the result."** Every absence assertion here carries a control that must be found
  in the same run.
- **`MigrationGuardTests` is the live near-miss.** An earlier unconditional guard threw the same
  `InvalidOperationException` earlier, making the assertion tautological; it was narrowed to
  `*Relational-specific methods can only be used*`. Every new guard assertion pins the **cause**,
  not the type.
- **The 2026-09-10 narrowing must survive.** `impl-review-phase-1-2.md:105` records that an
  `http→https` upgrade on the same host is `httpsOnly` working and is logged, not failed. A stricter
  root rule must keep scheme upgrades passing.
- **`TenExCards.Tests/AGENTS.md`** — any test constructing `new WebApplicationFactory<Program>()`
  directly must set **every** guarded setting, or it passes locally on user-secrets and fails only
  in CI. There are currently two such bare factories.

## Desired End State

A change that removes `.AllowAnonymous()` from `MapStaticAssets()`, adds a third `.AllowAnonymous()`
anywhere, drops a new page into `Components/Account/Pages/`, or merges code reading a boot-path
setting that is not yet set, fails a test **locally and in CI, before anything deploys**. A deploy
that serves a fully gated site, or one whose container is no longer running as Production, fails
`Verify` instead of printing `DEPLOY VERIFIED`. Every one of those assertions has been observed
failing for its intended reason, at least once, by deliberate breakage.

Verified by: the whole suite green; each new assertion individually observed red under a deliberate
edit; and one CI run whose **step list** shows the new script self-test failing with `Test`,
`Publish`, `Pack`, `Deploy` and `Verify` all skipped.

## What We're NOT Doing

- **Not testing G2/G3 pipeline ordering.** Research established both are loud and self-reporting.
  An ordering test buys the smallest signal per unit of cost in the whole inventory.
- **Not closing F4 (dead circuits pass the gate green).** Explicitly **re-deferred**, with the
  trigger rewritten — see `## Decisions Settled From Research Open Questions`.
- **Not asserting HSTS through the test host.** Measured impossible without forging a `Host` header;
  the equivalent assertion lands in the deploy gate instead.
- **Not constructing a working key-ring encryptor in any test, ever.** Measured reaching Key Vault.
  The Production-shaped host is forbidden a key identifier.
- **Not deleting `RedirectToLogin` or the `<NotAuthorized>` fragment**, and not branching
  `UseStatusCodePagesWithReExecute` on `403`. Both are behaviour changes to production code inside a
  testing phase, and both become correct if `Routes.razor` ever becomes interactive.
- **Not promoting `TenExCards.E2E` to gating.** `test-plan.md` §5 sets that bar as a track record
  earned over merges and assigns the revisit to Phase 3.
- **Not writing tests for `scripts/pack.py`.** This phase does not otherwise open it.
- **Not adding an `env:` block to `deploy.yml`.** CI supplies the test run nothing today and that
  property is load-bearing: it is why the 2026-09-13 `Gemini:ApiKey` guard was caught in CI at all.

## Implementation Approach

Two test layers plus two script oracles, sequenced cheapest-and-highest-signal first.

The **metadata layer** (sub-phase 1) enumerates `EndpointDataSource` from the already-booted host
and asserts the exact partition above. This is the direct replacement for the hand-run `grep`, and
it is the only shape that catches a marker nobody wrote a probe for — including one inherited from
an `_Imports.razor` that never mentions authorization.

The **boot-path layer** (sub-phase 2) adds a second host shape parameterised by environment,
asserting each demand's guard fires **with its own message**. D6's control is the *same absence
under Development not throwing* — same run, same factory shape, one variable changed. That proves
both that the environment branch is what fires the guard and that no other missing setting caused
the throw, at zero network cost.

The **gate layer** (sub-phases 3–4) changes `fetch()` to surface the content type, uses the landed
URL already being discarded, and adds a `--self-test` mode so the new assertions have a control and
the script can be proven runnable before it gates a deploy.

Sub-phase 5 is separate because "the assertion exists" and "the assertion can fail" are different
claims, and this repository has two recorded cases of the second not following from the first.

## Decisions Settled From Research Open Questions

Research left six open. All six are closed here as scope decisions.

| # | Question | Decision |
|---|---|---|
| 1 | `RedirectToLogin` — dead code or a real in-circuit gap? | **Dead code, measured.** Pin the behaviour (sub-phase 1.3), document the component, change no production code. |
| 2 | Can a Production-shaped test host exist without network? | **Yes, for the absent case — measured.** It must never be given a key identifier; that case was measured reaching Key Vault. |
| 3 | A `403` renders "does not exist" | **No live trigger.** The fallback policy challenges rather than forbids. Recorded in §7 negative space; no code change. |
| 4 | F4 — dead circuits pass the gate | **Re-deferred**, trigger rewritten: revisit when `TenExCards.E2E` is promoted to gating (`test-plan.md` §5, Phase 3), since that suite already drives real circuits and a second gate-side probe duplicates it at higher cost. Recorded with the residue stated plainly: E2E is `continue-on-error`, so a dead circuit still reports green in CI today. |
| 5 | Nothing pins `ASPNETCORE_ENVIRONMENT` unset | **Asserted in `verify_deploy.py`** (sub-phase 3.3), via the `Strict-Transport-Security` header on the live `https` root — the one assertion an in-process test provably cannot make. |
| 6 | Neither deploy script has any test | **`--self-test` plus a CI step** (sub-phase 4), running before `Test` so a syntax error or a broken assertion stops the job before anything deploys. |

## Critical Implementation Details

**The Production-shaped host must never receive `DataProtection:KeyIdentifier`.** Measured on
2026-09-14: supplying it made the host construct `DefaultAzureCredential`, reach
`kv-tenexcards-plc`, and fail `keys/wrap/action` with `ForbiddenByRbac` — from a developer's machine,
using their `az` session. The boot and `GET /` both still succeeded, so this failure is *silent to a
status-code observer*. The guard-fires test asserts only the absent case; the present case is left
to the manual `Xml`-column verification already documented in `TenExCards/AGENTS.md` with its
false-positive trap.

**Every new bare factory must set all four guarded settings.** `TenExCards.Tests/AGENTS.md` records
this as a rule with an incident behind it: `WebApplication.CreateBuilder` loads user-secrets in
Development, so a test that omits a guarded setting passes locally and fails only in CI — which
means a failed deploy. Sub-phase 2 adds a third bare factory; the count in that `AGENTS.md` sentence
must be updated with it.

**The static-asset partition is discovered by metadata, not by path.** Asset endpoints carry a
`StaticAsset*` metadata type; matching on route patterns would drift with every fingerprinted file.
The asset count is the control: if the partition returns zero assets, the check is broken and its
verdict is void, not a pass.

## Sub-Phase 1: Endpoint metadata contract

### Overview

Replace the hand-run `grep` with an assertion. Pins G4 (static assets anonymous), G5 (sign-out
reachable signed-out), G6 (render-mode builder **not** anonymous — the absence), and G7 (Identity
folder inheritance), plus the enhanced-navigation behaviour that settles open question 1.

- **Behaviour asserted**: the set of endpoints carrying `IAllowAnonymous` metadata is exactly the
  six routable ones plus every static-asset endpoint — and nothing else.
- **Regression caught**: a third `.AllowAnonymous()` call anywhere in the pipeline; a new page
  dropped into `Components/Account/Pages/` inheriting anonymity from `_Imports.razor` with no
  attribute anyone would review; `.AllowAnonymous()` removed from `MapStaticAssets()`; and
  `.AllowAnonymous()` added to the render-mode builder, which would exempt every page route at once.
- **Research source**: §2 guarantee table (G4–G7); §2's note that G6's guard today is
  `context/archive/2026-09-13-manage-saved-cards/plan.md:611`, a checklist line; planning probes H
  and I.
- **Edge / boundary case**: the asset partition must be non-empty. A zero-asset result means the
  metadata predicate stopped matching — a broken check returning the desired answer, which
  `lessons.md` names as worse than a wrong one.
- **Anti-pattern avoided**: not a status-code probe per route (that reproduces the gate's own hole
  and cannot see a marker on an endpoint nobody probed); not an enumeration that would still pass if
  a new anonymous route were added — the assertion is an **exact set**, so any addition fails.

### Changes Required

#### 1. New test file

**File**: `TenExCards/TenExCards.Tests/PipelineMetadataTests.cs`

**Intent**: Enumerate the booted endpoint graph and pin which endpoints are anonymous, so a
metadata change that no status code reveals fails here instead of shipping.

**Contract**: `IClassFixture<TenExCardsWebApplicationFactory>`. Resolves
`IEnumerable<EndpointDataSource>` from `factory.Services` after a client has been created (the graph
is not built before then). Partitions on whether any metadata entry's type name contains
`StaticAsset`. Three facts:

- every static-asset endpoint carries `IAllowAnonymous`, **and the asset count is greater than
  zero** — stated in the same assertion so a broken predicate cannot read as a pass;
- the anonymous routable route patterns are exactly `/`, `/Error`, `/not-found`, `/Account/Login`,
  `/Account/Register`, `/Account/Logout`;
- the gated routable route patterns are exactly `/generate`, `/cards`, `/cards/new`,
  `{**path:file}`, `/_blazor`, `/_blazor/negotiate`, `/_blazor/disconnect/`,
  `/_blazor/initializers/`, `/_framework/opaque-redirect`.

Both routable assertions use a set comparison whose failure message names the added or missing
pattern. A comment records that the gated list is where G6 is observable, and that a new page
failing this test is the test working — the page must be classified, not the list relaxed.

#### 2. Enhanced-navigation assertion

**File**: `TenExCards/TenExCards.Tests/PipelineMetadataTests.cs`

**Intent**: Pin that a Blazor enhanced navigation to a gated route is still gated by the HTTP
pipeline, which is what makes `RedirectToLogin` unnecessary today.

**Contract**: anonymous `GET /generate` carrying the `blazor-enhanced-nav: on` request header, with
`AllowAutoRedirect = false`, returns `302` with `Location` starting `/Account/Login`. A comment
records that this goes red the day `Routes.razor` becomes interactive — the change that would turn
`RedirectToLogin` from dead code into load-bearing — and points at
`Components/Account/RedirectToLogin.razor`.

### Success Criteria

#### Automated Verification

- The suite passes: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
- The new file's tests are present in the run and green
- The solution still builds: `dotnet build TenExCards/TenExCards.slnx`

#### Manual Verification

- Temporarily add `.AllowAnonymous()` to the `AddInteractiveServerRenderMode()` builder in
  `Program.cs`; confirm `PipelineMetadataTests` fails naming the moved patterns, then revert
- Temporarily remove `.AllowAnonymous()` from `MapStaticAssets()`; confirm the asset assertion fails
  rather than the asset count silently becoming zero, then revert
- Confirm the asset-count control fires: temporarily break the `StaticAsset` metadata predicate and
  confirm the test fails on the count rather than passing vacuously

**Implementation Note**: pause for manual confirmation before sub-phase 2.

---

## Sub-Phase 2: Boot-path guards under a Production-shaped host

### Overview

Prove each boot-path demand's guard fires for its own reason, including the one demand no
environment exercises today.

- **Behaviour asserted**: with one guarded setting absent, host construction throws
  `InvalidOperationException` whose **message names that setting**; and `DataProtection:KeyIdentifier`
  does so only outside Development.
- **Regression caught**: a merge that reads a new boot-path setting before it is set on the app; a
  guard silently removed or weakened; the Development exclusion accidentally widened to Production,
  which would ship an unencrypted key ring signing auth cookies.
- **Research source**: §5.1 demand table (D1–D8); §5.2 three-environment matrix; §5.3 "D6 is
  exercised in none of the three environments"; §5.4 the design fork; planning probes A and B.
- **Edge / boundary case**: D6 under Development with the setting absent must **not** throw — the
  in-run control. If it throws, the environment branch is not what fired the guard and the
  Production assertion proves nothing.
- **Anti-pattern avoided**: matching on exception **type** alone. `MigrationGuardTests` is the live
  near-miss in this very suite, where an earlier guard threw the same type and made the assertion
  tautological. Every assertion here matches the message. Also avoided: constructing a working
  encryptor, measured reaching Key Vault.

### Changes Required

#### 1. New test file with a shared bare-factory helper

**File**: `TenExCards/TenExCards.Tests/BootPathGuardTests.cs`

**Intent**: One place that builds a bare `WebApplicationFactory<Program>` with a named environment
and a named setting omitted, so every guard can be asserted without each test re-deriving the
four-settings rule.

**Contract**: a private helper taking the environment name and the set of settings to omit; it
supplies every *other* guarded setting (`ConnectionStrings:DefaultConnection`, `Gemini:ApiKey`,
`Gemini:Models` where the guard requires it, `Testing:SkipStartupMigration`) plus the in-memory
`AppDbContext` replacement that `MigrationGuardTests` already demonstrates — the descriptor filter
by generic argument, not by `DbContextOptions<>` alone.

A file-level comment states the standing rule: **this host is never given
`DataProtection:KeyIdentifier`**, with the 2026-09-14 measurement as the reason.

Facts:

- D1 `ConnectionStrings:DefaultConnection` omitted ⇒ throws matching
  `*ConnectionStrings:DefaultConnection is not configured*`
- D4 `Gemini:ApiKey` omitted ⇒ throws matching `*Gemini:ApiKey is not configured*`
- D5 `Gemini:Models` configured empty ⇒ throws matching `*Gemini:Models is empty*`
- D6 under `Production` with the identifier omitted ⇒ throws matching
  `*DataProtection:KeyIdentifier is not configured*`
- **Control, same class, same run**: D6 under `Development` with the identifier omitted ⇒ does
  **not** throw; the host builds

#### 2. Update the testing rules

**File**: `TenExCards/TenExCards.Tests/AGENTS.md`

**Intent**: The "there are currently two such bare factories" sentence becomes wrong the moment this
file lands, and that sentence is what a future agent greps.

**Contract**: update the count, name `BootPathGuardTests`, and add the never-supply-a-key-identifier
rule with its measured reason beside the existing `## A green local run is not a green CI run`
section.

### Success Criteria

#### Automated Verification

- The suite passes: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
- `grep -rn "new WebApplicationFactory<Program>" TenExCards/` returns exactly the number
  `TenExCards.Tests/AGENTS.md` now claims
- No test run opens a network connection to `vault.azure.net` (see manual check below)

#### Manual Verification

- Run the suite with the user-secrets store moved aside, per the PowerShell block in
  `TenExCards.Tests/AGENTS.md`, and confirm it is still green — this is the CI-shaped run
- Confirm the D6 control actually controls: temporarily change the control's environment to
  `Production` and confirm it then throws, then revert
- Confirm each guard assertion pins its cause: temporarily supply `Gemini:ApiKey` to the D1 case and
  confirm D1 still fails for D1's reason rather than passing on another guard's throw
- Watch the run for Key Vault traffic (`netstat`, or the absence of any `ForbiddenByRbac` /
  `An error occurred while reading the key ring` line in the output)

**Implementation Note**: pause for manual confirmation before sub-phase 3.

---

## Sub-Phase 3: The deploy gate gets a second oracle

### Overview

Give `verify_deploy.py` the two oracles it structurally lacks: content type, and the landed path.

- **Behaviour asserted**: every same-origin asset is served with a non-HTML content type; the root
  page lands on the **path** it asked for; and the live root response carries
  `Strict-Transport-Security`.
- **Regression caught**: a gated asset laundering `302 → sign-in → 200 text/html`; a fully gated
  site printing `DEPLOY VERIFIED`; and `ASPNETCORE_ENVIRONMENT` being added as an app setting, which
  would silently disable both HSTS and key-ring encryption with every response still `200`.
- **Research source**: §4 — the single-integer oracle at `:241`, the response destroyed at `:80`,
  the landed URL discarded by `_, _` at `:236`, and the table of failure classes the gate records as
  a pass; §6 — the live site's four app settings, `ASPNETCORE_ENVIRONMENT` absent; planning probes
  F and G.
- **Edge / boundary case**: an `http→https` upgrade on the same host must still **pass**. The
  2026-09-10 review narrowed the original fix precisely to allow it
  (`impl-review-phase-1-2.md:105`), and failing all same-host redirects would report `httpsOnly`
  working as a broken deploy.
- **Anti-pattern avoided**: asserting status codes only — that reproduces the exact hole the gate
  already has. Also avoided: hard-coding `/Account/Login` into the gate, which a renamed login route
  would silently reopen.

### Changes Required

#### 1. Surface the content type

**File**: `scripts/verify_deploy.py`

**Intent**: `fetch()` currently returns from outside its `with` block, so the header is gone before
the caller sees it. The content type cannot be asserted until that changes.

**Contract**: `fetch()` returns `(status, body, landed_url, content_type)`, reading the header
*inside* the `with` block. Both call sites (`fetch_root`, the asset loop) updated. The error path
(`HTTPError`) returns the same arity. Standard library only; no new imports beyond what is present.

#### 2. Assert asset content type

**File**: `scripts/verify_deploy.py`

**Intent**: An asset that arrives as a sign-in page is the failure the status check cannot see.

**Contract**: beside the existing `if status != 200` check in the asset loop, fail when the
content-type's media type is `text/html`. The message names the asset, the media type received, and
states the likely cause (the asset is gated and the sign-in page was served in its place). Assets
are logged with their media type so a CI log shows the oracle working on a pass, not only on a fail.

#### 3. Assert the root landed on the path it asked for, and that the site is Production-shaped

**File**: `scripts/verify_deploy.py`

**Intent**: Close the larger hole — a gated root page — and pin the environment setting that nothing
currently watches.

**Contract**: in `fetch_root`, the existing different-host failure is kept. Added beneath it: fail
when the landed **path** differs from the requested path. A scheme-only change on the same host and
path stays a logged pass, preserving the 2026-09-10 narrowing verbatim. The failure message
distinguishes the two cases explicitly.

Separately, after the root fetch succeeds, fail when the response carries no
`Strict-Transport-Security` header, with a message naming `ASPNETCORE_ENVIRONMENT` as the likely
cause and stating that adding it as an app setting also disables key-ring encryption. A comment
records that this cannot be asserted in-process: `UseHsts()`'s default excluded hosts contain
`localhost`, measured 2026-09-14.

### Success Criteria

#### Automated Verification

- `python scripts/verify_deploy.py --help` runs without error
- `python -m py_compile scripts/verify_deploy.py` succeeds
- `python scripts/verify_deploy.py` passes against the live site

#### Manual Verification

- Read the run's output and confirm each asset now logs its media type
- Confirm the scheme-upgrade case still passes: run against the `http://` base URL and confirm the
  upgrade to `https://` is logged, not failed
- Confirm the HSTS assertion is reading a real header and not a default, by inspecting the logged
  root response

**Implementation Note**: pause for manual confirmation before sub-phase 4.

---

## Sub-Phase 4: `--self-test` and its CI step

### Overview

Give the three new assertions a control, and give the script a way to be proven runnable before it
gates a deploy.

- **Behaviour asserted**: each new assertion rejects the input it exists to reject **and** accepts
  the input it must not reject.
- **Regression caught**: a syntax error or a broken assertion in `verify_deploy.py` reaching `main`
  and surfacing mid-deploy; and an assertion silently weakened to always pass.
- **Research source**: §4 — "neither script has any test coverage, no Python version is pinned, and
  `deploy.yml` triggers only on push to `main`, so a syntax error in either script is first observed
  on `main`, mid-deploy"; open question 6.
- **Edge / boundary case**: the scheme-upgrade acceptance case is a *must-not-reject* control — it
  is the one the 2026-09-10 review added deliberately, and a self-test that only checks rejections
  would let a future tightening break `httpsOnly` silently.
- **Anti-pattern avoided**: a compile check alone, which kills the syntax-error class but proves
  nothing about whether the new assertions can fail — the exact gap `lessons.md` names.

### Changes Required

#### 1. Self-test mode

**File**: `scripts/verify_deploy.py`

**Intent**: Exercise the new decision logic against synthetic inputs, with no network, so the
assertions have a control.

**Contract**: a `--self-test` flag that runs a list of named cases and exits non-zero on any
failure, printing which case failed. It requires the redirect and content-type decisions to be
callable as small pure predicates rather than inline `if`s — extract them as such in sub-phase 3's
edits if not already. Cases, each stated as input → expected verdict:

- `http://host/` → landed `https://host/` ⇒ **accept** (the scheme upgrade; the 2026-09-10 narrowing)
- `https://host/` → landed `https://host/Account/Login` ⇒ **reject** (gating redirect)
- `https://host/` → landed `https://other/` ⇒ **reject** (different host, the existing rule)
- `https://host/` → landed `https://host/` ⇒ **accept**
- content type `text/css` ⇒ **accept**; `text/javascript` ⇒ **accept**
- content type `text/html; charset=utf-8` ⇒ **reject**
- missing content type ⇒ **reject**, and the message says so rather than crashing

Standard library only. Kept small; this is a control for three predicates, not a second suite.

#### 2. CI step

**File**: `.github/workflows/deploy.yml`

**Intent**: Run the self-test before anything else can consume the script, so a broken gate stops the
job rather than a deploy.

**Contract**: a step placed **before** `Test`, running `python3 scripts/verify_deploy.py --self-test`
and `python3 -m py_compile scripts/pack.py scripts/verify_deploy.py`. No `env:` block is introduced.
`permissions` is untouched. The step's comment records why it sits before `Test`: it is the cheapest
step in the job and it gates the gate.

### Success Criteria

#### Automated Verification

- `python scripts/verify_deploy.py --self-test` exits `0` locally
- `python -m py_compile scripts/pack.py scripts/verify_deploy.py` succeeds
- The suite still passes: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`

#### Manual Verification

- Confirm each self-test case can fail: temporarily invert one predicate and confirm the self-test
  names that case, then revert
- Confirm the scheme-upgrade acceptance case fails if the redirect rule is tightened to reject all
  same-host redirects — this is the control that protects `httpsOnly`

**Implementation Note**: pause for manual confirmation before sub-phase 5.

---

## Sub-Phase 5: Prove every new gate can fail

### Overview

"The assertion exists" and "the assertion can fail" are different claims. `lessons.md` records two
cases in this repository where the second did not follow from the first, and `S-01` set the method
that did work: commit a deliberate failure and read the run's **step list**, not its colour.

- **Behaviour asserted**: the new CI step actually blocks the job, and every new assertion has been
  observed red for its intended reason.
- **Regression caught**: an assertion that was always going to pass.
- **Research source**: §7 — the precedent set, including the `strings | grep -c` absence check that
  returned the desired answer, and the `S-01` positive precedent at
  `context/archive/2026-09-12-accounts-and-sessions/change.md:149-155`.
- **Edge / boundary case**: the deliberate CI failure must be in the **self-test step**, not in
  `Verify`. `Verify` runs *after* `Deploy`, so failing it deliberately would mean deploying a
  deliberately broken state to production; the self-test step runs before `Test` and nothing ships.
- **Anti-pattern avoided**: reading a red badge and calling it proof. The evidence is the step list
  showing which steps were skipped.

### Changes Required

#### 1. The deliberate-failure record

**File**: `context/changes/testing-pipeline-guarantees/change.md`

**Intent**: Record what was broken, what went red, and what the step list showed — so a later reader
can tell that these gates were proven rather than assumed.

**Contract**: a dated `## Verification` section listing, per assertion, the edit made, the failure
message observed, and the revert. For the CI proof: the run URL, the step that failed, and the
explicit list of steps reported **skipped**.

### Success Criteria

#### Automated Verification

- The working tree is clean of every deliberate breakage: `git status --short` shows only the
  intended change files
- The full suite is green after all reverts

#### Manual Verification

- Each sub-phase 1 and 2 assertion observed red under a deliberate edit and reverted
- Each sub-phase 3 and 4 assertion observed red via `--self-test` and reverted
- One CI run on `main` in which the self-test step was deliberately made to fail, with the step list
  read and recorded showing `Test`, `Publish`, `Pack`, `Retain`, `Azure login`, `Deploy` and
  `Verify` all skipped — then reverted and a clean run confirmed

**Implementation Note**: pause for manual confirmation before sub-phase 6.

---

## Sub-Phase 6: Cookbook and records

### Overview

Turn what shipped into the answer to "how do I add a pipeline or authorization test in this
project?", and correct every record this phase made stale.

### Changes Required

#### 1. Cookbook §6.1

**File**: `context/foundation/test-plan.md`

**Intent**: §6.1 is a `TBD` placeholder pointing at this phase. It becomes the canonical pattern
`/10x-tdd` reads in Lesson 2.

**Contract**: §6.1 gains location (`TenExCards/TenExCards.Tests/`), naming, the two reference tests
(`PipelineMetadataTests`, `BootPathGuardTests`), the run command, and the pattern itself stated in
one or two sentences: *enumerate the endpoint graph's `IAllowAnonymous` metadata as an exact set with
the asset count as its control; assert a guard by its message under a second, Production-shaped host
that is never given a key identifier, with the same absence under Development as the in-run control*.
A line records that the deploy-side twin lives in `scripts/verify_deploy.py --self-test`.

#### 2. Status, negative space, and the freshness ledger

**File**: `context/foundation/test-plan.md`

**Intent**: Keep the orchestrator's state truthful and record the two deliberate non-decisions.

**Contract**: §3 Phase 1 row → `complete`. §5 deployed-site smoke row updated to say the
content-type, landed-path and HSTS assertions have landed. §6.6 gains two or three lines on what
this phase taught. §7 gains three entries: F4's re-deferral with its rewritten trigger and its stated
residue; the anonymous `403` having no live trigger; and HSTS being unassertable in-process. §8
`updated:` bumped.

#### 3. Replace the hand-run grep in the repository rules

**File**: `TenExCards/AGENTS.md`

**Intent**: `### Authorization defaults to protected` currently ends by telling a reader to record a
third `.AllowAnonymous()` here. The enumeration test now catches one, and the rule should say so.

**Contract**: the "exactly two `.AllowAnonymous()` calls" paragraph gains a sentence naming
`PipelineMetadataTests` as what enforces it, and states that a new page failing that test is the
test working. A short note records that `RedirectToLogin` and the `<NotAuthorized>` fragment are
unreachable today, with the condition (`Routes.razor` becoming interactive) that would change that,
so neither is deleted as dead code.

#### 4. Close the change

**File**: `context/changes/testing-pipeline-guarantees/change.md`

**Contract**: `status: complete`, `updated:` bumped.

### Success Criteria

#### Automated Verification

- The suite passes: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
- `python scripts/verify_deploy.py --self-test` exits `0`
- `grep -n "TBD" context/foundation/test-plan.md` no longer matches §6.1

#### Manual Verification

- §6.1 read as if by someone who was not here: does it answer "how do I add a pipeline test?"
  without reading this plan
- `TenExCards/AGENTS.md` no longer implies the `.AllowAnonymous()` count is guarded by a human
- The F4 re-deferral states its residue plainly rather than reading as "handled"

---

## Testing Strategy

### Unit Tests

None. Every guarantee in scope is a property of the composed pipeline or the boot path; a unit test
would assert a call was made rather than that the pipeline behaves.

### Integration Tests

- `PipelineMetadataTests` — the endpoint-graph partition and the enhanced-navigation behaviour, over
  the real host.
- `BootPathGuardTests` — per-demand guard messages under two environment shapes, over bare factories.

### Manual Testing Steps

1. Run the suite with user-secrets moved aside (the block in `TenExCards.Tests/AGENTS.md`) and
   confirm green — this is the only local run shaped like CI.
2. For each new assertion, make the deliberate edit named in its sub-phase, confirm red for the
   intended reason, revert.
3. Run `python scripts/verify_deploy.py` against the live site and read the logged media types.
4. Make the CI self-test step fail deliberately on `main`; read the **step list**; revert; confirm a
   clean run.

## Performance Considerations

`PipelineMetadataTests` shares the class fixture, so it adds one client creation, not one host boot.
`BootPathGuardTests` builds five bare hosts, each of which throws or builds during service
configuration — the same cost shape as the existing `MigrationGuardTests`. No test in this phase
opens a network connection; the no-network property measured on 2026-09-12 must still hold, and
sub-phase 2's manual criteria check it.

## Migration Notes

No schema change, no infrastructure change, no app setting change. `deploy.yml` gains one step and
no `env:` block. `Program.cs` is edited only temporarily, during deliberate-failure verification,
and every edit is reverted before the phase closes.

## References

- Research: `context/changes/testing-pipeline-guarantees/research.md`
- Phase brief: `context/changes/testing-pipeline-guarantees/change.md`
- Strategy: `context/foundation/test-plan.md` §2 (risks #1 and #5), §3 (Phase 1), §5, §6.1
- Rules: `context/foundation/lessons.md` — "Prove the check before trusting the result",
  "A negative check needs a control, or it cannot fail"
- The live near-miss to imitate: `TenExCards/TenExCards.Tests/MigrationGuardTests.cs`
- The proof method to imitate:
  `context/archive/2026-09-12-accounts-and-sessions/change.md:149-155`
- The narrowing that must survive:
  `context/archive/2026-09-08-deploy-pipeline/reviews/impl-review-phase-1-2.md:105`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Endpoint metadata contract

#### Automated

- [x] 1.1 The suite passes
- [x] 1.2 The new file's tests are present in the run and green
- [x] 1.3 The solution still builds

#### Manual

- [x] 1.4 Render-mode builder marked anonymous ⇒ test fails naming the moved patterns; reverted
- [x] 1.5 `MapStaticAssets().AllowAnonymous()` removed ⇒ asset assertion fails; reverted
- [x] 1.6 Asset-count control fires when the metadata predicate is broken

### Phase 2: Boot-path guards under a Production-shaped host

#### Automated

- [ ] 2.1 The suite passes
- [ ] 2.2 Bare-factory count matches what `TenExCards.Tests/AGENTS.md` claims
- [ ] 2.3 No test run opens a connection to `vault.azure.net`

#### Manual

- [ ] 2.4 Suite green with the user-secrets store moved aside
- [ ] 2.5 D6 control throws when its environment is flipped to Production; reverted
- [ ] 2.6 Each guard assertion pins its own cause, not another guard's throw
- [ ] 2.7 No Key Vault traffic observed during the run

### Phase 3: The deploy gate gets a second oracle

#### Automated

- [ ] 3.1 `verify_deploy.py --help` runs without error
- [ ] 3.2 `py_compile` succeeds on `verify_deploy.py`
- [ ] 3.3 `verify_deploy.py` passes against the live site

#### Manual

- [ ] 3.4 Each asset logs its media type
- [ ] 3.5 The `http→https` scheme upgrade is logged, not failed
- [ ] 3.6 The HSTS assertion reads a real header

### Phase 4: `--self-test` and its CI step

#### Automated

- [ ] 4.1 `verify_deploy.py --self-test` exits `0`
- [ ] 4.2 `py_compile` succeeds on both scripts
- [ ] 4.3 The suite still passes

#### Manual

- [ ] 4.4 Each self-test case observed failing under an inverted predicate; reverted
- [ ] 4.5 The scheme-upgrade acceptance case fails if all same-host redirects are rejected

### Phase 5: Prove every new gate can fail

#### Automated

- [ ] 5.1 Working tree clean of every deliberate breakage
- [ ] 5.2 Full suite green after all reverts

#### Manual

- [ ] 5.3 Sub-phase 1 and 2 assertions observed red and reverted
- [ ] 5.4 Sub-phase 3 and 4 assertions observed red via `--self-test` and reverted
- [ ] 5.5 CI run with a deliberately failing self-test step; step list read and recorded; reverted

### Phase 6: Cookbook and records

#### Automated

- [ ] 6.1 The suite passes
- [ ] 6.2 `verify_deploy.py --self-test` exits `0`
- [ ] 6.3 §6.1 no longer matches `TBD`

#### Manual

- [ ] 6.4 §6.1 answers "how do I add a pipeline test?" standalone
- [ ] 6.5 `TenExCards/AGENTS.md` no longer implies a human guards the `.AllowAnonymous()` count
- [ ] 6.6 The F4 re-deferral states its residue plainly
