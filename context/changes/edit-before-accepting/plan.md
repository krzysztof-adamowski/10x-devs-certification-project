# Edit Before Accepting Implementation Plan

## Overview

`S-03` adds one affordance to a loop that already works: the learner corrects a candidate's wording
and then accepts the corrected version (FR-008). It also lands the externally required
learner-perspective end-to-end test, which `context/foundation/roadmap.md` moved onto this slice
from `S-02` on 2026-09-13 — because US-01's first acceptance criterion demands **accept, reject and
edit** at equal prominence, and edit did not exist until now.

Three things make this more than a textarea. The column bounds that `CandidateBounds` enforces on
generated candidates are not enforced anywhere on an edited one. The Secondary success criterion —
"fewer than a quarter of accepted cards are edited before saving" — is only observable at the
moment of acceptance, because the candidate is discarded immediately afterwards, so the fact has to
be captured here even though `S-06` owns the reporting. And the end-to-end test needs the
application running with deterministic candidates, which the Gemini free tier (20 requests per day
per model, non-deterministic prose) cannot supply.

## Current State Analysis

The triage surface is one `@rendermode InteractiveServer` component holding the whole batch in its
own state, because navigating between routes would dispose it.

- `TenExCards/TenExCards/Components/Pages/Generate.razor:63-88` renders the current candidate
  read-only — `<h2>@Current.Prompt</h2>`, `<p>@Current.Answer</p>` — above a `d-flex gap-2` row of
  two `btn btn-primary` buttons, each `style="min-width: 10rem"`. Equal prominence between accept
  and reject is achieved by literal sameness of class and width, not by a shared CSS rule.
- `TenExCards/TenExCards/Components/Pages/Generate.razor.cs:157-198` holds `AcceptAsync`,
  `RejectAsync` and `AdvanceAsync`. `AcceptAsync` catches every exception from `SaveAsync`, sets
  `_saveError` and **returns without advancing**, so a retry re-issues the same save rather than
  queueing a second one. `AdvanceAsync` moves `_stage` to `Summary` **before** its interop await,
  because Blazor renders at an event handler's first yielding await.
- `TenExCards/TenExCards/Generation/CandidateCard.cs` is `record CandidateCard(string Prompt,
  string Answer)` — immutable, so an edit needs either a buffer or a `with` expression.
- `TenExCards/TenExCards/Generation/CandidateBounds.cs` drops over-long candidates rather than
  truncating them, and it sits between the generator and the pending list. Nothing sits between an
  edit and `SaveAsync`.
- `TenExCards/TenExCards/Data/AppDbContext.cs:47-49` sets `Prompt` and `Answer` `HasMaxLength` from
  `CardBounds.MaxPromptCharacters` / `MaxAnswerCharacters`, the `const int` pair the `card-bounds`
  change extracted, and `CandidateBoundsTests` asserts the two equal — the EF in-memory provider
  ignores `HasMaxLength`, so nothing else would catch a drift. **This slice assumes `card-bounds`
  has landed**: before it, those numbers were settable `GenerationOptions` properties and every
  signature below would need a different shape.
- `TenExCards/TenExCards/Data/Card.cs` carries `Origin` and no edit state. `Origin` was pre-placed
  by `S-02` for `S-05`; the same reasoning applies to the edit flag here.
- `TenExCards/TenExCards/Cards/ICardStore.cs` requires `ownerId` on every member. There is no
  ambient-user overload and there must not be one.
- `TenExCards/TenExCards/Program.cs` throws at boot on a missing `Gemini:ApiKey` and on an empty
  `Gemini:Models`, both unconditionally, then registers `GeminiCardCandidateGenerator` as a
  singleton. It already carries one test-only seam — `Testing:SkipStartupMigration`, which
  **defaults to running the migration**.
- `.github/workflows/deploy.yml` runs `dotnet test` on `TenExCards.Tests` between `Set up .NET` and
  `Publish`. A failing test stops the deploy; this was verified rather than assumed.
- There is no browser-driven test anywhere, and `context/foundation/tech-stack.md` names no tool for
  one.

Missing, and added here: the edit sub-state, a bounds gate on the edit path, the `Edited` column,
and the end-to-end project.

## Desired End State

A signed-in learner mid-triage sees three buttons of identical size and weight: **Keep this card**,
**Discard this card**, **Edit wording**. Choosing the third replaces the card body in place with two
bounded, counted fields seeded from the generated text, offering **Keep this card** and **Cancel**.
Keep saves the edited text and advances to the next candidate; Cancel restores the generated wording
and returns to the read-only view, with every original triage option intact. An accepted card that
differs from what was generated is stored with `Edited = true`; one accepted untouched is stored
`false`. A card can never be saved empty or over the column bounds, and a save failure leaves the
learner in edit mode with their typing intact.

Alongside it, `TenExCards.E2E` drives a real browser through the whole learner journey — register,
paste, reject one candidate, edit and accept another, accept a third untouched, read the summary —
against a locally started application with a scripted generator. It runs in CI without gating the
deploy.

Verification: `## Testing Strategy` below, plus Phase 4's live checks.

### Key Discoveries:

- **`CandidateBounds` cannot see an edit.** `Generate.razor.cs:164` calls
  `Store.SaveAsync(_ownerId!, Current.Prompt, Current.Answer, …)` directly. Introducing an edit path
  without a gate hands Azure SQL a 600-character prompt against a `nvarchar(500)` column — it throws
  rather than truncating, which surfaces as the existing `_saveError` alert and blocks the learner
  on a card only they can fix. Silent truncation would be worse and is forbidden by the
  no-silent-loss guardrail.
