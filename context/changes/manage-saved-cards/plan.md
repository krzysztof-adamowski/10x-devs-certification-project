# Manage Saved Cards Implementation Plan

## Overview

`S-04` gives the learner a way to **find one of their own saved cards in order to edit or delete
it** — `FR-009`, `FR-010`, `FR-011`. One gated interactive page at `/cards` carries a search box and
the learner's most recent cards; a found card is repaired or removed in place. Four new
owner-scoped members on `ICardStore` sit behind it, and the slice changes no schema.

The requirement was deliberately narrowed during shaping, and the narrowing is the hard part of
this plan rather than a footnote. `FR-009` was rewritten from "view every card" to "find a card in
order to edit or delete it", and `## Non-Goals` says the saved-card list "exists to find a card for
editing or deletion rather than to browse". Every scope decision below is downstream of that.

## Current State Analysis

`S-02` (`passage-to-saved-cards`) landed and deployed the write path on 2026-09-13. What exists:

- **`Card`** — `Id`, `OwnerId`, `Prompt` (max 500), `Answer` (max 1000), `Origin`, `CreatedAt`, with
  an index on `OwnerId` and a cascade delete from `AspNetUsers`. No passage column, by requirement.
- **`ICardStore`** — `SaveAsync` and `CountForOwnerAsync` only. Both take `ownerId` as a required
  first parameter; the interface comment states there is no ambient-user overload and no
  parameterless query. **Everything in the product to date is append-only**; this slice adds the
  first read-back, the first mutation and the first deletion.
- **`/generate`** — the only `@rendermode InteractiveServer` page, a five-stage state machine holding
  its batch in component state. Its summary screen deliberately links nowhere, because this surface
  did not exist.
- **Authorization defaults to protected.** A new page under `Components/Pages/` is gated with no
  attribute; an unauthenticated request must produce a `302` to `/Account/Login`, never a `401`.
- **`TenExCards.Tests`** gates the deploy. `CardOwnershipTests` is the shape for the account
  boundary; `PassageBounds` / `CandidateBounds` / `CandidateDeduplicator` are the shape for a
  decidable rule extracted below a component so it can be asserted without rendering anything.

What is missing: any way to read a card back, any way to change one, any way to remove one, and any
surface that lists them.

Two working-tree facts, current at planning time and not blockers: **`S-02`'s Phase 5 (the
repository record) is still open**, and `TenExCards/AGENTS.md`, `context/deployment/deploy-plan.md`
and `context/foundation/roadmap.md` all carry uncommitted changes. Phase 3 below touches
`AGENTS.md`, so the parallel-session rule in `context/foundation/lessons.md` applies directly to it.

## Desired End State

A signed-in learner opens `/cards` and sees their twenty most recent cards, newest first, each
showing its prompt and answer. Typing in the search box narrows to cards whose prompt *or* answer
contains what they typed, case-insensitively, capped at the same twenty. Any row offers **Edit** and
**Delete**. Edit turns that row into a form bound to the card's current text, bounded by the same
500/1000 limits the column enforces, saving in place. Delete turns into **Delete permanently /
Cancel** on that row, and the second click removes the card with no undo. A learner who owns no
cards is told so and pointed at `/generate`. A search that matches nothing says that, distinctly.

No card belonging to another account is reachable, findable, editable or deletable — and an attempt
is indistinguishable from a card that does not exist.

Verified by: the extended `CardOwnershipTests` passing; `/cards` answering `302` to an
unauthenticated request in `AuthBoundaryTests`; and, on the live instance, an edit surviving a
refresh and a two-account probe finding nothing across the boundary.

### Key Discoveries:

- **The account boundary belongs in the store, not above it.** `TenExCards.Tests/AGENTS.md` names
  "every query is scoped to the owning account" as the invariant the project exists to protect, and
  `ICardStore.cs:5-8` makes `ownerId` structurally unavoidable. Four new members must keep that
  shape.
