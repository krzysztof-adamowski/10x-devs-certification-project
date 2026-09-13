<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Triage Outcomes and Card Origin Are Recorded

- **Plan**: `context/changes/outcome-recording/plan.md`
- **Scope**: Phases 1–3 of 4 (Phase 4 not started — deploy deliberately deferred to the user)
- **Date**: 2026-09-14
- **Verdict**: NEEDS ATTENTION (all code findings fixed in `12816bc` and the follow-up commit)
- **Findings**: 2 critical, 3 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | FAIL → PASS after fixes |
| Scope Discipline | WARNING |
| Safety & Quality | FAIL → PASS after fixes |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING (manual items pending) |

## Findings

### F1 — Recorder wait unbounded; swallow was eventual, not prompt

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence / Safety & Quality
- **Location**: `Generation/TriageRecorder.cs` (all members)
- **Detail**: The plan required `RecorderTimeoutSeconds` (2, a `const int`) with the caller's token
  linked into a `CancellationTokenSource`. Not implemented. The shared factory is built with
  `EnableRetryOnFailure()`, whose default budget reaches ~30s, so a transient Azure SQL fault froze
  the learner behind the `_busy` guard — on the reject path, which carries no other I/O and shows no
  progress, and on the open path, where `StopElapsedTimer()` had already frozen the counter under
  "Reading your passage and writing cards". Violates AGENTS.md "Never let the form freeze."
  Both review agents found this independently.
- **Fix**: Link each member's token to a 2s CTS at the swallow site; add a hanging-factory test.
- **Decision**: FIXED — `12816bc`

### F2 — Batch opened before the passage was cleared

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence / Safety & Quality
- **Location**: `Components/Pages/Generate.razor.cs`, `SubmitAsync`
- **Detail**: `OpenBatchAsync` was awaited between the `TriageSession` construction and
  `_passage = string.Empty`. The plan forbids exactly this placement. Blazor renders at an event
  handler's first yielding await, so the component rendered with the passage *and* its candidates
  both alive in the circuit — against the PRD retention guardrail and AGENTS.md "Never hold more in
  a circuit than you must." The invariant comment ("There is no moment in which both candidates and
  the text they came from exist") sat directly below the await that created that moment.
- **Fix**: Move the await below the passage/focus-hint clearing.
- **Decision**: FIXED — `12816bc`

### F3 — Owner guard escaped the swallow on the reject path

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `Generation/TriageRecorder.cs`, `OpenBatchAsync` and `IncrementAsync`
- **Detail**: `ArgumentException.ThrowIfNullOrWhiteSpace(ownerId)` sat outside the `try`, mirroring
  `CardStore`. On the accept path this is shielded by the component's own try/catch; the **reject
  path has no shield**, so the throw would escape a circuit event handler and take the untriaged
  batch with it — the exact failure the best-effort design exists to prevent. The interface promises
  "safe by construction". Reachable only if `ClaimTypes.NameIdentifier` were absent, so a contract
  hole rather than a live bug.
- **Fix**: Move the guard inside the `try` so it logs and returns like any other fault; pin it with
  a `[Theory]` over blank owners for all three members.
- **Decision**: FIXED

### F4 — Per-learner result set silently dropped card-only learners

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (measurement correctness)
- **Location**: `scripts/outcome_rates.sql`, result set 5
- **Detail**: `FROM TriageBatches GROUP BY OwnerId` omits every learner who owns cards but no batch
  — the all-manual learner, and anyone whose cards predate the epoch. That is precisely the
  population that drags `GeneratedShare` down, so the set was biased upward by its own omission,
  defeating its stated purpose. Confirmed empirically: the old form returned **zero rows** against
  the dev database; the fixed form returns **seven learners**, two of them at `GeneratedShare = 0.0`.
- **Fix**: Drive from `AspNetUsers` with two `OUTER APPLY` subqueries — not two `LEFT JOIN`s, which
  would multiply each child by the other's row count.
