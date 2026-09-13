# Manual Card Entry Implementation Plan

## Overview

`S-05` / FR-012: a signed-in learner writes a flashcard by hand — a prompt and an answer — and saves
it to their own space without generating one first. It is the last of the three slices that hang off
the north star, and the smallest: `ICardStore` was built in `S-02` to accept an origin, `CardOrigin`
already defines `Manual`, and the `Origin` column already exists. This slice is a surface over an
interface that was designed to receive it.

The change was first scaffolded under the change-id `manual-card-retry`, which matched nothing in
the roadmap, the PRD or the codebase. It was a mis-typed `manual-card-entry`; the folder was removed
and re-created under the roadmap's own id so the slice traces back to `S-05`.

## Current State Analysis

The application is a deployed Blazor Web App on `net10.0` with per-page interactivity, EF Core
against Azure SQL, Identity on a role-free `IdentityUserContext`, an encrypted Data Protection key
ring, a test project that gates the deploy, and a pipeline that deploys every push to `main`.
`S-02` landed the generation and triage loop on 2026-09-13.

What exists that this slice builds on:

- **A store that already takes the origin.** `Cards/ICardStore.cs:11-16` declares
  `SaveAsync(ownerId, prompt, answer, origin, ct)`, and `Cards/CardStore.cs:29-32` validates it with
  `Enum.IsDefined(origin)`. Nothing needs to change in either file.
- **The enum value, already written and already documented as this slice's.**
  `Data/Card.cs:23` — `/// <summary>Written as Generated here; Manual is what S-05 adds.</summary>`,
  with `Manual = 2`.
- **The column, already migrated.** `Migrations/20260912214732_AddCards.cs:22` created
  `Origin = table.Column<int>(type: "int", nullable: false)`. An `int` column already holding `1`
  accepts `2` with no schema change. **There is no migration in this plan**, so the forward-only
  migration hazard that shapes every other persisting slice is absent from this one.
- **A statically rendered form pattern, twice.** `Components/Account/Pages/Login.razor` and
  `Register.razor` are `EditForm method="post"` with `[SupplyParameterFromForm]`, a
  `DataAnnotationsValidator`, and a redirect on success through `IdentityRedirectManager`.
- **`IdentityRedirectManager`, registered and reusable.** `Program.cs:20` registers it scoped. It is
  `internal sealed` in the same assembly, so `Components/Pages/` can inject it, and its
  `RedirectTo(uri, queryParameters)` overload is exactly the post-redirect-get this plan needs.
- **Authorization that defaults to protected.** `Program.cs:121` sets a fallback policy, so a page
  under `Components/Pages/` is gated with no attribute at all.
- **An account-boundary test pattern.** `TenExCards.Tests/CardOwnershipTests.cs` and
  `AuthBoundaryTests.cs`, with `HtmlFormHelpers.ExtractHiddenFields` / `PostFormAsync` for driving a
  static form from a test.

What is missing:

- **Any way to create a card that did not come from a passage.** `/generate` is the only writer.
- ~~**A single source for the 500 / 1000 character bounds.**~~ **Resolved ahead of this slice by
  the `card-bounds` change**, which made them `const int` on `Data/CardBounds.cs` and deleted the
  `GenerationOptions` properties and configuration keys outright. The manual form reads the
  constants directly. This was originally Phase 1 of this plan; Phase 1 is now a check that the
  prerequisite landed, kept as a phase so no later numbering moves.
- **Any mention of `S-05` in `TenExCards/AGENTS.md`.** Its "Still missing" line names `S-03` and
  `S-04` and stops; manual entry is absent from the document entirely.

## Desired End State

A signed-in learner on `/generate`, before pasting anything, sees a secondary line offering to write
a card by hand. It takes them to `/cards/new`: a statically rendered page with a prompt field, an
answer field, and a two-line reminder that a card should test one load-bearing claim and admit one
defensible answer. They fill it in and save. The page comes back empty, ready for the next card,
telling them how many they have written this visit. Refreshing that page writes nothing. An
over-length field is refused with a message naming the limit, and no row is written. An anonymous
request for `/cards/new` is redirected to the login page, never answered `401`.

Verified by: the deployed instance saving a hand-written card that survives a refresh, with
`Origin = 2` in `sqldb-tenexcards` scoped to the writing account; a refresh after a save leaving the
card count unchanged; and the suite passing with the secret store moved aside, the way CI sees it.

