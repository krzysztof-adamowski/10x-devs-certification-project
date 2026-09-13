<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Pipeline Guarantees Under Test

- **Plan**: `context/changes/testing-pipeline-guarantees/plan.md`
- **Scope**: all six sub-phases
- **Date**: 2026-09-14
- **Verdict**: APPROVED (after fixes applied in this session)
- **Findings**: 0 critical, 8 warnings, 6 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS (was WARNING — F4, F5 fixed) |
| Architecture | PASS |
| Pattern Consistency | PASS (was WARNING — F8, F9, F11 fixed) |
| Success Criteria | PASS |

No product code changed: `git diff main...HEAD -- TenExCards/TenExCards/` is empty. No
`## What We're NOT Doing` boundary was crossed.

## Findings

### F1 — `media_type()` had no control in the self-test

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `scripts/verify_deploy.py` — `SELF_TEST_CASES`
- **Detail**: every `asset_rejection` case passed a value already lowercased and parameter-free, so
  the `;`-split and `.lower()` that `media_type()` performs were never exercised. Confirmed by
  probe: `asset_rejection("TEXT/HTML")` returns `None`. Dropping either operation would accept a
  real `text/html; charset=utf-8` sign-in page while `--self-test` still printed PASSED — the gate
  failing open with its own control certifying it.
- **Fix**: two composed cases calling `asset_rejection(media_type({...}))`, one accept
  (`text/css; charset=utf-8`) and one reject (`TEXT/HTML; charset=utf-8`).
- **Decision**: FIXED — verified red by replacing `media_type`'s body with `return value`.

### F2 — `redirect_rejection()` was scheme-blind, and its self-test case name implied otherwise

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW
- **Dimension**: Safety & Quality
- **Location**: `scripts/verify_deploy.py` — `redirect_rejection`
- **Detail**: `redirect_rejection("https://h/", "http://h/")` returned `None` — an https→http
  downgrade was accepted. The docstring described scheme handling as deliberate, which read as
  though the reverse was covered. Worse, the self-test case named
  *"root: https->http downgrade to another path"* passed because of the **path** difference, not
  the downgrade: a check that cannot fail for the reason its name gives, which is the exact class
  `MigrationGuardTests` was narrowed for.
- **Fix**: reject when `want.scheme == "https" and got.scheme == "http"`; split the misleading case
  into a path case and a genuine same-path downgrade case.
- **Decision**: FIXED — verified red by neutering the scheme rule.

### F3 — the CI step's comment over-claimed what `--self-test` covers

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `.github/workflows/deploy.yml`, `TenExCards/AGENTS.md`, `scripts/README.md`
- **Detail**: the step was described as "gates the gate". It gates the two extracted predicates and
  nothing else — `py_compile` checks syntax, not name resolution, and `--self-test` never calls
  `fetch()`, `fetch_root()`, `normalise_base()`, `same_origin()`, `AssetCollector` or `main()`.
  `fetch()` changed arity from 3 to 4 in this very change; a missed call site would be a runtime
  `TypeError` invisible to both. **This was then demonstrated during the review**: a patch silently
  deleted four functions, both checks passed, and only the live run caught it.
- **Fix**: correct the claim in all three places rather than pretend the coverage exists; record the
  real fix (plumbing smoke cases) as a follow-up.
- **Decision**: FIXED (documentation corrected); coverage deferred — `follow-ups/review-fixes.md` §4.

### F4 — `BuildHost` did not stub `ICardCandidateGenerator`

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW
- **Dimension**: Safety & Quality
- **Location**: `TenExCards/TenExCards.Tests/BootPathGuardTests.cs` — `BuildHost`
- **Detail**: `TenExCardsWebApplicationFactory` replaces the generator with the comment *"so no
  factory-booted test can reach the network"*, and the no-network property is a measured project
  invariant. `BuildHost` omitted it, so `ShippedModelRotation_IsNotEmpty` — the one test in the
  class that calls `CreateClient()` — ran with the real `GeminiCardCandidateGenerator` registered.
  The safety property held by *absence of a request*, not by construction; a later test that
  rendered a page would spend live quota against the developer's own key.
- **Fix**: mirror the shared factory — `RemoveAll<ICardCandidateGenerator>()` plus the stub.
- **Decision**: FIXED.

### F5 — the never-reaches-Key-Vault guarantee rested on an unasserted precondition

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW
- **Dimension**: Safety & Quality
- **Location**: `TenExCards/TenExCards.Tests/BootPathGuardTests.cs` — `BuildHost`
- **Detail**: no test supplies `DataProtection:KeyIdentifier` — verified. But `UseEnvironment
  ("Production")` skips user-secrets while still loading **environment variables**, so an ambient
  `DataProtection__KeyIdentifier` would satisfy the guard and send the host to Key Vault with the
  operator's `az` session. The test would fail, but only after the network call. Latent, not live:
  the variable is set neither on this machine nor in CI.
