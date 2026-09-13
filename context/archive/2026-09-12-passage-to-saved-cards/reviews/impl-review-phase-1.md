<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Passage to Saved Cards

- **Plan**: `context/changes/passage-to-saved-cards/plan.md`
- **Scope**: Phase 1 of 5 — The card entity and the account boundary
- **Commit**: `9041291`
- **Date**: 2026-09-13
- **Verdict**: NEEDS ATTENTION (triaged 2026-09-13 — 6 fixed, 1 skipped)
- **Findings**: 0 critical, 1 warning, 6 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING → resolved (F1, F2, F3 fixed; F7 skipped by decision) |
| Architecture | PASS |
| Pattern Consistency | WARNING → resolved (F4, F5 fixed) |
| Success Criteria | PASS (F6 fixed) |

Six of six plan items land as specified — zero DRIFT, zero MISSING. The only code beyond the
contract is an `ownerId` guard, the test asserting it, and a client-side `Guid`. All five
"What We're NOT Doing" guardrails hold: nothing writes `CardOrigin.Manual`, there is no card
list surface, no entity or column can hold a passage, no logging was added, and the only new
DI registration is the store itself.

Automated criteria re-run on the committed tree: `dotnet build TenExCards/TenExCards.slnx`
0 warnings / 0 errors; `dotnet test` 21 passed / 0 failed. Manual criteria 1.5 and 1.6 were
confirmed by the user against `sqldb-tenexcards-dev` (six columns, `PK_Cards` + `IX_Cards_OwnerId`,
`FK_Cards_AspNetUsers_OwnerId` CASCADE, `20260912214732_AddCards` in `__EFMigrationsHistory`).

## Findings

### F1 — A scoped `AppDbContext` lives for the whole Blazor circuit, not the operation

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: `TenExCards/TenExCards/Cards/CardStore.cs:15`, `TenExCards/TenExCards/Program.cs:48`
- **Detail**: `CardStore(AppDbContext db)` takes the scoped context shim. In Blazor Server a DI
  scope is the **circuit**, not a request, so every interactive component injecting `ICardStore`
  shares one `AppDbContext` for the entire session. Two consequences, both landing in exactly the
  slice that saves many cards per circuit during triage:
  (1) **Change-tracker accumulation** — each `SaveAsync` leaves a tracked `Card` in a context never
  disposed until the circuit ends, crossing `TenExCards/AGENTS.md`'s "Never hold more in a circuit
  than you must";
  (2) **Concurrent use** — two overlapping component events (a progress tick plus an accept click)
  on one context throw `InvalidOperationException: A second operation was started on this context
  instance`, which per the plan's own note surfaces as the generic Blazor error UI with the
  untriaged batch lost behind it.
  The test file demonstrates the hazard by working around it: `CardOwnershipTests.cs:40-49` creates
  a fresh scope per store call precisely because sharing one "would let EF's change tracker answer a
  read that the database never saw." A real component has one circuit scope and cannot do that.
  **This is plan-conformant, not drift** — plan.md:538 pins the scoped registration. But its stated
  reason, *"it resolves `AppDbContext`, which is scoped, so nothing else is available to it,"* is
  factually wrong: `AddDbContextFactory<AppDbContext>` is registered at `Program.cs:39` and is the
  pattern this repository already has for exactly this problem.
- **Fix A ⭐ Recommended**: Depend on `IDbContextFactory<AppDbContext>` and create + dispose a
  context per method (`await using var db = await factory.CreateDbContextAsync(ct);`). Leave
  `AddScoped<ICardStore, CardStore>()` exactly as the plan pins it.
  - Strength: Removes both failure modes and makes the store lifetime-agnostic, while honoring the
    plan's pinned lifetime verbatim — the DI registration line does not change at all. Uses the
    factory registration `Program.cs:32-43` already exists for.
  - Tradeoff: One context per call instead of one per circuit; a few lines in `CardStore`, and the
    tests' per-call-scope helper becomes redundant rather than wrong.
  - Confidence: HIGH — the factory is registered, the pattern is documented in `Program.cs`'s own
    comment, and nothing calls the store yet so the blast radius is zero.
  - Blind spot: Not yet measured whether a per-call context adds meaningful latency on Azure SQL S0
    inside the 2s acknowledgement budget; connection pooling makes this very unlikely to matter.