### Key Discoveries:

- **`Components/Account/Pages/_Imports.razor:2` carries `@attribute [AllowAnonymous]`.** Anything
  dropped in that folder is anonymous the moment it exists, with no attribute anyone would review.
  This page goes in `Components/Pages/`. `TenExCards/AGENTS.md` names this trap explicitly.
- **The unload warning is a `beforeunload` handler, and Blazor's enhanced navigation is a fetch.**
  `wwwroot/Components/Pages/Generate.razor.js` registers `beforeunload`; an `<a href>` clicked
  inside the interactive `Generate` component is intercepted by enhanced navigation, which never
  raises that event. A link to `/cards/new` rendered during `Stage.Triaging` would therefore discard
  the untriaged batch with no warning whatsoever. The link is rendered only in the
  `Composing`/`Failed` branch, where there is no batch to lose.
- **A static `EditForm` POST re-renders the same URL, so a refresh re-posts it.** `Login` and
  `Register` dodge this by redirecting on success. A page that stays put would write a duplicate
  card on `F5` — silent duplication, invisible until `S-04` ships a card list. Hence
  post-redirect-get.
- **Only two of the PRD's four card properties survive without a source passage.** `## Business
  Logic` requires a candidate to test one load-bearing claim, be reformulated rather than copied,
  admit one defensible answer, and not repeat another card *in the same set*. A hand-written card
  has no source to be copied from and no set to duplicate within. The guidance on this page states
  the two that transfer and nothing else.
- **`DataAnnotations` length attributes take a compile-time constant**, so the extracted bounds must
  be `const int`, not `static readonly int`. This is the argument that settled the design across
  three slices: an `IOptions<>` property cannot be an attribute argument, so a configurable bound
  would have blocked Phase 2's form validation outright. See `context/changes/card-bounds/plan.md`.
- **A test that constructs its own `WebApplicationFactory<Program>` must set every guarded
  setting.** `TenExCards.Tests/AGENTS.md` records the 2026-09-13 incident where a missed one passed
  locally on user-secrets and failed only in CI, stopping a deploy at `Test`.

## What We're NOT Doing

- **No duplicate detection against saved cards.** The PRD scopes deduplication to "another card in
  the same set"; a hand-written card is not part of a set. Checking against stored cards needs a
  query `ICardStore` does not have and a surface for showing the collision — both `S-04`.
- **No nav-menu entry, and no link from `Home.razor`.** `S-05`'s roadmap risk is that manual entry
  becomes a primary path, which would make the PRD's 75%-generated target meaningless. Generation
  stays the default; this is one click off it.
- **No edit or delete of the saved card.** `S-04`.
- **No migration, no `ICardStore` change, no new DI registration, no new package.**
- **No recording of manual entry as a measured outcome.** `Origin = Manual` is written because the
  column exists; deriving the AI-origin share from it is `S-06`.
- **No interactivity.** No `@rendermode`, no circuit, no JS, no live character counter.

## Implementation Approach

Three phases. Phase 1 collapses the character bounds to one constant before a third reader exists,
so the new page is written against a single source rather than adding to a drift problem; it changes
nothing a learner can see and ships green on its own. Phase 2 is the page and its link. Phase 3
deploys, verifies on the live instance, and closes the repository record.

The page follows `Login.razor` rather than `Generate.razor`: statically rendered, `EditForm`
`method="post"` with `[SupplyParameterFromForm]`, `DataAnnotationsValidator`, and a redirect on
success. That keeps a two-field form off the circuit budget — B1 is 1.75 GB with no back-pressure,
and `AGENTS.md` requires holding nothing in a circuit that need not be there.

## Critical Implementation Details

**State sequencing on the success path.** The save must complete before the redirect is issued, and
the redirect is what clears the form — there is no "clear the fields" step. `NavigationManager`'s
redirect during a static POST handler unwinds by throwing, so any code after the `RedirectTo` call
does not run; the save, and any counter arithmetic feeding the query string, happen before it.

**The this-visit counter is cosmetic and must never be read as a fact.** It arrives in the query
string, where the learner can type anything. Clamp it to a sane non-negative range on read, use it
for display only, and let no branch depend on it. A comment saying so belongs next to it, because
the next reader's instinct will be to trust it.

## Phase 1: Confirm the shared bounds are in place

### Overview

The extraction this phase originally performed is now the `card-bounds` change, which landed
`Data/CardBounds.cs` with both values as `const int` and re-pointed every reader. What remains is a
gate: confirm that prerequisite is actually in the tree before Phase 2 writes
`[MaxLength(CardBounds.MaxPromptCharacters)]` against it. No code change, no user-visible change.

Kept as a numbered phase rather than deleted so that Phases 2 onward, the `## Progress` section and
every cross-reference keep their numbers.