- **The test provider and production disagree about case.** The EF in-memory provider used by every
  test is case-**sensitive**; Azure SQL's default collation is case-**insensitive**.
  `AppDbContext.cs:49-51` already carries a comment about the sibling half of this problem
  (`HasMaxLength` is ignored in-memory). A search built on the database collation would pass its
  tests and behave differently live.
- **The 500/1000 limits are `const int` on `CardBounds`**, read by `Card`'s `HasMaxLength`, by
  `CandidateBounds`, and by the JSON schema sent to the model. The `card-bounds` change put them
  there — before it they were settable `GenerationOptions` properties, and this plan's Phase 1 owned
  the extraction. It no longer does: a manual edit simply reads the constants, and **no settable
  mirror may be reintroduced**, because the value is compiled into `AddCards` and a key that widens
  the gate cannot widen the column.
- **An exception inside a circuit event handler is a guardrail failure, not an error report.**
  `Generate.razor.cs:175-181` catches its save failure precisely so the generic Blazor error UI never
  appears. The same applies to every handler on `/cards`.
- **A Blazor event handler renders at its first yielding `await`.** `Generate.razor.cs:200-207`
  records this the expensive way: the stage must move *before* the awaited call, or the component
  re-renders against state the markup can no longer address.
- **The PRD permits deletion outright.** `FR-011`'s resolution is that "the guardrail forbids
  *silent* loss, which a deliberate deletion is not" — so the design question was deliberateness,
  not undo.
- **The tracked edit rate is not this slice's edit.** The Secondary success criterion is "fewer than
  a quarter of accepted cards are **edited before saving**" — a candidate edited at triage, which is
  `S-03`. Nothing named requires a post-save edit marker, which is why no column is added here.

## What We're NOT Doing

- **No schema change and no migration.** Not `UpdatedAt`, not `EditCount`, not a soft-delete flag,
  not a concurrency token. Migrations run forward-only on the boot path with no deployment slots;
  this slice declines that risk because no named requirement buys anything with it.
- **No browse surface.** No paging, no page-size control, no sort control, no "show all", no card
  count as a headline. The cap and the absence of paging are what keep `## Non-Goals` out; they are
  load-bearing, not placeholders.
- **No decks, tags, folders, favourites, archiving or export.** All named Non-Goals.
- **No bulk operations** — no select-many, no delete-all.
- **No undo after deletion**, and no soft delete. A deliberate deletion is permitted to be final.
- **No editing a candidate before accepting it.** That is `S-03` (`edit-before-accepting`).
- **No manual card creation.** That is `S-05` (`manual-card-entry`). This slice does not write
  `CardOrigin.Manual` anywhere, and editing a `Generated` card does **not** change its `Origin` —
  origin records how a card was created, and `S-06` depends on that meaning.
- **No recording of edit or delete outcomes.** That is `S-06` (`outcome-recording`).
- **No optimistic concurrency between two tabs of the same learner.** Last write wins; a card
  deleted in one tab reports itself as gone in the other. Recorded as an accepted risk below.
- **No infrastructure change.** `infra/main.bicep` is not deployed and `what-if` is not run.
- **No new NuGet package**, and in particular no component-rendering test library.

## Implementation Approach

Built below the component first, as `S-02` was, and for the same reason: an
`@rendermode InteractiveServer` page cannot be driven by the `WebApplicationFactory` HTTP harness, so
anything decidable must live in a type that can be asserted without rendering.

Phase 1 does three things that are all "below the page". It adds `CardOptions` carrying the one
genuinely configurable number this slice introduces, the shared result cap — the card-length limits
are **not** here, having been extracted to `const int` by the `card-bounds` change this plan now
depends on. It adds a pure `CardEdit` rule that trims and bounds what the learner typed. And it
gives `ICardStore` its four new owner-scoped members, every one of them filtering on `ownerId`, with
"not yours" returning exactly what "does not exist" returns.

**Find is one method, not two.** `FindForOwnerAsync(ownerId, term, limit, ct)` treats a null,
empty or whitespace-only term as "most recent", so the default list and a search share a single
owner-filtered query. There is no second query shape that could be written without the filter.