- **Fix B**: Leave it as planned and revisit in Phase 3, when the component that injects the store
  actually exists.
  - Strength: Keeps Phase 1 exactly as reviewed and approved; defers a decision until there is a
    concrete call site to reason about.
  - Tradeoff: Phase 3 is the phase with the least slack, and the failure mode it would surface is a
    lost untriaged batch behind the generic error UI — the exact guardrail failure the plan spends
    several paragraphs preventing.
  - Confidence: MEDIUM — deferring is safe only if the revisit actually happens; nothing in the plan
    currently schedules it.
  - Blind spot: Whether Phase 3's component will inject `ICardStore` directly or go through another
    seam that would change the analysis.
- **Decision**: FIXED via Fix A

### F2 — `SaveAsync` accepts an undefined `CardOrigin` and persists it

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `TenExCards/TenExCards/Cards/CardStore.cs:32`, `TenExCards/TenExCards/Data/Card.cs:51-54`
- **Detail**: The explicit enum values (`Generated = 1`, `Manual = 2`) are correct and the `<remarks>`
  reasoning is sound — but the side effect is that `default(CardOrigin)` is `0`, naming no member.
  `SaveAsync` writes `origin` unvalidated, so a caller passing `default` silently persists an
  undefined discriminator that no constraint, migration or test would report. That is the same class
  of silent corruption the `<remarks>` block exists to prevent.
- **Fix**: Add `if (!Enum.IsDefined(origin)) throw new ArgumentOutOfRangeException(nameof(origin));`
  in `SaveAsync`, beside the existing `ownerId` guard.
- **Decision**: FIXED

### F3 — `ownerId` is guarded; `prompt` and `answer` are not

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `TenExCards/TenExCards/Cards/CardStore.cs:24` vs `:30-31`
- **Detail**: `ArgumentException.ThrowIfNullOrWhiteSpace(ownerId)` guards one of three string
  parameters at the same boundary. A null or blank prompt reaches SQL and fails as a
  `DbUpdateException` on the `NOT NULL` constraint. `AppDbContext.cs:54-61` states that "nothing
  else stands between a generated candidate and this table" and that such a throw lands inside a
  circuit event handler, losing the whole untriaged batch. Length enforcement is deliberately
  Phase 2's `CandidateBounds`, so this is only about the null/blank case.
- **Fix**: Add `ArgumentException.ThrowIfNullOrWhiteSpace` for `prompt` and `answer`. Do not add
  length checks here — those belong to Phase 2's `CandidateBounds`, and duplicating them would
  create the drift the plan's `MaxPromptCharacters`/`MaxAnswerCharacters` rule forbids.
- **Decision**: FIXED

### F4 — `AddCards.Down()` carries no forward-only warning, and it drops real product data

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `TenExCards/TenExCards/Migrations/20260912214732_AddCards.cs:42-47`
- **Detail**: `20260910190508_InitialSpine.cs:42-49` attaches a `<remarks>` block to its `Down()`
  — "EF generated this; it was not authored, and nothing relies on it. Migrations in this project
  are FORWARD-ONLY … Do not read the presence of this method as a rollback story." `AddCards` has
  only `/// <inheritdoc />`, and its `Down()` is a `DropTable("Cards")`. `DropSpineProbes` also
  omits the block, so the precedent is split 1-of-2 — but `AddCards` is the first migration whose
  `Down()` would destroy real learner data, which makes it the one that most warrants the warning.
- **Fix**: Copy `InitialSpine`'s `<remarks>` block onto `AddCards.Down()`, adding that this one
  drops every saved card.
- **Decision**: FIXED