- **Decision**: FIXED

### F5 — Test gaps against the established pattern

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `TenExCards.Tests/TriageRecorderTests.cs`
- **Detail**: `CardOwnershipTests` pins the ownerless guard on each member and pins `CreatedAt`;
  the recorder tests pinned neither. `OpenedAt` in particular is what result set 2's ">30 min old"
  filter depends on — a `default(DateTimeOffset)` would classify every batch as settled.
- **Fix**: Add the blank-owner `[Theory]` for all three members and an `OpenedAt.BeOnOrAfter(before)`
  assertion. Suite went 141 → 144.
- **Decision**: FIXED

### F6 — `Untriaged` caveat read as exhaustive but omitted swallowed rejects

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `scripts/outcome_rates.sql` header; mirrored in `TenExCards/AGENTS.md`
- **Detail**: Three causes enumerated; a swallowed *reject* is a fourth. Numerically harmless (the
  denominator is `CandidateCount`), but the enumeration is mirrored into AGENTS.md so the gap
  propagates.
- **Fix**: One clause in both places.
- **Decision**: FIXED

### F7 — BOM added to three source files

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `Program.cs`, `Components/Pages/Generate.razor.cs`, `Data/AppDbContext.cs`
- **Detail**: Edited with a UTF-8-SIG writer, adding a byte-order mark these files did not have
  (verified against `6a863c7`). Cosmetic but unintended diff noise.
- **Fix**: Rewrite without BOM.
- **Decision**: FIXED — `12816bc`

### F8 — Roadmap flipped outside the plan's stated scope

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: `context/foundation/roadmap.md`
- **Detail**: `S-06` flipped `proposed` → `planning` → `in-progress`. The plan only describes
  touching the roadmap in Phase 4 (flip to `done`).
- **Fix**: None — this is the documented `/10x-plan` and `/10x-implement` bookkeeping ritual, not
  implementation scope creep.
- **Decision**: ACCEPTED

### F9 — Operator runbook authenticates as the server admin

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: `TenExCards/AGENTS.md`, "### Reading the outcome rates"
- **Detail**: The documented `sqlcmd` invocation uses `tenexadmin` + `sql-admin-password` to run a
  read-only query. A read-only contained user would be the lower-privilege fit, matching the
  `tenexdev` precedent. `SQLCMDPASSWORD` handling itself is correct per the PowerShell-history rule.
- **Fix**: Create a read-only contained user in `sqldb-tenexcards` and a vault secret for it.
- **Decision**: SKIPPED — needs a new data-plane object no template recreates (a fourth item for
  that list in AGENTS.md) and a user decision. Recorded here rather than actioned.

### F10 — Mixed `float` / `numeric` idiom across result sets

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `scripts/outcome_rates.sql`
- **Detail**: Rates use `SUM(CAST(x AS float))`; shares use `CASE … THEN 1.0`. Both are correct —
  no integer division anywhere, every divisor in `NULLIF(…, 0)` — but the idioms differ.
- **Fix**: Pick one idiom for readability.
- **Decision**: SKIPPED — cosmetic; both forms verified correct against the dev database.

## Success criteria status

Automated criteria for Phases 1–3 all pass (build clean, 144/144 tests, CI-like run without
user-secrets, migration applied to `sqldb-tenexcards-dev`, query proven against that database).

Pending, and not rubber-stamped:

- **1.8** — production startup log naming `AddTriageBatches`. Requires the Phase 4 deploy.
- **2.6 / 2.7** — completed and abandoned batch rows. Requires a browser triage run.
- **2.8** — save-failure-does-not-increment. No clean way to force a mid-triage database failure by
  hand; the recorder half is covered by the unreachable- and hanging-factory tests, and the ordering
  half is a code-level guarantee. Proposed as verified-by-inspection, pending user agreement.
- **Phase 4 (4.1–4.9)** — deploy deliberately deferred; the user chose "stop before deploying".