Phase 2 is the page: one component, three per-row states, two distinct empty states. Phase 3 wires
the entry points and updates the repository record. Phase 4 deploys and performs the checks
`verify_deploy.py` structurally cannot make.

## Critical Implementation Details

**Timing & lifecycle.** The search runs per keystroke against the database, so a slower earlier
query can return *after* a faster later one and overwrite the newer results with stale ones. Each
new query must cancel the one before it — hold a `CancellationTokenSource` on the component, cancel
and replace it at the top of every search, and discard a result whose token was cancelled rather
than assigning it. This is invisible locally against `sqldb-tenexcards-dev` and shows up on the live
instance under Azure SQL latency.

**State sequencing.** Per-row state (which row is being edited, which is confirming a delete) must
be keyed by the card's `Id`, never by its index in the result list. A delete, an edit that changes
what a search matches, and a re-search all reorder or shorten that list while a row is open; an
index-keyed state then points at a different card than the learner was looking at. Re-running the
search after a successful edit or delete must also clear the open-row state first, so the refreshed
list cannot inherit a state belonging to a row that is no longer in it.

## Phase 1: `CardOptions` for the result cap; the store learns to read back

### Overview

Everything below the page: the `MaxResults` cap, the pure edit rule, and four owner-scoped store
members with the tests that prove the boundary holds on each of them. No migration, and no
user-visible change — at the end of this phase the application behaves exactly as it does today.

**The length bounds are no longer this phase's work.** They were extracted to `const int` by the
`card-bounds` change, which this plan now depends on — see `## References`. What remains here is the
one genuinely configurable number `S-04` introduces.

### Changes Required:

#### 1. The result cap becomes `CardOptions`

**File**: `TenExCards/TenExCards/Cards/CardOptions.cs` (new)

**Intent**: The recency list and search share one cap on how many rows come back. It is a runtime
tuning decision — unlike the length bounds, which are compiled into a migration — so it belongs in
configuration.

**Contract**: `public class CardOptions` with `public const string SectionName = "Cards"` and
`MaxResults` defaulting to `20`. Bound in `Program.cs` next to the existing
`Configure<GenerationOptions>` call, with a matching `Cards` section in `appsettings.json` carrying
`MaxResults` alone.

**It must not carry `MaxPromptCharacters` or `MaxAnswerCharacters`, even as properties defaulting
from the constants.** `CardBounds` compiles into `AddCards`' `HasMaxLength`, so a settable mirror
could only ever widen the gate in front of a column it cannot widen — a card that passed the wider
gate would then throw at `SaveAsync`. `S-05` also needs those numbers as compile-time constants for
its `[MaxLength(...)]` attributes, which an options property cannot supply. See
`context/changes/card-bounds/plan.md`.

**Files**: `TenExCards/TenExCards/Generation/CandidateBounds.cs`,
`TenExCards/TenExCards/Generation/GeminiCardCandidateGenerator.cs`,
`TenExCards/TenExCards/Data/AppDbContext.cs`

**Intent**: None — these three already read `CardBounds` directly.

**Contract**: Untouched by this phase. `card-bounds` re-pointed all three and removed the
`GenerationOptions` parameter from `CandidateBounds.WithinColumnLimits`; there is nothing left here
to re-sign.

#### 2. The edit rule

**File**: `TenExCards/TenExCards/Cards/CardEdit.cs` (new)

**Intent**: What the learner typed into an edit form is either a valid replacement or a named
refusal, decided by a pure function so it is assertable without rendering the page. Mirrors
`PassageBounds` and `CandidateBounds`.

**Contract**: A static `Validate(string prompt, string answer)` — bounds come from `CardBounds`, so
there is no options argument — returning a
result that carries either the **trimmed** prompt and answer or a message naming which field failed
and why. Refusals: either field empty or whitespace after trimming; either field longer than its
limit *after* trimming. Trimming happens before the length check, so trailing whitespace never
costs the learner a character of their limit.

#### 3. The store learns to find, read, update and delete

**File**: `TenExCards/TenExCards/Cards/ICardStore.cs`

**Intent**: Four members, each taking `ownerId` first and filtering on it, each answering a
cross-account request exactly as it answers a request for a card that never existed.

