<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Passage to Saved Cards

- **Plan**: `context/changes/passage-to-saved-cards/plan.md`
- **Scope**: All 5 phases (full plan review)
- **Commits**: `9041291` … `7994684` (11 commits)
- **Date**: 2026-09-13
- **Verdict**: REJECTED at review; **all ten triaged 2026-09-13** — 5 fixed, 1 deferred, 2 recorded, 1 accepted, 1 skipped
- **Findings**: 1 critical, 6 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING → accepted (F7) |
| Safety & Quality | FAIL → resolved (F1, F2, F4, F9 fixed; F5 skipped by decision) |
| Architecture | PASS |
| Pattern Consistency | WARNING → resolved (F3 fixed; F6 deferred to S-03) |
| Success Criteria | PASS |

**Plan Adherence is a genuine PASS.** Every numbered item across Phases 2–5 lands as specified, and
the four deviations that exist — the model rotation, the fifth `GenerationFailure` value, the
`Assets` correction, the `503` fall-through — are all recorded in `change.md` as decisions, which is
what separates a deviation from drift. All scope guardrails hold: nothing writes `CardOrigin.Manual`,
no card-management surface exists, `Program.cs` still has exactly two `.AllowAnonymous()` calls, and
the passage reaches neither the database nor a log.

**Success Criteria re-verified, not assumed.** Build clean; 58/58 tests; the secret leak-check proven
live by planting a key-shaped string and watching it get caught.

## Findings

### F1 — Triage handlers have no re-entrancy guard and the buttons never disable

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `Components/Pages/Generate.razor.cs:167,188,197`; `Components/Pages/Generate.razor:79-80`
- **Detail**: `AcceptAsync` awaits `Store.SaveAsync` before mutating `_pending`, `_saved` and
  `_triagedCount`. During that await the same card and two fully-enabled buttons stay on screen with
  no spinner, no disabled state and no in-flight cue — a save against Azure SQL with
  `EnableRetryOnFailure` can take seconds, so a learner who thinks the click missed is invited to
  click again. There is no `_busy` flag, no `disabled` binding on either triage button, and no
  `_stage`/`_pending.Count` check at handler entry.

  If a second event can begin while the first is awaiting, three things follow. `Current` is
  evaluated eagerly, so both invocations capture the same candidate: **the card is saved twice**,
  with different `Guid`s and no uniqueness constraint to stop it, and `AdvanceAsync` runs twice so
  **the next candidate is dropped without ever being rendered**. On the last card the second
  `RemoveAt(0)` hits an empty list and throws `ArgumentOutOfRangeException` **unhandled inside a
  circuit event handler** — the generic Blazor error UI, which one card earlier would take the whole
  untriaged batch with it. That is the exact failure the plan spends paragraphs preventing elsewhere,
  and `GenerationResult`'s return-don't-throw rule exists for it.

  **What I could not verify:** whether Blazor Server actually dispatches a second event mid-await.
  SignalR defaults `MaximumParallelInvocationsPerClient` to 1, which may serialize the hub
  invocations and make these traces unreachable. I could not settle this by reading, and the
  component cannot be driven by this project's HTTP harness. **The code gap is certain; the
  reachability is not.** A ten-second empirical test settles it — see the fix.
- **Fix ⭐**: Add `private bool _busy;`, guard all three handlers (`if (_busy) return;` … `try/finally`),
  add `if (_stage is not Stage.Triaging || _pending.Count == 0) return;` to the triage handlers, and
  bind `disabled="@_busy"` on both triage buttons plus `disabled="@(!CanSubmit || _busy)"` on submit.
  - Strength: Closes the data-integrity hole whether or not it is reachable, and independently fixes
    a definite UX gap — a multi-second save currently gives no feedback at all. No lock needed: every
    mutation runs on the circuit dispatcher.
  - Tradeoff: One field and four small edits. The `disabled` binding is a visible improvement, not a
    cost.
  - Confidence: HIGH on the fix being correct and cheap; MEDIUM on the traces being reachable today.
  - Blind spot: Reachability. **Settle it first**: on the live site, double-click "Keep this card"
    on a 5-candidate batch, then run `check-prod-cards.ps1` — a duplicate prompt or a jump from
    "Card 1 of 5" to "Card 3 of 5" confirms it.
- **Decision**: FIXED — _busy flag, TryBeginTriage() entry guards on all three handlers, disabled bindings on the submit and both triage buttons, plus a "Saving…" in-flight cue