### F5 — Test helper creates users whose `UserName` never equals their `Email`

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `TenExCards/TenExCards.Tests/CardOwnershipTests.cs:32-33`
- **Detail**: Two separate `Guid.NewGuid()` calls, so `UserName` and `Email` never match.
  `TenExCards.Tests/AGENTS.md` singles out "a registered user's `UserName` must equal the submitted
  email address" as the invariant whose violation would reopen silently, and `PasswordStorageTests`
  uses one `email` variable for both. Nothing this file asserts is affected — only `user.Id` is used
  — but it contradicts the class's own `<remarks>` claim that the users are "the same shape the
  application will actually pass," and helpers get copied.
- **Fix**: Hoist one `var email = $"owner-{Guid.NewGuid():N}@example.com";` and assign both fields.
- **Decision**: FIXED

### F6 — `CountForOwnerAsync`'s blank-owner guard has no test behind it

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `TenExCards/TenExCards.Tests/CardOwnershipTests.cs:121`
- **Detail**: `SaveAsync_WithNoOwner_IsRefused` covers `SaveAsync` only. `CountForOwnerAsync`
  carries the identical `ThrowIfNullOrWhiteSpace` guard at `CardStore.cs:43` with nothing asserting
  it, so removing that guard would not go red — the same "a boundary test that has never been
  observed failing is not a gate" argument the phase's own criterion 1.4 makes.
- **Fix**: Add a matching `[Theory]` case for `CountForOwnerAsync`.
- **Decision**: FIXED

### F7 — Random `Guid` as the clustered primary key

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `TenExCards/TenExCards/Cards/CardStore.cs:28`
- **Detail**: `Id = Guid.NewGuid()` with `Id` as the clustered PK (`AddCards.cs:27`,
  `uniqueidentifier`, no `NEWSEQUENTIALID()` default) produces random insert positions, causing page
  splits and index fragmentation on Azure SQL S0. Negligible at MVP volumes.
  **Correcting the obvious fix**: `Guid.CreateVersion7()` would *not* help here. SQL Server compares
  `uniqueidentifier` starting from the last six bytes, and a v7 GUID carries its timestamp in the
  first six — so time-ordered .NET GUIDs land in the part SQL Server sorts last. A real fix means a
  `NEWSEQUENTIALID()` column default or a non-clustered PK with a clustered index elsewhere, both of
  which are schema changes on a forward-only path.
- **Fix**: Skip. The cheap fix does not work and the working fix is a migration; revisit only if
  card volume ever makes fragmentation measurable.
- **Decision**: SKIPPED

## Triage outcome — 2026-09-13

All seven findings triaged in one pass. Six fixed, one skipped by decision.

| Finding | Decision |
| --- | --- |
| F1 — scoped `AppDbContext` lives for the circuit | FIXED via Fix A — `CardStore` now takes `IDbContextFactory<AppDbContext>` and creates/disposes a context per member. `AddScoped<ICardStore, CardStore>()` is unchanged, so the plan's pinned lifetime holds verbatim. |
| F2 — undefined `CardOrigin` persisted | FIXED — `Enum.IsDefined` guard in `SaveAsync`. |
| F3 — unguarded `prompt`/`answer` | FIXED — `ThrowIfNullOrWhiteSpace` for both. Length checks deliberately left to Phase 2's `CandidateBounds`. |
| F4 — `Down()` missing the forward-only remark | FIXED — `InitialSpine`'s block copied across, with a sentence naming that it drops every saved card. |
| F5 — test helper `UserName` != `Email` | FIXED — one hoisted `email` variable. |
| F6 — `CountForOwnerAsync` guard untested | FIXED — matching `[Theory]` case added. |
| F7 — random `Guid` clustered PK | SKIPPED — `Guid.CreateVersion7()` does not fix this (SQL Server orders `uniqueidentifier` from the last six bytes; a v7 GUID carries its timestamp in the first six). A real fix is a `NEWSEQUENTIALID()` default or a non-clustered PK, both schema changes on a forward-only path, for a problem not yet measurable. |

After the fixes: `dotnet build` 0 warnings / 0 errors; `dotnet test` **23 passed, 0 failed** (up from 21 — F6 added two theory cases). Criterion 1.4 was re-verified against the refactored store: removing the owner filter turns two tests red, restoring it turns them green.