**Contract**:

```csharp
Task<IReadOnlyList<Card>> FindForOwnerAsync(string ownerId, string? term, int limit, CancellationToken ct);
Task<Card?> GetForOwnerAsync(string ownerId, Guid id, CancellationToken ct);
Task<bool> UpdateForOwnerAsync(string ownerId, Guid id, string prompt, string answer, CancellationToken ct);
Task<bool> DeleteForOwnerAsync(string ownerId, Guid id, CancellationToken ct);
```

`bool` is "a row belonging to this owner matched and was changed", so `false` means *either* not
yours *or* already gone — deliberately indistinguishable, because telling them apart leaks the
existence of another account's card.

**File**: `TenExCards/TenExCards/Cards/CardStore.cs`

**Intent**: Implement the four against the existing `IDbContextFactory<AppDbContext>` pattern, with
the same `ArgumentException.ThrowIfNullOrWhiteSpace(ownerId)` guard every current member opens with.

**Contract**: `FindForOwnerAsync` filters on `OwnerId`, then — when the term is non-empty after
trimming — on prompt **or** answer containing it, **with both sides lowered inside the query**
(`c.Prompt.ToLower().Contains(t)` where `t` is the lowered trimmed term). Lowering both sides is
what makes SQL Server and the in-memory provider agree; relying on the database collation would
make the tests assert behaviour the live site does not have.

Ordering is `CreatedAt` descending **then `Id` descending as a tiebreaker**, before `Take(limit)`.
The tiebreaker is not decoration: `CreatedAt` is set from `DateTimeOffset.UtcNow` per save, so two
cards accepted in the same batch can share a value, and an unordered tie at the cutoff makes a card
randomly invisible between one query and the next.

`limit` is clamped to at least 1 so a misconfigured `MaxResults` cannot silently return nothing.

#### 4. Tests

**File**: `TenExCards/TenExCards.Tests/CandidateBoundsTests.cs`

**Intent**: None — `card-bounds` already re-pointed every assertion in this file at `CardBounds`, and
the drift test it left compares those constants against the entity's `HasMaxLength`.

**Contract**: Untouched by this phase.

**File**: `TenExCards/TenExCards.Tests/CardEditTests.cs` (new)

**Intent**: The edit rule, directly.

**Contract**: Accepts at exactly the limits; refuses one over, on each field independently; refuses
empty and whitespace-only on each field; trims before measuring, so a value at the limit with
trailing spaces is accepted rather than refused; returns the trimmed text on success.

**File**: `TenExCards/TenExCards.Tests/CardOwnershipTests.cs`

**Intent**: Extend the existing class to cover each new member across the boundary. This is the
phase's real deliverable — three of the four members can destroy or expose data.

