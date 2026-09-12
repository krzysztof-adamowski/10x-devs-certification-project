<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Passage to Saved Cards (S-02)

- **Plan**: `context/changes/passage-to-saved-cards/plan.md`
- **Mode**: Deep
- **Date**: 2026-09-12
- **Verdict**: REVISE at review time; SOUND after triage
- **Findings**: 3 critical, 6 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | WARNING |
| Lean Execution | WARNING |
| Architectural Fitness | WARNING |
| Blind Spots | FAIL |
| Plan Completeness | WARNING |

## Grounding

11/11 paths ✓, 6/6 symbols ✓, brief↔plan ✓, Progress contract ✓ (5/5 phases matched, 39/39 success
criteria mapped to checkboxes, no stray checkboxes in phase bodies). Two cited line ranges had
drifted: `Program.cs:88-93` is now `90-95`, and `Program.cs:144-168` is now `152-179`.

Deep-mode verification covered blast radius on the existing test project, EF model assertions,
`TenExCards.Tests/AGENTS.md` compliance, interactive-render-mode readiness, and the existing
configuration/registration patterns in `Program.cs`.

## Findings

### F1 — Phases 1–3 each deploy to production; Phase 4 is written as if it were the only deploy

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Blind Spots
- **Location**: Phase 1 §5, Phase 4 §1, Implementation Approach
- **Detail**: `.github/workflows/deploy.yml` triggers on every push to `main` and the history is
  linear with one commit per phase, so each phase's commit is a production deploy. Phase 1's
  forward-only `AddCards` migration therefore runs on the production boot path at the end of Phase 1
  — verified only against `sqldb-tenexcards-dev` — while Phase 4 is written as though it were the
  first deploy. `S-01` did perform the missing check: `deploy-plan.md` records reading each deploy's
  startup log for the migration line. Supporting hazard: `Program.cs:159-161` calls `MigrateAsync()`
  only when migrations are pending, and the in-memory test provider needs none, so a `DbSet`
  committed without its migration yields a green suite and no `Cards` table in production.
- **Fix A ⭐ Recommended**: Post-push production check on Phases 1–3; reword Phase 4 as "measure the
  bounds".
  - Strength: Matches S-01's precedent and lessons.md's "Verify a restart from the log, never from
    the first 200".
  - Tradeoff: Three extra manual checkpoints.
  - Confidence: HIGH — the precedent is in the record for the immediately preceding slice.
  - Blind spot: Does not decide what happens if the production migration throws.
- **Fix B**: Hold Phases 1–3 on a working branch and push once at Phase 4.
  - Strength: One production deploy for the whole slice.
  - Tradeoff: Contradicts every prior slice's per-phase history and makes one deploy carry three
    phases of unverified change.
  - Confidence: MEDIUM — workable but unprecedented here.
  - Blind spot: Branch protection is absent, so nothing enforces the discipline.
- **Decision**: ACCEPTED as risk, on a stated premise. Recorded in the plan's
  `## Critical Implementation Details`: the instance has no users, so a failed boot costs the
  implementer's time rather than a learner's data — and **the first real learner retires the
  acceptance**, at which point Phases 1–3 need S-01's startup-log check.