- **The edit fact has exactly one observable moment.** `AdvanceAsync` removes the candidate from
  `_pending`, and the passage it came from is already gone. Nothing after acceptance can reconstruct
  whether the text was changed, so `S-06` cannot backfill it.
- **The accept path's failure handling is load-bearing and must be preserved.** The comment at
  `Generate.razor.cs:167-170` explains why it never advances and never moves to `Failed`: `Failed`
  would re-render a passage that is gone by design. Edit mode inherits that constraint.
- **`Program.cs` boots before any DI replacement can run.** The `Gemini:ApiKey` and `Gemini:Models`
  guards throw during `builder`, so a harness that only swaps `ICardCandidateGenerator` still needs
  both settings present. `TenExCards.Tests/AGENTS.md` records the day this cost a CI failure.
- **`--no-launch-profile` alone defaults to Production**, where the Key Vault guard runs and every
  framework asset 500s. Any spawned application process must set `ASPNETCORE_ENVIRONMENT=Development`
  explicitly.
- **A `200` is not evidence of a restart.** `context/foundation/lessons.md` records a poll returning
  `200` within one second of `az webapp restart` — the old container still serving, the genuine
  `Application started` line arriving two minutes later.

## What We're NOT Doing

- **Finding, editing or deleting an already-saved card.** That is `S-04` (`manage-saved-cards`,
  FR-009/010/011). This slice edits a *candidate*, which has never been persisted. No card list, no
  route to one.
- **Creating a card by hand.** `S-05` (`manual-card-entry`). `CardOrigin.Manual` still has no
  writer.
- **Reporting the acceptance rate, origin share or edit rate.** `S-06` (`outcome-recording`,
  FR-013). This slice *records* the edit flag and displays nothing; FR-013's own scope note says the
  requirement is that the outcomes are recorded, not that they are shown.
- **Recording rejections, or anything about a candidate that was not accepted.** `S-06`'s unresolved
  constraint — whether recording a rejection may retain passage-derived content — stays unresolved,
  and nothing here depends on it.
- **Persisting an in-progress edit, or resuming one after a refresh.** The edit buffer lives in the
  circuit and dies with it, exactly as the untriaged batch does. Forbidden by
  `TenExCards/AGENTS.md`.
- **Re-running generation on an edited candidate**, or any "improve this for me" affordance. Editing
  is the learner's own wording, with no second model call.
- **Any friction discouraging edits.** The roadmap records the hazard — editing quietly becomes the
  path of least resistance — and the PRD's answer is to measure it, not to obstruct it. US-01
  mandates equal effort; adding a confirmation step to edit would break the same rule the reject
  path is protected by.
- **Making the end-to-end test gate the deploy.** It runs in CI with `continue-on-error: true`.
  Revisiting that is a decision for after it has a track record, not part of this slice.
- **Testing generated prose.** `TenExCards.Tests/AGENTS.md` forbids it and the end-to-end test
  drives a scripted generator precisely so it never asserts on a model's output.
- **Deploying `infra/main.bicep`, or setting any app setting.** No new secret, no new resource, no
  new pointer. The one schema change travels in the application's own forward-only migration.
- **A backfill of `Edited` for existing cards.** Rows written before this migration get `false`,
  which is the truthful value: nothing edited them.

## Implementation Approach

The same inside-out order `S-02` used, for the same reason — the irreversible thing goes first,
while nothing depends on it.

**Phase 1 takes the migration alone.** `Card.Edited` and the `ICardStore.SaveAsync` parameter land
with the existing caller passing `false`, so the forward-only step is rehearsed against
`sqldb-tenexcards-dev` and merged before the code that gives it meaning exists. If it goes wrong,
the change that has to be undone is one column and one argument.

**Phase 2 is the whole user-visible slice**, and it is deliberately thin in the component. The two
decisions worth testing — *is this within the column bounds* and *was this actually edited* — become
pure functions beside the existing `CandidateBounds`, because a `@rendermode InteractiveServer`
component cannot be driven by the HTTP harness. That constraint is why `S-02`'s component ended up
thin, and it applies unchanged.

**Phase 3 builds the harness and the test.** The application gains a `#if DEBUG` E2E block — a
scripted generator, an in-memory store, no migration — that is *physically absent from a Release
build*, so the seam cannot exist in the deployed binary. Playwright then drives the real page.

**Phase 4 verifies on the deployed instance**, including that both flag values reach the app's
database correctly, because that is the one thing a local run with an in-memory provider cannot
prove.

**Phase 5 writes the record back**, including the new project in the paths convention and the rule
that keeps the E2E seam out of Release.

## Critical Implementation Details

**Ordering: no app setting precedes anything here.** Unlike `S-01` and `S-02`, this slice introduces
no configuration the deployed app reads. `Testing:E2E` is Development-only and Debug-only; the
production App Service never sees it. There is therefore no "set it before the merge" window to
manage.

**Every phase commit deploys to production, and Phase 1's is a schema change.** `main` is
production. Phase 1's `AddCardEdited` runs on the production boot path at the end of Phase 1, before
anything reads the column. The premise `S-02` recorded still holds — this instance has no learners,
so a failed boot costs the implementer's time rather than a learner's data — but the migration
confirmation from the **startup log** is kept as a Phase 1 manual criterion anyway, because it is
cheap and because `context/foundation/lessons.md` requires a restart to be proven from the log
rather than from a `200`.