**Contract**: For owners A and B, with cards saved to each:
`FindForOwnerAsync` returns only A's rows, for both an empty term and a term matching B's text;
`GetForOwnerAsync` returns `null` for B's card id; `UpdateForOwnerAsync` returns `false` for B's card
id **and leaves B's row unchanged**; `DeleteForOwnerAsync` returns `false` for B's card id **and
leaves B's row present**. Plus, for the owner's own rows: search matches on answer text as well as
prompt text; search is case-insensitive in both directions; the result respects `limit`; and each of
the four members refuses an empty or whitespace `ownerId` with `ArgumentException`, matching the
existing theories.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build TenExCards/TenExCards.slnx`
- Tests pass: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
- The suite passes with the secret store moved aside, the way CI sees it — per the PowerShell block
  in `TenExCards.Tests/AGENTS.md`
- `grep -rn "MaxPromptCharacters\|MaxAnswerCharacters" TenExCards/` shows the readers pointing at
  `CardBounds` and **no** such property on `CardOptions` — a settable length mirror is the defect
  `card-bounds` removed
- Each cross-account test is observed **failing** with its `OwnerId` filter removed, before the
  filter is restored

#### Manual Verification:

- `dotnet run` against `sqldb-tenexcards-dev` still generates and triages exactly as before — the
  `CardOptions` addition changes nothing on the generation path
- No new migration file exists, and `dotnet ef migrations list` shows nothing pending

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human before proceeding to the next phase.

---

## Phase 2: The `/cards` page

### Overview

The slice's actual outcome: one gated interactive page where a card is found, repaired or removed.
Everything decidable was settled in Phase 1, so this component holds presentation and state only.

### Changes Required:

#### 1. The page

**File**: `TenExCards/TenExCards/Components/Pages/Cards.razor` (new)

**Intent**: Search box, result list, and per-row view / editing / confirming-delete rendering. Gated
by the fallback policy — **no `[AllowAnonymous]`, and this file does not go under
`Components/Account/Pages/`**, whose `_Imports.razor` would make it anonymous silently.

**Contract**: `@page "/cards"` with `@rendermode InteractiveServer`. A search `<input>` bound with
`@bind:event="oninput"`. Each result renders prompt and answer; in view state, **Edit** and
**Delete** buttons; in editing state, a bound prompt input and answer textarea with live character
counters against `CardBounds` and **Save** / **Cancel**; in confirming-delete state, **Delete
permanently** / **Cancel** in place of the row's normal actions.

Three informational states, each distinct because they call for different actions: the learner owns
no cards at all (say so, link to `/generate`); the search matched nothing (say so, and that clearing
the box shows their recent cards); and the default list is showing a capped view (say that these are
the most recent and that search reaches the rest — a learner with sixty cards must not read a
twenty-row list as their whole collection).

**File**: `TenExCards/TenExCards/Components/Pages/Cards.razor.cs` (new)

**Intent**: Component state and the four store calls. Follows `Generate.razor.cs`: `ownerId` from
`AuthenticationState` in `OnInitializedAsync`, injected `ICardStore` and `IOptions<CardOptions>`,
and **every handler catches its own failure rather than letting an exception reach the circuit**.

**Contract**: Fields for the search term, the result list, the `Guid?` of the row being edited, the
`Guid?` of the row confirming deletion, the edit form's two bound strings, a per-row validation
message, and a status line. One `CancellationTokenSource` for the in-flight search, cancelled and
replaced on each new query, with a stale result discarded rather than assigned. Opening either
row state closes the other and closes any other row — at most one row is ever out of view state.

Save calls `CardEdit.Validate` first and shows the refusal in place without touching the store; on
success it calls `UpdateForOwnerAsync` and, on `false`, tells the learner the card no longer exists
and re-runs the search. Delete's second click calls `DeleteForOwnerAsync`; `false` is reported the
same way, since both mean the row is gone. After any successful mutation the open-row state is
cleared **before** the list is re-queried.

`OnInitializedAsync` loads the default list with a null term.

#### 2. The route is gated

**File**: `TenExCards/TenExCards.Tests/AuthBoundaryTests.cs`

**Intent**: Assert `/cards` behaves like `/generate` for an unauthenticated caller.

**Contract**: A case alongside `GenerateRoute_Unauthenticated_RedirectsToLoginNever401` asserting
`302` with a `Location` path starting `/Account/Login` — never `401`, which `verify_deploy.py`
treats as a hard failure.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build TenExCards/TenExCards.slnx`
- Tests pass: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
- `AuthBoundaryTests` shows `/cards` answering `302` to the login path
- `grep -rn "AllowAnonymous" TenExCards/TenExCards/` still returns exactly the four known surfaces —
  `Home`, `Error`, `NotFound`, and `Components/Account/Pages/_Imports.razor`

#### Manual Verification:

- Full local loop: generate a batch, accept several, open `/cards`, find one by a word from its
  **answer**, edit it, and see the change reflected after a refresh
- A card edited to a prompt of exactly 500 characters saves; 501 is refused in place, naming the
  field, without the row leaving edit state
- Delete requires the second click; Cancel restores the row untouched; the deleted card is gone
  after a refresh and absent from `sqldb-tenexcards-dev`
- Opening edit on one row closes an open confirm on another, and vice versa
- Typing quickly in the search box never shows results for an earlier prefix
- All three informational states render: no cards at all, a search matching nothing, and the capped
  default list stating it is capped