### Changes Required:

None. `card-bounds` owns every file this phase used to name — `Data/CardBounds.cs`,
`Data/AppDbContext.cs`, `Generation/GenerationOptions.cs`, `appsettings.json` and
`TenExCards.Tests/CandidateBoundsTests.cs`.

Two differences from what this plan originally specified are deliberate and must not be "restored"
in Phase 2:

- **`GenerationOptions.MaxPromptCharacters` / `MaxAnswerCharacters` no longer exist**, rather than
  being kept and defaulted from the constants. The value is compiled into the `AddCards` migration,
  so a settable property could only ever widen the gate in front of a column it cannot widen.
- **The drift assertion is therefore two-way, not three-way** — `CardBounds` against the EF model's
  `GetMaxLength()`. With no configuration key and no property, there is no third term left to check.

### Success Criteria:

#### Automated Verification:

- `TenExCards/TenExCards/Data/CardBounds.cs` exists and declares both values as `const int`
- Solution builds: `dotnet build TenExCards/TenExCards.slnx`
- Tests pass: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
- No pending model change: `dotnet ef migrations has-pending-model-changes --project TenExCards/TenExCards/TenExCards.csproj` reports none
- `grep -rn "MaxPromptCharacters\|MaxAnswerCharacters" TenExCards --include=*.cs --include=*.json`
  shows no `GenerationOptions` property and no `appsettings.json` key for either bound

#### Manual Verification:

- `CardBounds`' XML doc names its readers, so Phase 2 adding the form as another finds the
  instruction

**Implementation Note**: This phase writes no code. If any criterion above fails, `card-bounds` has
not landed or did not land as planned — stop and resolve that rather than re-doing the extraction
here, which is what created the conflict this sequencing exists to remove.

---

## Phase 2: The manual-entry page

### Overview

A statically rendered page at `/cards/new` that saves a hand-written card with `CardOrigin.Manual`,
plus the stage-scoped link that leads to it and the tests that pin the boundary.

### Changes Required:

#### 1. The page

**File**: `TenExCards/TenExCards/Components/Pages/CardEntry.razor` (new)

**Intent**: The form itself — prompt, answer, save — plus the two-line quality reminder and the
this-visit confirmation. Modelled on `Login.razor`'s markup shape.

**Contract**: `@page "/cards/new"`. **No `@rendermode`** and **no `[AllowAnonymous]`** — the fallback
policy gates it. It must live in `Components/Pages/`, never `Components/Account/Pages/`, whose
`_Imports.razor` would make it anonymous. `EditForm Model="Input" method="post" OnValidSubmit="SaveAsync" FormName="card-entry"`
with a `DataAnnotationsValidator`, a `ValidationSummary`, and a `ValidationMessage` per field. The
answer field is a multi-line `InputTextArea`. The guidance text states exactly two rules — the card
should test one load-bearing claim, and its prompt should admit one defensible answer — and must not
restate the other two PRD properties, which are false here. When the this-visit count is above zero
the page shows a confirmation naming it.

#### 2. The page's code-behind

**File**: `TenExCards/TenExCards/Components/Pages/CardEntry.razor.cs` (new)

**Intent**: Read the owner from the authentication state, save through `ICardStore` with
`CardOrigin.Manual`, and redirect back to the same route with the incremented count.

**Contract**: A `partial class CardEntry` injecting `ICardStore` and `IdentityRedirectManager`, with
`[CascadingParameter] private HttpContext HttpContext` for the user (the `Login.razor.cs` pattern)
and `[SupplyParameterFromForm] private InputModel Input`. A private `sealed class InputModel` with
`Prompt` and `Answer`, each `[Required]` and each carrying
`[MaxLength(CardBounds.MaxPromptCharacters)]` / `[MaxLength(CardBounds.MaxAnswerCharacters)]` —
the constants are why Phase 1 made them `const`. A `[SupplyParameterFromQuery] private int? Written`
carries the count.