**State sequencing: the edit buffer commits only on a successful save.** `_pending[0]` is not
replaced when the learner presses Keep in edit mode. The handler validates, computes the edit flag
against the *original* candidate, and calls `SaveAsync` with the trimmed buffer; only after that
returns does `AdvanceAsync` run. A failure therefore leaves the buffer, the edit mode and the
original candidate all intact, and a retry re-issues the same save — which is the invariant the
existing accept path already protects.

**Lifecycle: leave the state consistent before the await.** `AdvanceAsync` already moves `_stage` to
`Summary` before its interop await for this reason. The edit sub-state must be cleared on the same
side of that boundary: `_editing` goes false, and the buffers are cleared, before any await that can
yield, or the renderer runs with an edit mode pointing at a candidate that is no longer there.

**User experience: equal prominence is achieved by literal sameness.** The three buttons carry the
same class and the same `min-width`. Do not distinguish edit with an outline or a smaller size —
US-01's first acceptance criterion is the specification, and the end-to-end test asserts the three
controls are siblings in one row.

**Debug & observability: nothing about an edit is logged.** The edited text is derived from the
passage, and the passage is unrecoverable by design. The flag reaches the database and nowhere else.

---

## Phase 1: The `Edited` column

### Overview

One forward-only migration and the store signature that carries the flag, landed while no caller
means anything by it. Existing rows and the existing call site both get `false`.

### Changes Required:

#### 1. The entity

**File**: `TenExCards/TenExCards/Data/Card.cs`

**Intent**: Record whether the learner changed the generated wording before accepting, so the
Secondary success criterion has a source. `Origin` answers *where the card came from*; this answers
*whether the learner had to intervene*, and FR-013 wants both independently.

**Contract**: `public bool Edited { get; set; }` — non-nullable, defaulting to `false`. No
relationship, no index: `S-06` reads it by aggregate over an owner's cards, which the existing
`OwnerId` index already serves.

#### 2. The store

**File**: `TenExCards/TenExCards/Cards/ICardStore.cs`, `TenExCards/TenExCards/Cards/CardStore.cs`

**Intent**: Carry the flag through the only type that touches the `Cards` table, keeping the rule
that every member takes `ownerId` explicitly.

**Contract**: `SaveAsync` gains `bool edited` immediately after `origin` and before `ct`, and
`CardStore` assigns it to the new property. No validation is added for it — a boolean has no invalid
value. The existing argument guards stay exactly as they are.

#### 3. The existing caller

**File**: `TenExCards/TenExCards/Components/Pages/Generate.razor.cs`

**Intent**: Keep the application compiling and behaving identically; Phase 2 is what gives the
argument a real value.

**Contract**: The `Store.SaveAsync(...)` call in `AcceptAsync` passes `edited: false`, named rather
than positional so the next reader does not have to count arguments.

#### 4. The migration

**File**: `TenExCards/TenExCards/Migrations/<timestamp>_AddCardEdited.cs` (generated)

**Intent**: Add the column to the app's database on the boot path, forward-only.

**Contract**: Generated with
`dotnet ef migrations add AddCardEdited --project TenExCards/TenExCards/TenExCards.csproj`. It must
contain exactly one `AddColumn<bool>` against `Cards` with `nullable: false` and
`defaultValue: false`, and must touch no Identity table and no `DataProtectionKeys`. If it contains
anything else, the model snapshot has drifted and that is the defect to fix — not the migration.

#### 5. The tests

**File**: `TenExCards/TenExCards.Tests/CardOwnershipTests.cs`

**Intent**: Update the call sites for the new parameter and pin that the flag round-trips, so a
later refactor cannot quietly drop it on the way to the database.