- A failure path does not produce the generic Blazor error UI — verified by temporarily forcing
  `UpdateForOwnerAsync` to throw

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human before proceeding to the next phase.

---

## Phase 3: Entry points and the repository record

### Overview

The page exists but nothing points at it. This phase makes it reachable and brings the repository's
own description of what is wired back into line.

### Changes Required:

#### 1. Navigation

**File**: `TenExCards/TenExCards/Components/Layout/NavMenu.razor`

**Intent**: A third nav entry for the new page.

**Contract**: A `NavLink` to `cards` beside the existing Home and Generate cards entries, following
the same `nav-item px-3` markup.

**File**: `TenExCards/TenExCards/Components/Pages/Home.razor`

**Intent**: A signed-in learner should be able to reach their cards from the landing page.

**Contract**: Inside the existing `<Authorized>` block, a secondary link to `cards` beside the
primary generate call to action. The `<NotAuthorized>` block is untouched.

**File**: `TenExCards/TenExCards/Components/Pages/Generate.razor`

**Intent**: The triage summary can now point somewhere. `S-02`'s plan recorded "No link to a
saved-card list; that surface is `S-04`" — this is that link.

**Contract**: In the `Stage.Summary` block, a link to `cards` alongside the existing "Paste another
passage" button. The summary's counts and its "Paste another passage" primary action are unchanged.

#### 2. The repository record

**File**: `TenExCards/AGENTS.md`

**Intent**: `## What is wired, and what is not` must describe the card-management surface, and
`### Authorization defaults to protected` must account for a second gated interactive page.

**Contract**: Record that `/cards` exists, that it is the second `@rendermode InteractiveServer`
page, and that the account boundary on every card query lives in `ICardStore` rather than in any
component. State that no anonymous page uses `InteractiveServer` **still** holds, so the render-mode
bullet's reasoning is unchanged and the count of `.AllowAnonymous()` calls stays at exactly two. Add
the browse prohibition to `## Never do these` under the product rules — the existing "never add
roles, sharing, admin views, decks, tags, or export" bullet is the right home for "and the saved-card
list is a find surface, not a browse surface: no paging, no sort controls, no show-all".

**A fact goes in `AGENTS.md` or in `deploy-plan.md`, never both.** This phase writes nothing to
`context/deployment/deploy-plan.md`; the slice changes no infrastructure and no deploy command.

**Commit by path.** `S-02`'s Phase 5 also edits `AGENTS.md` and the file already carries uncommitted
changes. Per `context/foundation/lessons.md`, verify `git diff --cached -- <path>` before committing
and prefer `git commit --only <paths>`; never `git add -A`. If the staged `AGENTS.md` diff contains
`S-02` Phase 5 content, that content belongs to that change's commit, not this one.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build TenExCards/TenExCards.slnx`
- Tests pass: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
- `git diff --cached` immediately before the commit contains only this change's paths

#### Manual Verification:

- Every entry point reaches `/cards`: the nav link, the Home link, and the link from the triage
  summary
- `AGENTS.md` reads correctly end to end; the `.AllowAnonymous()` count still says two and is still
  true; no fact in it is duplicated in `deploy-plan.md`

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human before proceeding to the next phase.

---

## Phase 4: Deploy and verify on the live instance

### Overview

A push to `main` deploys. The scripted verification asserts status codes only, so the checks that
matter for this slice are the manual ones below — in particular the cross-account probe, which is
the one thing no test in the suite can prove against the real database.

### Changes Required:

No file changes. This phase is the merge, the pipeline run, and the verification.

**Do not hand-build and hand-deploy.** `.github/workflows/deploy.yml` deploys every push to `main`
via `scripts/pack.py` then `scripts/verify_deploy.py`. The manual `az webapp deploy` shape in
`## Never do these` is for out-of-band deploys only.

### Success Criteria:

#### Automated Verification:

- The `main` workflow run is green across its full step list, `Test` included
- `scripts/verify_deploy.py` passes within that run