### F2 — `SubmitAsync` does not catch, and the generator can throw before its own `try`

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `Generate.razor.cs:114-126`; `Generation/GeminiCardCandidateGenerator.cs:50`; `Generation/PassageBounds.cs:21`
- **Detail**: The component wraps `GenerateAsync` in `try`/**`finally`** with no `catch`, relying
  entirely on the interface's "a failure is returned, never thrown" contract. Nothing enforces that
  contract, and the generator already breaks it: `TargetCandidateCount`, `BuildMessages` and
  `BuildOptions` all run **before** the `try` that starts at line 60. `PassageBounds` uses
  `Math.Clamp`, which throws `ArgumentException` when `min > max` — so a deployment where
  `Generation:MinCandidates` exceeds `MaxCandidates` turns every submission into the circuit error
  UI rather than a failure message.
- **Fix**: Move the three pre-`try` lines inside the generator's `try`, and add a defence-in-depth
  `catch (Exception)` at the call site that sets `_failureMessage` and `Stage.Failed`.
- **Decision**: FIXED — the generator's pre-try work moved inside a try returning ProviderError, plus a defence-in-depth catch at the call site that honours the cancel path

### F3 — `GenerationOptions` is bound without validation, unlike the two guards beside it

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `Program.cs:51-54`
- **Detail**: Bare `Configure<GenerationOptions>()`, while the very next fifteen lines hand-roll two
  eager boot-time guards for `Gemini:ApiKey` and `Gemini:Models`. The plan anticipated this —
  "`GenerationOptions` is a new pattern here, and if it is bound with `Configure<T>` … it needs
  `ValidateOnStart` for the throw to land at boot instead of at first use" — and the eager guards
  were written only for the Gemini section. The result is F2's hazard: a misconfigured
  `MinCandidates`/`MaxCandidates` pair fails at a learner's first submission rather than at boot.
- **Fix**: `AddOptions<GenerationOptions>().Bind(...).Validate(o => o.MinCandidates <= o.MaxCandidates && o.MaxPassageCharacters > 0).ValidateOnStart()`.
- **Decision**: FIXED — AddOptions().Bind().Validate().ValidateOnStart(); proven live by setting MinCandidates to 99 and watching 28 tests go red

### F4 — `_cts` and `_elapsedTimer` are replaced without being disposed

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `Generate.razor.cs:102`, `:222`
- **Detail**: Each submission assigns a fresh `CancellationTokenSource(30s)` and a fresh
  `PeriodicTimer` over the previous ones; only the last of each is disposed in `DisposeAsync`. Every
  abandoned CTS holds a live timer registration for up to 30 seconds. Bounded and small, but it sits
  directly against the plan's `## Performance Considerations` discipline about circuit footprint —
  and on a repeat submission the first call's `finally` disposes the *second* call's timer, leaving
  the first tick loop running against a live component.
- **Fix**: `_cts?.Dispose();` before the assignment, and `StopElapsedTimer();` as the first line of
  `StartElapsedTimer()`.
- **Decision**: FIXED — StopElapsedTimer() at the top of StartElapsedTimer(); the _cts half was fixed alongside F1

### F5 — One render batch per streamed chunk

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `Generation/GeminiCardCandidateGenerator.cs:72`; `Generate.razor.cs:108-112`
- **Detail**: The generator reports progress on every `ContentUpdate` and the component turns each
  report into `InvokeAsync(StateHasChanged)`. A generation produced 7–10 chunks in testing, but a
  longer stream yields proportionally more — each one a render diff plus a WebSocket frame, purely to
  repaint a counter the learner cannot act on. On B1 with concurrent learners this is measurable CPU
  and socket traffic for no informational gain.
- **Fix**: Have the progress callback only store `_chunks`, and let the existing 1 Hz `PeriodicTimer`
  do the rendering. Two lines, and the timer already runs.
- **Decision**: SKIPPED — 7-10 chunks per generation is not a load problem, and the per-chunk repaint is the most honest form of the liveness signal

### F6 — No test for "each candidate is triaged exactly once"

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Pattern Consistency
- **Location**: `TenExCards/TenExCards.Tests/`
- **Detail**: `TenExCards.Tests/AGENTS.md` names four invariants to assert. Three are covered —
  over-length refusal, duplicate dropping, account scoping. **"Each candidate is triaged exactly
  once" has none**, and it is the one invariant this slice newly introduces. F1 is precisely its
  violation. The plan's `## What is deliberately not tested` justifies not driving the component,
  and that argument holds — but it rests on "almost nothing decidable lives in the component", which
  the triage counters and the advance step quietly contradict. Related: `ParseAndApplyRules` is
  private on a class needing a live client, so the rule *pipeline* (cap → dedupe → bounds →
  min-count, plus malformed JSON) is untested even though its three parts are individually covered.
- **Fix**: Extract the triage step and `ParseAndApplyRules` into plain testable types and assert
  exactly-once directly — no component framework needed, which keeps the test-double rule intact.
- **Decision**: DEFERRED to S-03 — recorded in that plan as Phase 2 §5 (extract TriageSession) and in its Testing Strategy. S-03 reworks the same loop to add editing, so the extraction is work it wants anyway

### F7 — The Phase 5 commit swept in unrelated roadmap edits

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: commit `7fece62`, `context/foundation/roadmap.md`
- **Detail**: That commit carries the user's `S-03` and `S-04` `proposed → planning` flips, made in
  parallel by `/10x-new` runs while Phase 5 was being written. They are now attributed to an `S-02`
  commit. This is `lessons.md`'s "Parallel sessions share one index" near-miss, and `git commit --only`
  — which *was* used — does not prevent it: it fixes which **paths** are committed, not which
  **edits within them**. The half of that rule I skipped is the other half: *"Verify
  `git diff --cached -- <path>` against what the plan actually constrains. A path in the touched-file
  set authorises the file, never its current contents."*
- **Fix**: Nothing worth rewriting history for — the flips are correct and intended. Accept the
  attribution and re-check the staged diff at commit time in future.
- **Decision**: ACCEPTED — the flips are correct and the history stands; rewriting published commits for an attribution nit is not worth it

### F8 — Accept and discard are styled identically, and discard is irreversible

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architecture
- **Location**: `Generate.razor:79-80`
- **Detail**: Both render as `btn btn-primary` at identical width. This is **deliberate and
  plan-mandated** — "rejecting must cost no more effort than accepting or the acceptance target
  measures the interface" — so it is recorded rather than recommended against. The cost is that a
  misclick on discard is unrecoverable: the candidate is gone, there is no undo, and `S-04`'s card
  management would not help since nothing was saved. Equal *effort* was the requirement; equal
  *visual weight* is one way to achieve it, not the only way.
- **Fix**: None. Recorded so a future reader knows the tradeoff was seen, not missed.
- **Decision**: RECORDED, no change — the plan mandates equal prominence to protect the acceptance metric; noted that equal EFFORT was the requirement and equal APPEARANCE is one way to meet it

### F9 — A null JSON envelope reports `Refused` rather than `Malformed`

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `Generation/GeminiCardCandidateGenerator.cs:188`
- **Detail**: `envelope?.Candidates ?? []` sends a well-formed-but-null body down the
  `< MinCandidates` branch, so the learner is told their passage did not yield usable cards when the
  provider actually answered wrongly. The passage is retained either way, so this is wording only —
  but `Refused` is documented as having exactly one cause, and this is a second.
- **Fix**: Treat a null envelope as `Malformed`.
- **Decision**: FIXED — a null envelope now returns Malformed, restoring Refused to its single documented cause

### F10 — The cap is applied before deduplication, so duplicates consume slots

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `Generation/GeminiCardCandidateGenerator.cs:193-204`
- **Detail**: `Take(MaxCandidates)` runs before the deduplicator, so a model returning 15 candidates
  with 3 duplicates yields 9 rather than the 12 unique ones available. **This is exactly the order
  the plan specifies**, so it is not drift — but the plan states the order without noting the
  consequence, and the learner silently gets fewer cards than the passage supported.
- **Fix**: None unless card yield disappoints in practice; reordering to dedupe-then-cap would change
  a plan-stated rule and belongs in a slice that can measure the effect.
- **Decision**: RECORDED, no change — plan-specified order; revisit only with S-06 outcome data showing card yield actually disappoints

## Triage outcome — 2026-09-13

| Finding | Decision |
| --- | --- |
| F1 — triage re-entrancy | **FIXED** |
| F2 — unguarded generator call | **FIXED** |
| F3 — options bound without validation | **FIXED** |
| F4 — timer replaced without disposal | **FIXED** |
| F5 — one render per streamed chunk | SKIPPED |
| F6 — no exactly-once test | DEFERRED to `S-03` |
| F7 — commit swept unrelated edits | ACCEPTED |
| F8 — identical styling on discard | RECORDED |
| F9 — null envelope reports Refused | **FIXED** |
| F10 — cap before dedupe | RECORDED |

After the fixes: build clean, **58 passed / 0 failed** — and 58/58 again **with the user-secret store
moved aside**, which is the condition CI actually runs under and the check
`TenExCards.Tests/AGENTS.md` now requires before pushing anything that touches `Program.cs`.

Two fixes were verified rather than assumed. `ValidateOnStart` was proven to fire by setting
`Generation:MinCandidates` to 99, which turned **28 tests red** — every factory-booted test, because
the validation throws at host build — and `appsettings.json` was then confirmed restored byte-exact.
F1's guard cannot be asserted by this suite for the same reason the invariant it protects was never
asserted: the component is out of the harness's reach. That is what `S-03` inherits.