**Contract**: Existing `SaveAsync` calls pass `edited: false`. One new `[Fact]` saves two cards for
one owner — one `edited: true`, one `edited: false` — and reads them back through `AppDbContext`
scoped to that owner, asserting both values survive. Assertions use AwesomeAssertions, never
FluentAssertions.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build TenExCards/TenExCards.slnx`
- Tests pass: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
- The migration adds exactly one column to `Cards` and alters no Identity or Data Protection table,
  read from the generated `Up()` body
- The new round-trip test is observed failing with the assignment in `CardStore` removed

#### Manual Verification:

- `dotnet ef database update` against `sqldb-tenexcards-dev` applies cleanly, and the `Cards` table
  shows `Edited` as `bit NOT NULL` with a `false` default
- Existing rows in the dev database read `Edited = 0` after the migration, with no error and no
  backfill
- After the phase commit reaches `main`, the App Service startup log names `AddCardEdited` as the
  one pending migration applied — read from a fresh `Application started` line, never from a `200`

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 2: The edit affordance

### Overview

The user-visible slice: a third button at equal prominence, an in-place edit mode with bounded and
counted fields, and an accept path that validates, computes the flag and preserves the existing
failure behaviour.

### Changes Required:

#### 1. The bounds gate, reachable for one candidate

**File**: `TenExCards/TenExCards/Generation/CandidateBounds.cs`

**Intent**: Give the edit path the same column-bounds check the generated path already gets, from
the same type, so the two can never disagree about what fits.

**Contract**: Add `public static bool IsWithinColumnLimits(string prompt, string answer)` and have
the existing `WithinColumnLimits(...)` filter call it, rather than repeating the two length
comparisons. Both read `CardBounds` — the `card-bounds` change landed the constants and removed the
`GenerationOptions` parameter, so neither method takes one. Behaviour of the existing method is
unchanged; `CandidateBoundsTests` must still pass untouched.

#### 2. The edit comparison

**File**: `TenExCards/TenExCards/Generation/CandidateEdit.cs` (new)

**Intent**: Decide whether committed text counts as an edit. It is a pure function in its own type
for the same reason the other three deterministic rules are — it is the only part of this slice the
test suite can assert directly.

**Contract**:
`public static bool WasEdited(CandidateCard original, string prompt, string answer)` — true when
either field differs from the original by an **ordinal** comparison after `Trim()` on both sides.
Culture-sensitive comparison is wrong here: it would treat some genuinely different strings as
equal. A whitespace-only difference is deliberately not an edit; a case change is.

Also `public static bool IsCommittable(string prompt, string answer)` — true when neither field is
null-or-whitespace and `CandidateBounds.IsWithinColumnLimits` passes. This is the single predicate
both the disabled attribute and the click handler consult, so they cannot drift.

#### 3. The triage markup

**File**: `TenExCards/TenExCards/Components/Pages/Generate.razor`

**Intent**: Offer edit beside accept and reject at equal prominence, and swap the card body for the
editor in place when it is chosen.

**Contract**: Within the `Stage.Triaging` branch, the card body renders read-only exactly as today
when not editing, and as two labelled controls when editing — a `<textarea>` for the prompt and a
`<textarea>` for the answer, both `@bind:event="oninput"`, each with a character counter below it
carrying `text-danger` when over its bound, mirroring the compose form's counter markup.

The button row keeps `d-flex gap-2 mb-3`. Read-only mode shows three `btn btn-primary
style="min-width: 10rem"` buttons: `Keep this card`, `Discard this card`, `Edit wording`. Edit mode
shows `Keep this card` — same class and width, `disabled="@(!CanCommitEdit)"` — and `Cancel`. The
"cards you have not decided on are discarded" line stays visible in both modes; the `_saveError`
alert renders above the card in both.

#### 4. The component state

**File**: `TenExCards/TenExCards/Components/Pages/Generate.razor.cs`

**Intent**: Hold the edit buffer, validate it, and commit it without disturbing the failure
behaviour the accept path already has.

**Contract**: Three fields — `bool _editing`, `string _editPrompt`, `string _editAnswer` — plus
`bool CanCommitEdit => CandidateEdit.IsCommittable(_editPrompt, _editAnswer)` and the two per-field
over-limit properties the counters read, which compare against `CardBounds` rather than `Options`.

`BeginEdit()` seeds both buffers from `Current` and sets `_editing`. `CancelEdit()` clears them and
unsets it; the candidate itself was never touched, so nothing is restored.

`AcceptAsync` becomes the single commit path for both modes. It resolves prompt and answer from the
buffers when `_editing` and from `Current` otherwise; when `_editing`, it re-checks
`CandidateEdit.IsCommittable` and returns with a validation message if it fails — the disabled
attribute is a courtesy and can be removed in dev tools, exactly as the compose form's comment
already says. It computes `edited` via `CandidateEdit.WasEdited` against `Current` and passes the
**trimmed** text to `SaveAsync`. The existing `catch` stays byte-for-byte in behaviour: set
`_saveError`, do not advance, do not move to `Failed`, and — new — do not leave edit mode, so the
learner's typing survives a retry.

`AdvanceAsync` clears `_editing` and both buffers **before** the interop await, alongside the
existing `_stage` assignment, for the reason the comment there already gives.

`RejectAsync` is unchanged and unreachable from edit mode by design; `Cancel` returns the learner to
the read-only view where it is one click away.

#### 5. The triage step, extracted so "exactly once" can be asserted

**File**: `TenExCards/TenExCards/Generation/TriageSession.cs` (new),
`TenExCards/TenExCards/Components/Pages/Generate.razor.cs`

**Intent**: Make the one invariant this product newly introduced testable. Carried over from `S-02`
by an explicit decision on 2026-09-13, recorded here because `S-02`'s own plan is archived with its
change.

**Why it is this slice's work rather than a refactor for its own sake.** `TenExCards.Tests/AGENTS.md`
names four invariants to assert; `S-02` covered three and left **"each candidate is triaged exactly
once"** uncovered, because the counters live inside a `@rendermode InteractiveServer` component the
HTTP harness cannot drive. That gap was not theoretical: `S-02`'s full-plan review found a
re-entrancy hole which was exactly this invariant being violated — a second click during an
in-flight save re-read the same candidate, saved it twice and advanced past the next one unseen. It
was closed with a `_busy` guard and `TryBeginTriage()`, and **that guard is still unasserted**. This
slice reworks the same loop to add a third button and an edit mode, so the extraction is work the
phase wants anyway.

**Contract**: A plain `TriageSession` type holding the pending candidates and the saved/discarded/
edited counts, exposing the decisions (`Accept`, `Reject`, and this slice's edit-then-accept) plus
`Current`, `Position` and `IsComplete`. It performs **no** I/O — the component keeps `SaveAsync`,
the JS interop and the `_busy` guard — so it is directly testable with no component framework and
without widening the test-double rule. `Generate.razor.cs` delegates to it instead of manipulating
`_pending`, `_saved`, `_discarded` and `_triagedCount` itself.

**The assertion that closes the gap**: over a batch of N candidates, any sequence of decisions
triages each exactly once — no candidate decided twice, none skipped, and the counts summing to N.
Include a repeated-decision case, which is what the re-entrancy bug produced.

#### 6. The tests

**File**: `TenExCards/TenExCards.Tests/CandidateEditTests.cs` (new),
`TenExCards/TenExCards.Tests/CandidateBoundsTests.cs`

**Intent**: Pin the two deterministic rules this phase introduces.

**Contract**: `CandidateEditTests` covers `WasEdited` — identical text is not an edit; text differing
only by leading or trailing whitespace is not an edit; a changed word in either field is an edit; a
case change is an edit — and `IsCommittable` — empty or whitespace-only prompt or answer is not
committable, text at exactly the bound is, one character over is not. `CandidateBoundsTests` gains
coverage of the new single-candidate overload at the boundary, keeping its assertion that the
`CardBounds` constants equal the `HasMaxLength` values.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build TenExCards/TenExCards.slnx`
- Tests pass: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
- `CandidateEditTests` observed failing with `Trim()` removed from `WasEdited`, proving the
  whitespace case is actually asserted