#### Manual Verification:

- `/cards` on the live site answers `302` to an unauthenticated request, never `401`
- Live assets still answer `text/css` or a JavaScript type rather than `text/html` — the verifier
  follows redirects and cannot see this
- An edit made on the live site survives a refresh, confirmed by querying `sqldb-tenexcards`
- A delete on the live site removes the row, confirmed by the same query
- **Cross-account probe with two live accounts**: account B, signed in, can neither see nor find
  account A's cards; the account-scoped query is confirmed against the live database
- Search latency on the live instance is not visibly worse than local, and rapid typing never
  displays results for an earlier prefix — the stale-query cancellation is the thing being checked,
  and Azure SQL latency is where it would fail

---

## Testing Strategy

### Unit Tests:

- `CardEdit.Validate`: at the limits, one over on each field independently, empty and whitespace-only
  on each field, trimming applied before measurement, trimmed text returned on success.
- `CandidateBoundsTests`: untouched by this slice. `card-bounds` left it asserting the `CardBounds`
  constants against the entity's `HasMaxLength`, which is still the only thing that catches a drift,
  because the in-memory provider ignores `HasMaxLength`.

### Integration Tests:

- `CardOwnershipTests`, extended: each of `FindForOwnerAsync`, `GetForOwnerAsync`,
  `UpdateForOwnerAsync` and `DeleteForOwnerAsync` refuses to cross the account boundary, with update
  and delete additionally asserted to have left the other owner's row untouched. Each is observed
  failing with its filter removed before the filter is restored.
- Search behaviour on the owner's own rows: matching on answer text, case-insensitive in both
  directions, ordering newest-first with the `Id` tiebreaker, and respecting `limit`.
- `AuthBoundaryTests`: `/cards` answers `302` to `/Account/Login` for an unauthenticated caller.

### Manual Testing Steps:

1. Sign in, generate and accept three or four cards, then open `/cards` and confirm they appear
   newest first.
2. Search for a word that appears only in a card's **answer**; confirm it is found.
3. Search using different capitalisation from the stored text; confirm it still matches.
4. Edit a card, save, refresh, confirm the change persisted.
5. Attempt to save an empty prompt and a 501-character prompt; confirm each is refused in place with
   the field named, and the row stays in edit state.
6. Click Delete, then Cancel; confirm nothing changed. Click Delete, then Delete permanently;
   confirm the card is gone after a refresh.
7. Open edit on one row, then click Delete on another; confirm the first row closes.
8. Type a search term quickly; confirm the results never settle on an earlier prefix's matches.
9. With a second account, confirm none of the first account's cards are visible or findable.

## Performance Considerations

The search lowers both sides of the comparison in SQL, so the predicate cannot use an index on
`Prompt` or `Answer`. That is acceptable and deliberate: the `OwnerId` index does the selective work
first, a learner's card count is in the hundreds by the PRD's own `target_scale.data_volume: small`,
and the alternative — relying on the database collation — would make the test suite assert behaviour
the live site does not have. Recorded here so a future scale problem is recognised rather than
rediscovered.

Result sets are capped at `CardOptions.MaxResults`, so neither the query nor the circuit's memory
grows with the learner's collection. Blazor Server memory is per-user on a 1.75 GB B1 tier with no
back-pressure; a capped list of twenty short rows is negligible beside the passage batch
`/generate` already holds.

Per-keystroke queries mean one round trip per character typed. At this scale and request volume
that is acceptable; the cancellation of superseded queries is what keeps it correct rather than what
keeps it cheap.

## Migration Notes

**None.** This slice adds no column, no table and no index, and produces no migration file. That is
a decision rather than an omission: `Program.cs` runs `Database.MigrateAsync()` on the boot path,
forward-only, on a tier with no deployment slots, so a migration is the highest-risk thing a slice
can contain — and nothing named in the PRD is bought by adding one here. Phase 1's automated
criteria include confirming that no migration was generated.

`CardOptions` adds one **configuration key**, not schema. `appsettings.json` is a tracked file
deployed inside the archive, so the new `Cards` section travels with the code and needs no App
Service app-setting change. No Key Vault secret and no app setting is added, changed or read by this slice.