The owner id is `HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)`; the fallback policy
guarantees it is present, but the page must not pass a null through to `SaveAsync` — `CardStore`
throws `ArgumentException` on a blank owner and that would surface as an unhandled error page.

On valid submit: save, then `RedirectTo("cards/new", new() { ["written"] = <clamped count> + 1 })`.
The save precedes the redirect because the redirect unwinds by throwing. Clamp `Written` on read to
a non-negative sane maximum and treat it as display-only — see `## Critical Implementation Details`.

A failed save is reported the way `Generate.razor.cs` reports one: a message saying nothing was lost
and to try again, with the typed values still in the fields, and **no** redirect — a redirect would
clear the form and lose what the learner wrote.

#### 3. The link from the generation page

**File**: `TenExCards/TenExCards/Components/Pages/Generate.razor`

**Intent**: A secondary line offering the manual path, worded so generation stays the obvious
default.

**Contract**: A plain `<a href="cards/new">` inside the `@if (_stage is Stage.Composing or Stage.Failed)`
branch only. **It must not be rendered in the `Triaging` branch**: enhanced navigation does not fire
`beforeunload`, so the unload warning would not run and the untriaged batch would go silently. Text
and styling must read as secondary to the `btn-primary` submit — a link, not a second button.

#### 4. The tests

**File**: `TenExCards/TenExCards.Tests/ManualCardEntryTests.cs` (new)

**Intent**: Pin the account boundary and the deterministic rules on this new write path, per
`TenExCards.Tests/AGENTS.md` — the origin, the ownership scoping, and the refusal of an over-length
field.

**Contract**: An `IClassFixture<TenExCardsWebApplicationFactory>` class using
`HtmlFormHelpers.ExtractHiddenFields` / `PostFormAsync` to drive the static form, mirroring
`AuthBoundaryTests`' register helper. Cases:

- an authenticated POST writes exactly one row, `Origin = CardOrigin.Manual`, `OwnerId` the poster's
- that row is invisible to a second account's `CountForOwnerAsync`
- a prompt one character over `CardBounds.MaxPromptCharacters` is refused and **no row is written** —
  the count before and after must be equal, not merely the response non-`302`
- the same for the answer
- a successful POST answers `302` to `/cards/new` carrying a `written` query parameter, and a
  following `GET` of that location writes nothing further
- an absurd or negative `written` value in the query string changes no behaviour and writes nothing

**File**: `TenExCards/TenExCards.Tests/AuthBoundaryTests.cs`

**Intent**: Add `/cards/new` to the existing gated-route coverage, so the route is asserted to
answer `302` to the login path and never `401` — `verify_deploy.py` treats `401` as a hard failure.