- `AuthBoundaryTests` still shows `/generate` answering `302` to the login path

#### Manual Verification:

- The three read-only buttons are visually identical in size and weight, in one row, and the page
  offers no confirmation step on any of them
- Editing a candidate and pressing Keep saves the edited text — confirmed by querying
  `sqldb-tenexcards-dev` for the row, which shows the new wording and `Edited = 1`
- Accepting a candidate untouched stores `Edited = 0`
- Entering edit mode, changing nothing, and pressing Keep stores `Edited = 0`
- Cancel restores the generated wording and returns all three original actions
- Emptying either field, or pasting past the bound, disables Keep and turns the matching counter
  red; discard and cancel stay available
- With the disabled attribute removed in dev tools, Keep on an invalid edit is refused by the
  handler and reports why
- A forced save failure mid-edit keeps the learner in edit mode with their typing intact, and a
  retry saves once

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 3: The learner-perspective end-to-end test

### Overview

The test `context/foundation/roadmap.md` moved onto this slice: a real browser walking US-01 from
registration to saved cards. It needs the application running with deterministic candidates, which
this phase builds as a `#if DEBUG` harness that cannot exist in a Release binary.

### Changes Required:

#### 1. The scripted generator

**File**: `TenExCards/TenExCards/Generation/ScriptedCardCandidateGenerator.cs` (new)

**Intent**: Return a fixed candidate set so the end-to-end test can assert on what it sees. It lives
in the application project because the harness that registers it does, and it is wrapped so it
compiles only in Debug.

**Contract**: The whole file sits inside `#if DEBUG` / `#endif`. It implements
`ICardCandidateGenerator`, reports one progress chunk, and returns
`GenerationResult.Success(...)` with **three** candidates whose prompts are distinguishable from
each other — the test rejects the first, edits and accepts the second, and accepts the third
untouched, so the three triage outcomes are each exercised once. Candidate text stays well inside
the column bounds, because the test appends to it.

#### 2. The harness seam

**File**: `TenExCards/TenExCards/Testing/E2EHarness.cs` (new),
`TenExCards/TenExCards/Program.cs`

**Intent**: Let a locally started application serve the whole learner flow with no model, no
database and no Key Vault, without putting any of that machinery in the deployed binary.

**Contract**: `E2EHarness.cs` is entirely within `#if DEBUG` and exposes a single extension,
`static bool UseE2EHarness(this WebApplicationBuilder builder)`. It returns `false` unless
**both** `builder.Environment.IsDevelopment()` and `Testing:E2E` are true; when it returns `true` it
has registered `ScriptedCardCandidateGenerator` as `ICardCandidateGenerator`, registered
`AppDbContext` and its factory against the EF in-memory provider with a unique database name, and
registered the scoped `AppDbContext` the Data Protection key store resolves.

`Program.cs` calls it once, early, into a local `var e2e`, and guards three existing regions with
`if (!e2e)`: the connection-string resolution and the two `AddDbContext*` registrations; the
`Gemini:ApiKey` and `Gemini:Models` guards together with the `GeminiCardCandidateGenerator`
singleton; and the boot-path migration block, whose existing `Testing:SkipStartupMigration` check is
left exactly as it is — the new flag is an additional reason to skip, never a change to that
setting's default. Outside Debug, `e2e` is a `const false` and the compiler removes the branches.

**Contract detail worth stating, because getting it wrong is silent**: `Microsoft.EntityFrameworkCore.InMemory`
is referenced by the application project with `Condition="'$(Configuration)' == 'Debug'"`. CI
publishes with `-c Release`, so the package is absent from the deployed archive and the harness code
is absent from the assembly. A reference without that condition would ship a test provider to
production and nothing would report it.

#### 3. The end-to-end project

**File**: `TenExCards/TenExCards.E2E/TenExCards.E2E.csproj` (new),
`TenExCards/TenExCards.slnx`

**Intent**: Hold the browser-driven test, separate from `TenExCards.Tests` so the deploy-gating
command cannot pick it up.

**Contract**: `net10.0`, `IsPackable=false`, referencing `xunit`, `xunit.runner.visualstudio`,
`Microsoft.NET.Test.Sdk`, `Microsoft.Playwright` and `AwesomeAssertions` — never FluentAssertions.
It does **not** reference `TenExCards.csproj`: it drives the application over HTTP, not in process.
Added to `TenExCards.slnx` so a solution build covers it.

#### 4. The application launcher

**File**: `TenExCards/TenExCards.E2E/AppUnderTest.cs` (new)

**Intent**: Start the application for the duration of a test class and stop it afterwards, on a
fixed loopback address the browser can reach.

**Contract**: An `IAsyncLifetime` fixture that spawns
`dotnet run --project TenExCards/TenExCards/TenExCards.csproj --no-launch-profile` with
`ASPNETCORE_ENVIRONMENT=Development`, `ASPNETCORE_URLS=http://127.0.0.1:5199`, `Testing__E2E=true`
and `DOTNET_ENVIRONMENT=Development` in its environment, then polls the root URL until it answers or
a 90-second budget expires, failing with the captured stdout and stderr rather than a bare timeout.
`ASPNETCORE_ENVIRONMENT` is set explicitly and is not optional: `--no-launch-profile` alone defaults
to Production, where the Key Vault guard runs and every framework asset 500s, and the page then
renders unstyled rather than failing outright. Disposal kills the process tree.