- **Fix**: throw in `BuildHost` when that variable is present, naming the cause — the same move
  `StaticAssets_AreAnonymous` makes with its count control.
- **Decision**: FIXED.

### F6 — framework-internal endpoints were pinned by exact name in a deploy-gating test

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Architecture
- **Location**: `TenExCards/TenExCards.Tests/PipelineMetadataTests.cs` — `ExpectedGated`
- **Detail**: `/_blazor`, `/_blazor/disconnect/`, `/_blazor/initializers/`, `/_blazor/negotiate` and
  `/_framework/opaque-redirect` are emitted by `AddInteractiveServerRenderMode()`, not by this
  repository, and were asserted by exact sequence equality. `deploy.yml` pins the SDK as `10.0.x`,
  which floats to the newest 10.0 runtime on every run, and this suite **gates the deploy** — so an
  ASP.NET Core patch renaming or adding one of these would block production for a reason unrelated
  to the repository, with a failure message pointing at the wrong cause.
- **Fix**: keep exact-match for application-owned routes; assert the framework's by prefix in a
  separate fact — non-empty, and entirely gated, which is the actual invariant.
- **Decision**: FIXED. Verified not to weaken G6: marking the render-mode builder anonymous now
  fails **three** tests (`RoutableEndpoints_…`, `FrameworkEndpoints_AreGated`,
  `EnhancedNavigation_…`) where it previously failed two.

### F7 — the in-memory `AppDbContext` block is now in its third verbatim copy

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM
- **Dimension**: Pattern Consistency
- **Location**: `BootPathGuardTests.cs`, `MigrationGuardTests.cs`, `TenExCardsWebApplicationFactory.cs`
- **Detail**: `TenExCards.Tests/AGENTS.md` documents the block as subtle and easy to get wrong; three
  copies means a future correction must land in three places or the suite drifts silently.
- **Fix**: extract `TestHost.UseInMemoryStore(...)` and call it from all three.
- **Decision**: DEFERRED — `follow-ups/review-fixes.md` §1. It edits the shared factory every other
  test class depends on, which is beyond what a review of this change should change.

### F8 — D5 drift: the `Gemini:Models is empty` guard is pinned by no test

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM
- **Dimension**: Plan Adherence
- **Location**: `TenExCards/TenExCards.Tests/BootPathGuardTests.cs` — `ShippedModelRotation_IsNotEmpty`
- **Detail**: the plan's contract said *"`Gemini:Models` configured empty ⇒ throws matching
  `*Gemini:Models is empty*`"*. `appsettings.json` ships a populated array and .NET configuration
  has no deletion, so no test host can produce the state the guard rejects. The implemented test
  asserts the shipped array instead — it catches someone emptying it, but would **not** catch the
  guard being removed.
- **Fix**: record it as an accepted gap in `test-plan.md` §7, the way F4 (dead circuits) records its
  residue, rather than leaving the constraint explained only in §6.6.
- **Decision**: FIXED (residue recorded); the guard stays deliberately unpinned.

### Observations

- **`hsts_rejection` extracted, and `max-age=0` now rejected.** The HSTS check was presence-only and
  inline in `main()`, so a header *disabling* HSTS passed and the decision could not be self-tested.
  Now a predicate alongside its two siblings, with three self-test cases. **FIXED.**
- **`scripts/README.md` was stale.** It declares itself the authority — *"if a rule here and a rule
  in a markdown file disagree, the script is right and the markdown is stale"* — yet still said
  `verify_deploy.py` exits non-zero only when something is not `200`, and omitted `--self-test`
  entirely. **FIXED.**
- **`deploy.yml`'s comment said the step "sits FIRST".** It is fourth, after checkout, build-id and
  SDK setup. **FIXED** — now "before anything expensive".
- **`test-plan.md` §4 said "15 files, 96 tests".** Now 17 files, 141 tests. **FIXED.**
- **Comment density.** Both new test files were the most heavily commented in the project (29% and
  22%, against 3–6% for `CandidateBoundsTests`/`TriageSessionTests`), and several blocks duplicated
  `AGENTS.md` text this same change had written — two copies of one measured fact to keep in sync.
  Trimmed to the operative sentence with a pointer to the markdown; the `because` strings were left
  alone, since those surface in failure output. **FIXED.**
- **`netloc` compared exactly** — `h` vs `h:443`, or a case difference, would read as a different
  host. Cannot arise against `*.azurewebsites.net`. **DEFERRED** — `follow-ups/review-fixes.md` §3.