**Contract**: One new `[InlineData("/cards/new")]` on the gated-route theory. Do not add it to the
anonymous-route theory.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build TenExCards/TenExCards.slnx`
- Tests pass: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
- The suite passes the way CI sees it — with `secrets.json` moved aside, per the runbook in `TenExCards.Tests/AGENTS.md`
- `grep -rn "rendermode\|AllowAnonymous" TenExCards/TenExCards/Components/Pages/CardEntry.razor` finds neither
- The new page is under `Components/Pages/`, not `Components/Account/Pages/`
- No new migration: `dotnet ef migrations has-pending-model-changes` reports none

#### Manual Verification:

- Against `sqldb-tenexcards-dev`: a card written by hand appears with `Origin = 2` and the signed-in account's `OwnerId`
- The form comes back empty after a save and names the this-visit count
- Refreshing after a save writes no second card — confirmed by a row count, not by the absence of a browser prompt
- An over-length answer is refused with a message naming the limit, and the typed text is still in the field
- The manual link is present while composing and **absent** during triage — check the rendered triage markup, not just the page
- Leaving `/generate` mid-triage still warns; the link's introduction has not disturbed the unload handler
- The guidance names two rules, and neither is one that requires a source passage

**Implementation Note**: After the automated criteria pass, pause for manual confirmation before
Phase 3. The refresh check and the triage-branch check are the two that a green suite does not
cover, and both are silent when wrong.

---

## Phase 3: Deploy, verify, and close the record

### Overview

Push to `main`, verify on the live instance, and bring the repository's own record into line —
including the two places that currently describe `S-05` as future work.

### Changes Required:

#### 1. Deploy

**File**: — (no file; a push to `main`)

**Intent**: `.github/workflows/deploy.yml` deploys every push to `main`. Do not hand-build or
hand-deploy this; `TenExCards/AGENTS.md` is explicit that a commit going to `main` goes through the
pipeline.

**Contract**: A green run across the full step list — `Test`, `Publish`, `Pack`, `Retain`,
`Azure login`, `Deploy`, `Verify`. Read the step list, not the colour. No app setting and no
infrastructure change is required by this slice, so nothing must be set before the merge.

#### 2. The stale enum comment

**File**: `TenExCards/TenExCards/Data/Card.cs`

**Intent**: `CardOrigin`'s XML doc says "`Manual` is what `S-05` adds". Once this ships that is no
longer a forward reference.

**Contract**: Rewrite the summary to state what each value means now. Do not delete the mention of
where manual cards come from — a reader needs to know `Manual` is written by `/cards/new` and
nowhere else.

#### 3. The repository guide

**File**: `TenExCards/AGENTS.md`

**Intent**: The document does not mention `S-05` at all — its "Still missing" line names `S-03` and
`S-04` and stops. Record that manual entry is live, and record the two mechanisms this slice
discovered, both of which are traps for the next agent.

**Contract**: A short paragraph in the wired/not-wired section saying manual entry is live as of
this date, on the same statically rendered form pattern as the Identity pages and deliberately not
in the nav. Two facts belong there and nowhere else: that **enhanced navigation does not fire
`beforeunload`**, so a link rendered during triage would discard the batch silently; and that a
static `EditForm` POST that re-renders duplicates on refresh, which is why this page redirects.
Update the "Still missing" line so it no longer implies `S-05` is outstanding. Every fact goes in
exactly one place — do not restate anything `deploy-plan.md` owns.

#### 4. The roadmap

**File**: `context/foundation/roadmap.md`

**Intent**: Close `S-05`.

**Contract**: The `## At a glance` row for `S-05` and the `### S-05:` body both move to `done`, with
a `**Landed <date>:**` note in the body recording what shipped and the two traps above. Bump the
frontmatter `updated`. `S-06`'s prerequisites list `S-03`, `S-04` and `S-05`; do not change it.

#### 5. The change record

**File**: `context/changes/manual-card-entry/change.md`

**Contract**: `status: done`, `updated` bumped.

### Success Criteria:

#### Automated Verification:

- The `main` workflow run is green across the full step list
- `scripts/verify_deploy.py` passes in that run
- `grep -rn "S-05" TenExCards/AGENTS.md TenExCards/TenExCards/Data/Card.cs` finds no remaining forward reference

#### Manual Verification:

- Anonymous `GET /cards/new` on the live site returns `302`, not `401`
- A card written by hand on the live site survives a refresh, confirmed by query against `sqldb-tenexcards` with `Origin = 2` and the right `OwnerId`
- A refresh immediately after a live save leaves the row count unchanged
- Live assets answer `text/css` or a JavaScript type, not `text/html` — the gated-asset false pass `verify_deploy.py` cannot catch
- `AGENTS.md` reads correctly end to end and the two new mechanisms appear exactly once
- Roadmap `S-05` and `AGENTS.md` tell the same story

**Implementation Note**: The live refresh check needs the row count from before the refresh. Take it
first; a duplicate is indistinguishable from a correctly saved second card after the fact.

---

## Testing Strategy

### Unit Tests:

- `CardBounds` equals the EF model's configured max lengths — the two-way assertion `card-bounds`
  left behind, which this slice inherits rather than extends
- An over-length prompt and an over-length answer are each refused with no row written

### Integration Tests:

- Register, sign in, POST the form, assert one row with `Origin = Manual` and the poster's `OwnerId`
- The same row is invisible to a second account
- `/cards/new` answers `302` to the login path when anonymous, never `401`
- A successful POST redirects, and following the redirect writes nothing further

### Manual Testing Steps:

1. Sign in, open `/generate`, confirm the manual link is visible before pasting.
2. Generate a batch and confirm the link is **gone** during triage; leave the page and confirm the unload warning still fires.
3. Open `/cards/new`, write a card, save. Confirm the form is empty and the count reads one.
4. Press `F5`. Confirm no browser re-post prompt and no second row.
5. Write a second card; confirm the count reads two.
6. Paste 501 characters into the prompt; confirm refusal, the limit named, and the text still there.
7. Edit `?written=` in the URL to a negative number and to something absurd; confirm nothing breaks and nothing is written.
8. Sign in as a second account and confirm the first account's hand-written cards are not counted.

## Performance Considerations

The page holds no circuit, so it adds nothing to the per-user memory ceiling that `AGENTS.md`
identifies as arriving without back-pressure. Each POST opens one short-lived `DbContext` through
the existing factory, the same as an accepted candidate does today.

## Migration Notes

None. The `Origin` column exists and already stores an `int`; `CardOrigin.Manual = 2` needs no schema
change. Phase 1's model edit must be verified to produce no pending model change — that check is a
success criterion, not an assumption.

## References

- **Prerequisite change: `context/changes/card-bounds/plan.md`** — landed `CardBounds` as `const
  int`. It absorbed this plan's original Phase 1; Phase 2's `[MaxLength(...)]` attributes are
  signed against it.
- Roadmap slice: `context/foundation/roadmap.md` → `### S-05: Learner creates a card manually`
- Requirement: `context/foundation/prd.md` → FR-012, and `## Business Logic` for the two rules that transfer
- Prior implementation: `context/changes/passage-to-saved-cards/plan.md` (`S-02`)
- Form pattern: `TenExCards/TenExCards/Components/Account/Pages/Login.razor`
- Testing rules: `TenExCards/TenExCards.Tests/AGENTS.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Confirm the shared bounds are in place

#### Automated

- [x] 1.1 `CardBounds.cs` exists and declares both values as `const int` — abdd5b4
- [x] 1.2 Solution builds — abdd5b4
- [x] 1.3 Tests pass — abdd5b4
- [x] 1.4 `has-pending-model-changes` reports none — abdd5b4
- [x] 1.5 No `GenerationOptions` property and no `appsettings.json` key for either bound — abdd5b4

#### Manual

- [x] 1.6 `CardBounds`' XML doc names its readers — abdd5b4

### Phase 2: The manual-entry page

#### Automated

- [x] 2.1 Solution builds
- [x] 2.2 Tests pass
- [ ] 2.3 Suite passes with `secrets.json` moved aside, the way CI sees it
- [x] 2.4 `CardEntry.razor` carries neither `@rendermode` nor `[AllowAnonymous]`
- [x] 2.5 The page is under `Components/Pages/`, not `Components/Account/Pages/`
- [ ] 2.6 No new migration implied

#### Manual

- [ ] 2.7 A hand-written card appears in `sqldb-tenexcards-dev` with `Origin = 2` and the right `OwnerId`
- [ ] 2.8 The form returns empty and names the this-visit count
- [ ] 2.9 A refresh after a save writes no second card, confirmed by row count
- [ ] 2.10 An over-length answer is refused, the limit named, the text retained
- [ ] 2.11 The manual link is present while composing and absent during triage
- [ ] 2.12 Leaving `/generate` mid-triage still warns
- [ ] 2.13 The guidance names two rules, neither requiring a source passage

### Phase 3: Deploy, verify, and close the record

#### Automated

- [ ] 3.1 `main` workflow run green across the full step list
- [ ] 3.2 `scripts/verify_deploy.py` passes in that run
- [ ] 3.3 No `S-05` forward reference left in `AGENTS.md` or `Card.cs`

#### Manual

- [ ] 3.4 Anonymous `/cards/new` on the live site returns `302`, not `401`
- [ ] 3.5 A live hand-written card survives a refresh, confirmed by query against `sqldb-tenexcards`
- [ ] 3.6 A refresh after a live save leaves the row count unchanged
- [ ] 3.7 Live assets answer `text/css` / a JavaScript type, not `text/html`
- [ ] 3.8 `AGENTS.md` reads correctly and the two new mechanisms appear exactly once
- [ ] 3.9 Roadmap `S-05` and `AGENTS.md` tell the same story