#### 5. The learner journey

**File**: `TenExCards/TenExCards.E2E/LearnerJourneyTests.cs` (new)

**Intent**: Assert US-01 from the learner's side, which is the form the requirement is stated in.

**Contract**: One `[Fact]` walking the whole journey against a fresh browser context: register a new
account with a unique address and a 16-character password; land signed in; navigate to `/generate`;
paste a passage; wait for triage; assert the three action controls are siblings in one row with
equal computed width, which is the machine-checkable form of "equal prominence"; discard the first
candidate; on the second, open the editor, change the prompt, press Keep; accept the third
untouched; assert the summary reports two saved and one discarded.

A second `[Fact]` asserts the refusal path: a passage over
`Generation:MaxPassageCharacters` is refused **before** generation begins — the generating state is
never entered and the message names the limit.

Selection is by accessible name and label, not by CSS class, so restyling does not break the test
and the assertions double as an accessibility check.

#### 6. The CI step

**File**: `.github/workflows/deploy.yml`

**Intent**: Run the journey on every push so it cannot rot, without letting a browser test block a
production deploy.

**Contract**: One step named for what it is, inserted after `Test` and before `Publish`, carrying
`continue-on-error: true` and its own `timeout-minutes`. It builds the E2E project, installs
Chromium with `--with-deps` via the generated `playwright.ps1`, and runs
`dotnet test TenExCards/TenExCards.E2E/TenExCards.E2E.csproj`. The existing `Test` step is not
touched — it still names `TenExCards.Tests` by path, so it cannot pick this project up. The header
comment block gains a sentence recording why this one step is non-gating, since every other
assertion in that file is.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build TenExCards/TenExCards.slnx`
- Existing tests pass unchanged: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
- The journey passes locally: `dotnet test TenExCards/TenExCards.E2E/TenExCards.E2E.csproj` after
  `playwright install chromium`
- A Release publish contains no harness: `dotnet publish -c Release` produces no
  `Microsoft.EntityFrameworkCore.InMemory.dll` in the output, and the published `TenExCards.dll`
  yields no `ScriptedCardCandidateGenerator` type
- `MigrationGuardTests` still passes, proving `Testing:SkipStartupMigration` still defaults to
  running the migration

#### Manual Verification:

- Starting the application by hand with `Testing__E2E=true` in Development serves `/generate` with
  no `Gemini:ApiKey`, no connection string and no Key Vault access
- Starting it **without** the flag still demands both settings and fails at boot naming them
- The CI run shows the new step executed, and a deliberately broken assertion makes that step red
  while `Publish`, `Pack`, `Deploy` and `Verify` still run and the deploy still completes
- The test is watched running headed at least once, confirming it exercises the real edit affordance
  rather than passing on a selector that matches nothing

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 4: Deploy and verify on the live instance

### Overview

The only checks a local run cannot make: that the column exists in the app's database, that both
flag values reach it, and that the edit affordance works on a B1 circuit over the real network.

### Changes Required:

#### 1. No code

**File**: —

**Intent**: This phase measures what Phases 1–3 already deployed. `main` is production and each
phase commit deployed itself; a change here would mean a phase's criteria were wrong.

**Contract**: If a defect is found, it is fixed as a commit scoped to the phase that introduced it,
not folded into this one.

### Success Criteria:

#### Automated Verification:

- The `main` workflow run is green across the full step list, with the non-gating E2E step's outcome
  recorded either way
- `scripts/verify_deploy.py` passes in that run

#### Manual Verification:

- The live `Cards` table has the `Edited` column, confirmed by query against `sqldb-tenexcards`
- On the live site: a candidate edited and accepted is stored with the new wording and `Edited = 1`;
  one accepted untouched is stored `Edited = 0` — both confirmed by query, not by the summary count
- An accepted edited card survives a page refresh
- The three actions are equally prominent in a real desktop browser at the deployed page's own
  width, and none of them asks for confirmation
- Over-bound and emptied edits are refused on the deployed circuit, not only locally
- No passage text and no candidate text appears in the App Service log stream during a full edit
  and accept

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 5: Update the repository record

### Overview

Four documents describe a repository that no longer exists as described: there is a third project, a
Debug-only harness, a non-gating CI step and a live edit path. Each fact goes in exactly one place.

### Changes Required:

#### 1. The repository guide

**File**: `TenExCards/AGENTS.md`

**Intent**: Make the new structure and the new prohibitions findable by the next agent before it
guesses.

**Contract**: The *Paths* convention names **three** project subfolders, not two, adding
`TenExCards/TenExCards.E2E/` and pointing at its own `AGENTS.md`. `## Testing` records that the
end-to-end project exists, runs in CI, and **does not** gate the deploy, with the reason. `## Never
do these` gains two product rules — never save an edited candidate without re-checking the column
bounds in the handler, and never add friction to editing that accept and reject do not also carry —
and one deployment rule: never make the E2E harness reachable from a Release build, naming the
`Condition` on the package reference and the `#if DEBUG` guard as the two things that enforce it
together.

#### 2. The unit-test rules

**File**: `TenExCards/TenExCards.Tests/AGENTS.md`

**Intent**: Keep the boundary between the two test projects explicit, since both now hold tests and
only one stops a deploy.

**Contract**: A short section saying what belongs here versus in `TenExCards.E2E`, and that
`Testing:E2E` is not this project's flag — this project's harness is
`TenExCardsWebApplicationFactory` and its own settings, unchanged.

