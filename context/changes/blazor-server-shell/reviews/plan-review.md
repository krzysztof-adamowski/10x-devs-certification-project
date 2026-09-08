<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Interactive Blazor Server Shell

- **Plan**: `context/changes/blazor-server-shell/plan.md`
- **Mode**: Deep
- **Date**: 2026-09-08
- **Verdict**: REVISE → **SOUND** (all 5 findings fixed)
- **Findings**: 2 critical, 1 warning, 2 observations

## Verdicts

| Dimension | Verdict (at review) | After fixes |
|-----------|---------------------|-------------|
| End-State Alignment | PASS | PASS |
| Lean Execution | PASS | PASS |
| Architectural Fitness | PASS | PASS |
| Blind Spots | FAIL | PASS |
| Plan Completeness | WARNING | PASS |

## Grounding

8/8 claimed paths exist ✓ · Progress↔Phase 26/26 rows ✓ · phase names match ✓ · 0 stray checkboxes
outside `## Progress` ✓ · no TBD/TODO/"as needed" ✓ · brief↔plan ✓ · criterion 1.2 grep ✗ (F1).

`context/foundation/lessons.md` and `docs/reference/contract-surfaces.md` do not exist — both
checks skipped.

Codebase verification was done inline rather than via sub-agent: the entire project is nine source
files, all already read in the planning session, so a delegated sweep would have re-read what was
already in context.

Additional claim verified during review (no finding): the Bootstrap prune is safe —
`Components/App.razor:9` is the **only** reference to Bootstrap anywhere in the generated template,
so dropping the other 43 files breaks nothing.

## Findings

### F1 — Success criterion 1.2 fails on a correct implementation

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 — Success Criteria, Automated #2 (Progress 1.2)
- **Detail**: The criterion required a grep for `WeatherForecast`, `AddOpenApi`, `MapOpenApi` and
  `UseHttpsRedirection` across `TenExCards/` to return nothing. It cannot: `TenExCards/AGENTS.md`
  matches at lines 14, 115 and 119, and 15 stale binaries under `bin/`/`obj/` match too. Phase 3
  deliberately *keeps* a `UseHttpsRedirection()` mention in AGENTS.md, so the gate was
  unsatisfiable by construction — the implementer would hit a red check on correct work.
- **Fix**: Scope the grep to `--include=*.cs --include=*.csproj --include=*.razor`, with an
  inline note explaining that the type filter is load-bearing rather than tidiness.
- **Decision**: FIXED — verified after the edit: the filtered grep now returns only the six
  `Program.cs` lines Phase 1 deletes, so it returns nothing on completion.

### F2 — The stated rollback is destroyed by the plan's own Phase 2

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Migration Notes, vs. Phase 2 — Changes Required
- **Detail**: Migration Notes claimed "rollback means redeploying the previous archive." That
  archive is `TenExCards/bin/publish.zip` (372,646 bytes, 2026-08-31 20:17) — the artifact
  currently serving production. The publish step writes that exact path with `-Force`, and Phase 1
  has by then deleted the source it was built from. B1 has no deployment slots, so from the moment
  Phase 2 ran, the promised rollback would not have existed.
- **Fix**: New Phase 2 step 1 copies the live archive to
  `bin/publish-scaffold-rollback.zip` before anything else (remaining steps renumbered 2–5); a new
  automated gate (Progress 2.11) asserts it exists at the expected size; Migration Notes rewritten
  to describe the real recovery path, naming commit `035e064` as the rebuild point if the copy is
  lost.
- **Decision**: FIXED

### F3 — Baseline measurements have two homes, one parser-sensitive

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 2 — Baseline measurement, vs. Phase 3 — Deployment record
- **Detail**: Phase 2 instructed writing the timings into `plan.md` "alongside `## Progress`" while
  Phase 3 wrote the same numbers into `deploy-plan.md`. One fact with two homes that can drift,
  and the Phase 2 home sits inside the region `/10x-implement` parses mechanically for execution
  state — where a malformed line is a CRITICAL failure by this review's own grounding check.
- **Fix A ⭐ Recommended**: Dedicated `context/changes/blazor-server-shell/baseline.md`
  - Strength: One home, nowhere near the Progress block; survives if Phase 3 is dropped under time
    pressure, which the roadmap explicitly anticipates for deploy-adjacent work.
  - Tradeoff: One more small file in the change folder.
  - Confidence: HIGH — no parser touches that path.
  - Blind spot: None significant.
- **Fix B**: Record only in `deploy-plan.md`; Phase 2 reports at the manual gate
  - Strength: `deploy-plan.md` is already ground truth for what is deployed; zero new files.
  - Tradeoff: Numbers live only in scrollback until Phase 3 runs; lose that phase and you re-measure.
  - Confidence: MEDIUM — depends on Phase 3 actually running.
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A. Phase 2 writes `baseline.md`; Phase 3 points `deploy-plan.md` at
  it rather than copying the numbers; Desired End State updated to match.

### F4 — /circuit-check outlives the slice meant to remove it

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 1 — Changes Required #3
- **Detail**: The plan said `CircuitCheck.razor` "is deleted when S-02 lands." But S-01 lands
  first and adds accounts, leaving a publicly reachable unauthenticated diagnostic page in an app
  that has just grown an auth boundary. Only a counter is exposed, so this is hygiene rather than
  a hole — but the deletion trigger pointed at the wrong slice.
- **Fix**: Retarget the in-file deletion comment to S-01, with the reasoning recorded.
- **Decision**: FIXED

### F5 — The UseHsts() rationale is asserted, not verified

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Critical Implementation Details
- **Detail**: The plan stated as settled fact that HSTS and platform `httpsOnly` are
  "complementary, not redundant." That was never verified against this platform — in particular
  whether `azurewebsites.net` is already covered by browser HSTS preloading, which would make the
  header a no-op on this hostname while still mattering on a custom domain. In a repo whose
  `AGENTS.md` and `deploy-plan.md` are built around separating measured facts from assumptions, an
  unverified claim stated flatly is itself the defect; the decision to keep `UseHsts()` is
  unaffected.
- **Fix**: Restate as reasoning rather than fact, flag the preload question as explicitly
  unverified, and record the practical consequence (30-day `max-age` blocks plain-HTTP testing on
  that hostname).
- **Decision**: FIXED

## Post-fix verification

Re-ran the mechanical checks after all edits:

- Exactly one `## Progress` heading ✓
- Zero checkbox lines outside `## Progress` ✓
- Criteria bullets ↔ Progress rows: 10/10, 11/11, 6/6 ✓
- No duplicate step indices ✓ (`2.11` correctly takes the next available index rather than
  renumbering, per `references/progress-format.md`)
- Filtered F1 grep returns only the `Program.cs` lines Phase 1 removes ✓
- No stale references to the old baseline location ✓
- `plan-brief.md` Open Risks updated so brief↔plan consistency holds ✓