### F2 — The plan never touches the test project's host wiring

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 2 §7, §8
- **Detail**: `TenExCardsWebApplicationFactory` supplies exactly two settings, and its own comment
  records why ("Program.cs throws on a null connection string before any service replacement below
  can run"). An unconditional `Gemini:ApiKey` guard breaks every test fixtured on the factory —
  `AuthBoundaryTests`, `IdentityConfigurationTests`, `PasswordStorageTests` and the new
  `CardOwnershipTests` — and the factory is in no phase's changed-file list. The promised
  `ICardCandidateGenerator` stub, the one additional double `TenExCards.Tests/AGENTS.md` permits, is
  never created or registered either. Separately, "the same style as the existing connection-string
  and key-identifier guards" names two guards that are *different* styles, and the repository has no
  options-binding precedent at all — no `Configure<T>`, `.Bind()` or `GetSection` anywhere.
- **Fix A ⭐ Recommended**: Unconditional guard (connection-string shape) plus the factory in Phase 2's
  file set, with a dummy key and the stub.
  - Strength: Local development genuinely needs the key, unlike vault key permissions; criterion 2.4
    already assumes boot-time failure.
  - Tradeoff: One extra setting line, and the stub must be specified.
  - Confidence: HIGH — the factory documents this exact interaction for the connection string.
  - Blind spot: Whether the binding needs `ValidateOnStart` is a separate decision.
- **Fix B**: Key-identifier shape, skipped in Development.
  - Strength: No test-project change.
  - Tradeoff: Local misconfiguration hides until the first generation; criterion 2.4 stops testing
    anything.
  - Confidence: MEDIUM.
  - Blind spot: Deployed behaviour is identical either way.
- **Decision**: FIXED via Fix A. Phase 2 §7 now states the unconditional choice and why the
  key-identifier reasoning does not transfer, flags `GenerationOptions` as a new pattern needing
  `ValidateOnStart`, and pins the DI lifetimes. A new Phase 2 §8 covers the factory and the stub;
  the old §8 became §9.

### F3 — Candidate text is unbounded against nvarchar(500)/(1000), and the test provider cannot catch it

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 1 §2, Phase 2 §5, §6, Performance Considerations
- **Detail**: The entity caps `Prompt` at 500 and `Answer` at 1000, but nothing between the model and
  `SaveAsync` enforces it — the JSON schema stated no `maxLength` and the generator applied only the
  cap and the deduplicator. An over-long candidate is a SQL truncation error thrown inside a circuit
  event handler: the generic Blazor error UI the plan itself names as the guardrail failing, with the
  untriaged batch lost behind it. The EF in-memory provider does not enforce `HasMaxLength`, so no
  factory test can catch a regression. Performance Considerations already assumed the bound to
  compute the circuit memory budget. During triage it emerged that 500/1,000 appear nowhere else in
  the repository and had no recorded derivation.
- **Fix ⭐**: State `maxLength` in the JSON schema and drop over-long candidates post-parse, with a
  boundary test on the pure function.
  - Strength: Bounds the text where it enters the system, making the entity limits and the memory
    budget true rather than assumed; testable without a save.
  - Tradeoff: A third post-parse rule.
  - Confidence: HIGH.
  - Blind spot: Does not cover a save failing for other reasons — see F6.
- **Decision**: FIXED, with drop chosen over truncate. Phase 1 §2 now records where 500/1,000 come
  from and what depends on them; Phase 2 §5 adds `CandidateBounds.WithinColumnLimits`; §6 adds
  per-field `maxLength` to the schema; §7 adds `MaxPromptCharacters`/`MaxAnswerCharacters` with a
  no-drift rule against `HasMaxLength`; §9 and the Testing Strategy add boundary tests plus an
  equality assertion between the configured limits and the entity's.

### F4 — The externally required learner-perspective test attaches to S-02, and the plan neither builds it nor descopes it

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: End-State Alignment
- **Location**: Testing Strategy → "What is deliberately not tested"
- **Detail**: `roadmap.md:285-286`, inside the S-02 risk block the plan cites in its own References:
  "The externally required test written from the learner's perspective attaches here, with US-01's
  acceptance criteria as its basis." The plan did not build it, mention it, or list it under
  "What We're NOT Doing", where every other deferral is recorded. Its "What is deliberately not
  tested" section argues the opposite direction, but that argument is about driving a circuit from
  the HTTP harness and does not reach a browser-driven test. US-01 also requires edit at equal
  prominence, and edit is `S-03`.
- **Fix A ⭐ Recommended**: Record the deferral and name the slice it attaches to instead.
  - Strength: Keeps a browser dependency out of the largest phase and turns a silent drop into a
    decision.
  - Tradeoff: Only acceptable if the target slice is actually named.
  - Confidence: MEDIUM.
  - Blind spot: "Externally required" is not defined in-repo.
- **Fix B**: Build the browser-driven US-01 test in this slice.
  - Strength: Discharges the roadmap's attachment point and covers the decaying manual checks.
  - Tradeoff: A new dependency, a new test category, and a CI decision in the largest phase.
  - Confidence: MEDIUM.
  - Blind spot: Free-tier cost if the test drives real generation per run.
- **Decision**: FIXED via Fix A, deferred to `S-03` for a substantive reason — US-01's first
  acceptance criterion names edit at equal prominence, and edit is `S-03`, so the test cannot assert
  US-01 in full before that slice. Recorded in `## What We're NOT Doing`, cross-referenced from
  "What is deliberately not tested", and made durable by a Phase 5 §3 roadmap edit with new
  criterion 5.7.

### F5 — `ReconnectModal.razor.js` is not the interop pattern the plan claims

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architectural Fitness
- **Location**: Key Discoveries, Phase 3 §3
- **Detail**: The plan called it "the repository's only existing JS-interop pattern — a collocated
  `.razor.js` module, which is the shape the unload warning follows". Verified false: the file has
  no `export` and no `import` across 63 lines, and `ReconnectModal.razor:1` loads it as plain markup,
  `<script type="module" src="@Assets[...]">`. It wires DOM listeners at module scope and calls
  `Blazor.reconnect()` directly. A repository-wide grep returns zero uses of `IJSRuntime`, so
  `Generate.razor.js` would be the first C#-invoked module — and the plan named neither `ImportMap`
  nor `ResourceAssetCollection` for resolving the fingerprinted path.
- **Fix A ⭐ Recommended**: Correct the Key Discovery and give Phase 3 the real mechanics.
  - Strength: Removes a false premise an implementer would follow into a failed module import, and
    makes the true cost visible before the largest phase starts.
  - Tradeoff: More Phase 3 detail.
  - Confidence: HIGH — verified against the file and its loading markup.
  - Blind spot: Whether the fingerprinted import resolves cleanly under `ImportMap` is untested.
- **Fix B**: Drop the `beforeunload` warning and rely on the on-page line, which is what US-01
  requires.
  - Strength: Removes the repository's first `IJSRuntime` use from the largest phase.
  - Tradeoff: Loses the browser-level confirmation the decision table deliberately chose.
  - Confidence: MEDIUM.
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A. The Key Discovery now states the opposite, and Phase 3 §3 specifies
  real exports, the two ways to resolve a fingerprinted path from `.razor.cs`, and a throwaway import
  check first — "a failed import is silent in the browser console and looks like a warning that
  simply never fires".

### F6 — The accept path has no failure handling

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 3 §2
- **Detail**: The plan is careful that the generator returns a failure rather than throwing; the write
  path got no equivalent. `SaveAsync` is awaited inside an event handler with no branch for it
  throwing — and it can, once `EnableRetryOnFailure`'s attempts are exhausted, which
  `Program.cs:44-45` exists because Azure SQL produces transient faults. The result is the generic
  Blazor error UI mid-triage and the loss of every untriaged candidate.
- **Fix ⭐**: An explicit failure branch on accept — the candidate stays in place, a retryable error
  is shown, the batch survives.
  - Strength: Applies the guardrail the plan already applies to generation to the other path that
    can fail.
  - Tradeoff: A sixth thing the component's state must express.
  - Confidence: HIGH.
  - Blind spot: A retry must not double-write.
- **Decision**: FIXED. Phase 3 §2 handles it inline without moving the state machine, and rules out
  both wrong shapes — advancing (the card was not saved and the learner would never know) and
  transitioning to `Failed` (that state re-renders a passage that is gone by design).

### F7 — The generation call's mechanics are underspecified

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 2 §4, §6, §7; Phase 3 §1
- **Detail**: Six things an implementer would have had to invent, all in the largest phase:
  `TimeoutSeconds` was configured but "CancellationTokenSource" appeared nowhere and nothing produced
  `GenerationFailure.Timeout`; "offers a cancel control" was the only mention of cancel, with no
  target state; `GenerationProgress` was used in the interface signature and never defined; nothing
  drove the per-second elapsed-time re-render, which is the only liveness signal under the
  non-streaming fallback; the generator's DI lifetime was unstated, and it is the first service here
  holding an outbound HTTP client with no `IHttpClientFactory` usage to follow; and
  `MaxFocusHintCharacters` had a UI limit but no handler re-check, though the plan argues elsewhere
  that "a disabled button is a courtesy and not an enforcement".
- **Fix ⭐**: Pin all six in the Phase 2 and Phase 3 contracts before implementation starts.
  - Strength: Each is a one-line decision now and a mid-phase detour later.
  - Tradeoff: None beyond editing time.
  - Confidence: HIGH — verified by grep.
  - Blind spot: Cancel semantics may deserve a product opinion.
- **Decision**: FIXED, all six. The timeout CTS is the component's and the generator returns
  `Timeout` rather than letting `OperationCanceledException` escape; cancel returns to `Composing`
  with the passage intact via the same path; `GenerationProgress` is a chunk count carrying no model
  text, because passage-derived content on screen after clearing would leak the disposal guarantee;
  a `PeriodicTimer` drives the elapsed counter; `ICardCandidateGenerator` is singleton and
  `ICardStore` scoped, with reasoning; the focus hint gets its handler re-check.

### F8 — `MigrationGuardTests` would pass for the wrong reason

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 2 §8 (consequence in `TenExCards.Tests`)
- **Detail**: `MigrationGuardTests.cs:50-52` asserts `factory.Services` throws
  `InvalidOperationException`, to prove the migration-skip flag defaults to running the migration.
  The unconditional `Gemini:ApiKey` guard throws the same exception type earlier, at service
  configuration, so the assertion is satisfied before the migration block is reached — green test,
  no guarantee. This is lessons.md's "Prove the check before trusting the result" arriving in a test
  rather than a CLI command.
- **Fix**: Give that test the dummy `Gemini:ApiKey` too, and assert on the exception message rather
  than only its type.
- **Decision**: FIXED. Added to Phase 2 §8's file list and contract, with the lessons.md reference
  and a note to comment the test accordingly.

### F9 — An empty or below-minimum candidate set has no branch

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: End-State Alignment
- **Location**: Phase 2 §6, Phase 3 §1
- **Detail**: The parsed set was clipped down to `MaxCandidates` but never checked upward against
  `MinCandidates`. A schema-conformant `candidates: []` — a short or unsuitable passage, a model that
  declines, or three post-parse rules that between them drop everything — counted as success, so the
  component would enter `Triaging` with nothing to triage, or index past the end.
  `GenerationFailure.Refused` existed with nothing specified to produce it.
- **Fix**: Treat a set below `MinCandidates`, empty included, as a `Refused` failure — passage
  retained, learner told — rather than entering the triage state.
- **Decision**: FIXED in Phase 2 §6. The failure path retains the passage exactly as a provider error
  does, which is what the learner needs, since the next thing they will do is add a focus hint and
  retry. This is now the only thing that produces `Refused`.

### F10 — Three numbers and one member with nothing behind them

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Lean Execution
- **Location**: Phase 1 §3, Phase 2 §5, §7
- **Detail**: (a) `CountForOwnerAsync` appeared to have no product caller; (b) `MaxCandidates` = 12 is
  unreachable — 12,000 characters is ~2,000 words and at one per 200 words the computation maxes at
  10, so the planned test "at the cap (very long passage → 12)" would assert against a passage the
  product refuses; (c) the PRD bounds submissions at "a few thousand words" and 12,000 characters
  sits at the low end of that, with the narrowing unrecorded.
- **Fix**: Reframe the cap as a defensive clip and correct its test; record the passage-bound
  reasoning.
- **Decision**: PARTLY FIXED, one bullet WITHDRAWN.
  - (a) **Withdrawn — reviewer error.** `CountForOwnerAsync` is the store's only read member, so
    dropping it would leave no way to write the account-boundary test `TenExCards.Tests/AGENTS.md`
    mandates from S-02 onward. It stays.
  - (b) FIXED. Phase 2 §5 reframes 12 as a defensive clip reached only by an over-producing model,
    corrects the test case, and records that it becomes reachable above ~14,400 characters.
  - (c) FIXED, with a caution the user's reasoning did not account for. The user's stated driver was
    the provider's limits plus minimal expected traffic. Recorded in Phase 2 §5: the provider's input
    window is very unlikely to be the binding constraint, and the two that are — the PRD's 30-second
    ceiling, which grows faster than input length because a longer passage also yields more
    candidates, and the B1 circuit memory budget — are per-request, so minimal traffic relieves
    neither. Any raise therefore happens after Phase 4's measurement and is re-measured against the
    ceiling.

## Post-triage state

- **Verdict after fixes**: SOUND.
- **Plan changes**: one new source file (`Generation/CandidateBounds.cs`), one new deterministic rule
  with its tests, three previously-invisible test-project files
  (`TenExCardsWebApplicationFactory.cs`, `StubCardCandidateGenerator.cs`, `MigrationGuardTests.cs`),
  six pinned mechanics, one new Phase 5 criterion (5.7), and one accepted risk with its premise and
  expiry recorded. Phase 2's sections renumbered: the old §8 is now §9.
- **Progress contract re-validated**: one `## Progress`, 5/5 phase titles matched, 39/39 criteria
  mapped, no stray checkboxes in phase bodies.
- **Approach unchanged.** No finding questioned the inside-out sequencing, the `ownerId`-first store,
  or the pure-function rule design.