#### 3. The end-to-end rules

**File**: `TenExCards/TenExCards.E2E/AGENTS.md` (new)

**Intent**: Hold the rules next to the tests they govern, as `S-01` did when it created
`TenExCards.Tests/AGENTS.md`.

**Contract**: Why it is non-gating and what would have to be true to change that; that it drives the
application over HTTP and never references the application project; the three environment variables
the launcher sets and why `ASPNETCORE_ENVIRONMENT` is not optional; that browsers must be installed
before a first local run; and that assertions select by accessible name, never by CSS class.

#### 4. The roadmap

**File**: `context/foundation/roadmap.md`

**Intent**: Close the slice and record what landed, including the outcome of the item this slice
inherited.

**Contract**: `S-03`'s `Status` flips to `done` in both the `## At a glance` row and the item body,
the frontmatter `updated` is bumped, and a `**Landed 2026-09-13:**` note records the edit
affordance, the `Edited` column as the signal `S-06` will read, the tool chosen for the
learner-perspective test and its non-gating posture. The "carries the externally required test" note
stays — it is the only durable record of the move from `S-02`.

#### 5. The lesson

**File**: `context/foundation/lessons.md`

**Intent**: Record the one rule here that generalises beyond this slice.

**Contract**: Append an entry on validating at the boundary the data actually crosses: generated
candidates were bounds-checked at the generator seam, and an edit path added later bypassed that
seam entirely while every existing test stayed green. The rule is that a validation gate belongs on
the call, not on one of the routes to it — with `CandidateBounds` and `SaveAsync` as the worked
example. Applies to plan, implement, impl-review.

### Success Criteria:

#### Automated Verification:

- Solution builds and tests pass
- `TenExCards/AGENTS.md` names three project subfolders and no longer implies two
- The new `TenExCards.E2E/AGENTS.md` exists and is referenced from `TenExCards/AGENTS.md`

#### Manual Verification:

- Every fact added lives in exactly one file — the E2E rules are not duplicated between
  `TenExCards/AGENTS.md` and `TenExCards.E2E/AGENTS.md`
- The roadmap, `S-03`'s entry and this plan tell the same story about what landed
- A reader who has never seen this repository can start the E2E suite from
  `TenExCards.E2E/AGENTS.md` alone

---

## Testing Strategy

### Unit Tests:

- `WasEdited`: identical text, whitespace-only difference, a changed word in the prompt, a changed
  word in the answer, a case-only change
- `IsCommittable`: empty and whitespace-only fields, text exactly at each bound, one character over
  each bound
- `CandidateBounds.IsWithinColumnLimits`: the boundary in both directions, and the existing
  assertion that the `CardBounds` constants still equal the `HasMaxLength` values
- `TriageSession`: over a batch of N, every candidate is decided exactly once and the counts sum to
  N; a repeated decision on the same candidate does not double-count or skip the next one — the
  "triaged exactly once" invariant `TenExCards.Tests/AGENTS.md` names, carried over from `S-02`
- `CardStore`: the `Edited` flag round-trips both values, inside the owner scope
- `MigrationGuardTests`: unchanged, and must stay passing — the new flag must not alter
  `Testing:SkipStartupMigration`'s default

### Integration Tests:

- `AuthBoundaryTests`: `/generate` still `302`s to the login path, not `401`
- The Release-publish check in Phase 3 is an integration assertion about the *artifact* rather than
  the code, and is the only thing standing between the harness and production

### Manual Testing Steps:

1. Sign in, generate a batch, and confirm the three actions are identical in size and weight
2. Edit a candidate's prompt, press Keep, and query the database for the row: new wording,
   `Edited = 1`
3. Accept the next candidate untouched and query it: `Edited = 0`
4. Open the editor on the third, change nothing, press Keep: `Edited = 0`
5. Open the editor, empty the answer, and confirm Keep disables while Cancel stays available
6. Remove the disabled attribute in dev tools and confirm the handler refuses the same edit
7. Cancel out of an edit and confirm the generated wording and all three actions return
8. Force a save failure mid-edit and confirm the typing survives and a retry saves exactly once
9. Repeat 2 and 3 on the deployed instance against `sqldb-tenexcards`

### What is deliberately not tested

The component itself is not driven by `TenExCards.Tests`, for the reason `S-02` recorded: a
`@rendermode InteractiveServer` component cannot be driven from the HTTP harness, which is why the
decisions worth asserting are pure functions outside it. That argument does **not** cover the
learner-perspective test, which is browser-driven and lands in Phase 3.

Generated prose is not asserted anywhere. The end-to-end test drives a scripted generator precisely
so that it never can.

## Performance Considerations

The edit buffer adds two strings per circuit, bounded at 500 and 1,000 characters — roughly 3 KB
against a per-circuit floor of about 250 KB before application state. It is held only while one
candidate is open for editing and cleared on advance. Nothing here changes the memory posture
`TenExCards/AGENTS.md` warns about.

`WasEdited` and `IsCommittable` run on keystroke-driven re-renders; both are two string comparisons
against text bounded at 1,500 characters combined, which is below the threshold at which any of this
is worth thinking about.

The end-to-end step adds a browser download and a full application start to CI. It sits behind
`continue-on-error` with its own `timeout-minutes`, so its worst case is a longer run, never a
blocked deploy.

## Migration Notes

`AddCardEdited` is forward-only, like every migration here. It adds one non-nullable `bit` column
with a `false` default, so existing rows are valid the moment it applies and no backfill is written
or needed. It is rehearsed against `sqldb-tenexcards-dev` before the phase commit reaches `main`,
and confirmed on production from the startup log rather than from a successful HTTP response.