## References

- **Prerequisite change: `context/changes/card-bounds/plan.md`** — extracts the 500/1,000 limits to
  `const int` in `CardBounds`. It owns the work this plan's Phase 1 originally described; Phase 1
  now adds only `CardOptions.MaxResults` and must not reintroduce a settable length.
- Roadmap slice: `context/foundation/roadmap.md` → `### S-04: Learner finds a saved card in order to
  edit or delete it`
- Requirements: `context/foundation/prd.md` → `FR-009`, `FR-010`, `FR-011`, `## Non-Goals`
- Predecessor: `context/changes/passage-to-saved-cards/plan.md` (the `Card` entity, `ICardStore`, and
  the interactive-page pattern)
- Recurring rules: `context/foundation/lessons.md` → "Parallel sessions share one index"
- Testing rules: `TenExCards/TenExCards.Tests/AGENTS.md`
- Store and boundary: `TenExCards/TenExCards/Cards/ICardStore.cs`,
  `TenExCards/TenExCards.Tests/CardOwnershipTests.cs`
- Interactive-page pattern: `TenExCards/TenExCards/Components/Pages/Generate.razor.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not
> rename step titles. See `references/progress-format.md`.

### Phase 1: Bounds become a card concern; the store learns to read back

#### Automated

- [x] 1.1 Solution builds — 24268a4
- [x] 1.2 Tests pass — 24268a4
- [x] 1.3 Suite passes with the secret store moved aside, the way CI sees it — 24268a4
- [x] 1.4 No `GenerationOptions` reference to the moved limits, and no dead `Generation:` key survives — 24268a4
- [x] 1.5 Each cross-account test observed failing with its `OwnerId` filter removed — 24268a4

#### Manual

- [ ] 1.6 Generation and triage still work unchanged against `sqldb-tenexcards-dev`
- [x] 1.7 No migration file exists and nothing is pending — 24268a4

### Phase 2: The `/cards` page

#### Automated

- [ ] 2.1 Solution builds
- [ ] 2.2 Tests pass
- [ ] 2.3 `AuthBoundaryTests` shows `/cards` answering `302` to the login path
- [ ] 2.4 `AllowAnonymous` still appears on exactly the four known surfaces

#### Manual

- [ ] 2.5 Full local loop: find by answer text, edit, and see the change survive a refresh
- [ ] 2.6 500 characters saves, 501 is refused in place without leaving edit state
- [ ] 2.7 Delete needs the second click; Cancel restores the row; the deletion persists
- [ ] 2.8 At most one row is ever out of view state
- [ ] 2.9 Rapid typing never settles on an earlier prefix's results
- [ ] 2.10 All three informational states render correctly
- [ ] 2.11 A forced store failure does not produce the generic Blazor error UI

### Phase 3: Entry points and the repository record

#### Automated

- [ ] 3.1 Solution builds
- [ ] 3.2 Tests pass
- [ ] 3.3 The staged diff immediately before the commit contains only this change's paths

#### Manual

- [ ] 3.4 Nav link, Home link and the triage-summary link all reach `/cards`
- [ ] 3.5 `AGENTS.md` reads correctly; the two-`AllowAnonymous` count is still true; no fact duplicated in `deploy-plan.md`

### Phase 4: Deploy and verify on the live instance

#### Automated

- [ ] 4.1 `main` workflow run green across the full step list
- [ ] 4.2 `scripts/verify_deploy.py` passes in that run

#### Manual

- [ ] 4.3 Anonymous `/cards` on the live site returns `302`, not `401`
- [ ] 4.4 Live assets answer `text/css` / a JavaScript type, not `text/html`
- [ ] 4.5 A live edit survives a refresh, confirmed by query against `sqldb-tenexcards`
- [ ] 4.6 A live delete removes the row, confirmed by the same query
- [ ] 4.7 Two-account cross-boundary probe finds nothing across accounts on the live database
- [ ] 4.8 Search latency acceptable live, and stale-query cancellation holds under real latency