The rollback path is redeploying the retained previous archive, and **that does not reverse
schema**. A previous binary is unaffected by the extra column — it neither reads nor writes it — so
an archive redeploy is a genuine rollback for Phases 2 onward and a partial one for Phase 1.

## References

- **Prerequisite change: `context/changes/card-bounds/plan.md`** — extracts the 500/1,000 bounds to
  `const int` in `CardBounds` and removes the `GenerationOptions` parameter from `CandidateBounds`.
  Phase 2 below is signed against that shape and must not start before it lands.
- Roadmap slice: `context/foundation/roadmap.md` — `S-03: Learner edits a candidate before accepting
  it`, including the record of the learner-perspective test's move from `S-02`
- Requirements: `context/foundation/prd.md` — FR-008, US-01 acceptance criteria, `## Success
  Criteria` Secondary, `## Business Logic`
- Prior slice: `context/changes/passage-to-saved-cards/plan.md` — the triage component, the
  inside-out phase order, and the production-deploy premise this plan reuses
- Repository rules: `TenExCards/AGENTS.md`, `TenExCards/TenExCards.Tests/AGENTS.md`
- Recurring rules: `context/foundation/lessons.md` — "Verify a restart from the log, never from the
  first 200", "Parallel sessions share one index"
- The triage loop as it stands: `TenExCards/TenExCards/Components/Pages/Generate.razor.cs:157-198`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not
> rename step titles. See `references/progress-format.md`.

### Phase 1: The `Edited` column

#### Automated

- [x] 1.1 Solution builds — 00b91cd
- [x] 1.2 Tests pass — 00b91cd
- [x] 1.3 Migration adds exactly one column to `Cards` and alters no Identity or Data Protection table — 00b91cd
- [x] 1.4 Round-trip test observed failing with the `CardStore` assignment removed — 00b91cd

#### Manual

- [x] 1.5 Migration applies cleanly to `sqldb-tenexcards-dev` with `Edited` as `bit NOT NULL` defaulting false — 00b91cd
- [x] 1.6 Existing dev rows read `Edited = 0` with no backfill — 00b91cd
- [x] 1.7 Production startup log names `AddCardEdited` as the one applied migration, read from a fresh `Application started` line — 54082ce

### Phase 2: The edit affordance

#### Automated

- [x] 2.1 Solution builds — 63f11ae
- [x] 2.2 Tests pass — 63f11ae
- [x] 2.3 `CandidateEditTests` observed failing with `Trim()` removed from `WasEdited` — 63f11ae
- [x] 2.4 `AuthBoundaryTests` still shows `/generate` answering `302` — 63f11ae

#### Manual

- [x] 2.5 Three read-only buttons identical in size and weight, none confirming — 54082ce
- [ ] 2.6 Edited accept stores the new wording with `Edited = 1`
- [ ] 2.7 Untouched accept stores `Edited = 0`
- [ ] 2.8 Editor opened and closed with no change stores `Edited = 0`
- [x] 2.9 Cancel restores the generated wording and all three actions — 54082ce
- [x] 2.10 Invalid edit disables Keep and reddens the counter; discard and cancel stay available — 54082ce
- [ ] 2.11 Handler refuses an invalid edit with the disabled attribute removed
- [ ] 2.12 Forced save failure keeps edit mode and the typing; retry saves once

### Phase 3: The learner-perspective end-to-end test

#### Automated

- [x] 3.1 Solution builds — 54082ce
- [x] 3.2 Existing tests pass unchanged — 54082ce
- [x] 3.3 Journey passes locally after `playwright install chromium` — 54082ce
- [x] 3.4 Release publish carries no in-memory provider and no `ScriptedCardCandidateGenerator` — 54082ce
- [x] 3.5 `MigrationGuardTests` still passes — 54082ce

#### Manual

- [x] 3.6 App serves `/generate` with `Testing__E2E=true` and no key, connection string or vault access — 54082ce
- [x] 3.7 Without the flag the app still fails at boot naming both Gemini settings — 54082ce
- [ ] 3.8 A deliberately broken assertion reddens the E2E step while the deploy still completes
- [ ] 3.9 The journey watched running headed at least once

### Phase 4: Deploy and verify on the live instance

#### Automated

- [x] 4.1 `main` workflow run green across the full step list — 54082ce
- [x] 4.2 `scripts/verify_deploy.py` passes in that run — 54082ce

#### Manual

- [x] 4.3 Live `Cards` table has the `Edited` column — 54082ce
- [ ] 4.4 Live edited accept stores `Edited = 1`; untouched accept stores `Edited = 0`
- [ ] 4.5 An accepted edited card survives a refresh
- [ ] 4.6 Three actions equally prominent in a real browser, none confirming
- [ ] 4.7 Over-bound and emptied edits refused on the deployed circuit
- [ ] 4.8 No passage or candidate text in the App Service log stream during a full edit and accept

### Phase 5: Update the repository record

#### Automated

- [x] 5.1 Solution builds and tests pass — f9fd25e
- [x] 5.2 `TenExCards/AGENTS.md` names three project subfolders — f9fd25e
- [x] 5.3 `TenExCards.E2E/AGENTS.md` exists and is referenced from `TenExCards/AGENTS.md` — f9fd25e

#### Manual

- [ ] 5.4 Every added fact lives in exactly one file
- [ ] 5.5 Roadmap `S-03`, its inherited-test note and this plan tell the same story
- [ ] 5.6 A newcomer can start the E2E suite from `TenExCards.E2E/AGENTS.md` alone
