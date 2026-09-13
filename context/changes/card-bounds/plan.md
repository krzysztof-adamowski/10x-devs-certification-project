# Card Length Bounds Become Compile-Time Constants Implementation Plan

## Overview

The 500 and 1,000 character limits on a card's prompt and answer are currently properties of
`GenerationOptions`, which is wrong on its face — they are properties of a `Card`, and generation is
merely one of three things that reads them. Three planned slices each noticed this independently and
each opened with a Phase 1 that fixes it, in two mutually exclusive ways. This change fixes it once,
before any of them starts.

It is a pure refactor: no schema change, no behaviour change, no new capability. Its entire value is
that `S-03`, `S-04` and `S-05` can then be implemented in any order without one invalidating
another's opening phase.

## Current State Analysis

Two numbers, five places, and a test that exists only because nothing else would notice them
drifting apart.

- `TenExCards/TenExCards/Generation/GenerationOptions.cs:22,24` declares
  `MaxPromptCharacters = 500` and `MaxAnswerCharacters = 1_000` as settable properties, with a
  comment saying they mirror `Card`'s `HasMaxLength` and that `CandidateBoundsTests` asserts the
  equality.
- `TenExCards/TenExCards/appsettings.json:16-17` sets both inside the `Generation` section.
- `TenExCards/TenExCards/Data/AppDbContext.cs:47-49` sets `HasMaxLength(500)` and
  `HasMaxLength(1000)` as literals, under a comment naming
  `Generation:MaxPromptCharacters`/`MaxAnswerCharacters` as the mirror.
- `TenExCards/TenExCards/Generation/CandidateBounds.cs:11-20` takes `GenerationOptions` and drops
  over-long candidates, called once from
  `TenExCards/TenExCards/Generation/GeminiCardCandidateGenerator.cs:195`.
- `TenExCards/TenExCards/Generation/GeminiCardCandidateGenerator.cs:152-153` interpolates both into
  the response schema's `maxLength`.
- `TenExCards/TenExCards.Tests/CandidateBoundsTests.cs:75-88` asserts the EF model's
  `GetMaxLength()` equals the bound option value, and that the bound value equals the configured
  one — because the EF in-memory provider ignores `HasMaxLength`, so a drift is otherwise invisible.

**The configurability is not real.** `HasMaxLength(500)` is compiled into the `AddCards` migration
and applied as `nvarchar(500)` in `sqldb-tenexcards`. Raising `Generation:MaxPromptCharacters` to
600 does not widen the column — it widens the *gate in front of* the column, so a 601-character
candidate passes `CandidateBounds` and then throws at `SaveAsync`. The key reads as a tuning knob
and is in fact a way to break the application at the last possible moment.

Three planned changes already depend on the shape chosen here:

| | `manage-saved-cards` P1 | `manual-card-entry` P1 |
| --- | --- | --- |
| New type | `Cards/CardOptions.cs` | `Data/CardBounds.cs` |
| Mechanism | config-bound settable properties | `const int` |
| `GenerationOptions` | properties deleted | properties kept, defaulted from the consts |
| `appsettings.json` | keys moved to a `Cards` section | untouched |
| `CandidateBounds` | re-signatured to take `CardOptions` | untouched |

And `edit-before-accepting` Phase 2 adds
`CandidateBounds.IsWithinColumnLimits(string, string, GenerationOptions)` — signed against the
properties the first column deletes.

## Desired End State

One static class, `CardBounds`, holds the two numbers as `const int`. `AppDbContext`,
`CandidateBounds` and the Gemini schema builder all read it. `GenerationOptions` no longer declares
them and `appsettings.json` no longer sets them. Behaviour at runtime is identical in every respect
— the same candidates are dropped, the same schema is sent, the same columns are created.

`CandidateBoundsTests`' drift assertion collapses from a three-way check (config → option → EF
model) to a two-way one (const → EF model), because the middle term no longer exists.

Verification: the suite passes unchanged in intent, a live generation still produces and saves
cards, and `grep` finds no remaining reader of the old properties.

### Key Discoveries:

- **`[MaxLength(...)]` requires a compile-time constant.** This is what makes the two designs
  genuinely exclusive rather than merely different: an `IOptions<>` property cannot be an attribute
  argument, so `manual-card-entry`'s form-validation approach is blocked outright by `CardOptions`
  as `manage-saved-cards` specifies it. The constant is the only mechanism that serves all three
  slices.
- **The reader set is exactly three, and `grep` proves it.** `CandidateBounds`, `AppDbContext` and
  the Gemini schema builder — confirmed by
  `grep -rn "MaxPromptCharacters\|MaxAnswerCharacters" TenExCards --include=*.cs`. There is no
  fourth, so this refactor has a closed edge.
- **Deleting the keys from `appsettings.json` is required, not tidy-up.** The binder ignores a key
  with no matching property — no error, no log. Leaving them produces a file in which two numbers
  appear configured while nothing reads them, which is the trap `manage-saved-cards` already
  identified for its own move.
- **Nothing in the deployed configuration sets these.** They exist only in `appsettings.json`; there
  is no `Generation__MaxPromptCharacters` app setting on the App Service, so removing them cannot
  change deployed behaviour.
- **`CandidateBounds` keeps its name and its folder.** It is now card-shaped rather than
  generation-shaped and could arguably move to `Cards/`, but `S-04` re-signatures it and `S-05`
  leaves it alone; moving the file would create exactly the churn this change exists to prevent.

## What We're NOT Doing

- **Creating `CardOptions`.** With the lengths const, it would carry only `MaxResults` — used by
  `S-04` alone. `S-04` creates it, and it no longer conflicts with anything.
- **Keeping a settable mirror of the lengths anywhere.** No `CardOptions.MaxPromptCharacters`
  defaulting from the const, no `GenerationOptions` property left behind. A property that can be set
  to a value the column cannot hold is the defect being removed, not a compatibility shim.
- **Changing either number.** 500 and 1,000 stay exactly as they are; changing a bound means a
  migration, which is a separate decision.
- **Any schema change or migration.** `HasMaxLength(CardBounds.MaxPromptCharacters)` evaluates to
  the same 500, so the model snapshot is unchanged and `has-pending-model-changes` must report
  none.
- **Moving `CandidateBounds`, renaming it, or changing what it does.** Only its signature loses a
  parameter.
- **Adding the `IsWithinColumnLimits` overload `S-03` needs.** That belongs to `S-03`; this change
  only makes it possible to add one with a sane signature.
- **Touching `GenerationOptions`' other six properties**, the `Gemini` section, or any other
  configuration.
- **Adding a roadmap slice.** This is a refactor with no learner-visible outcome, so it has no
  `S-nn`. `## References` records where it sits relative to the three slices it unblocks.

## Implementation Approach

One phase of code, because the change has a closed edge — three readers, one test file, one config
section — and splitting it would leave the repository in a state where the constant and the
properties both exist, which is the ambiguity being removed.

A second phase deploys and verifies, because a push to `main` is a production deploy and the one
runtime path this touches, the Gemini response schema, cannot be verified from a unit test.

## Critical Implementation Details

**The model check is the real assertion, and it must be run.** `HasMaxLength(500)` and
`HasMaxLength(CardBounds.MaxPromptCharacters)` produce an identical model snapshot, so
`dotnet ef migrations has-pending-model-changes` must report **none**. Prefer it to generating a
migration and inspecting it: it answers the question directly and leaves no file to remember to
delete. A pending change means the constant does not equal the literal it replaced, and that is a
defect to fix rather than a migration to keep.

**`CandidateBoundsTests` must stay meaningful after the middle term is removed.** Its current
three-way chain includes an assertion that the bound option equals the configured one; with no
configuration left, that assertion has nothing to say and is deleted. The one that matters — EF
model versus constant — is kept and must be observed failing when one side is changed, or the
refactor has quietly removed the only guard on the pair.

---

## Phase 1: One constant, three readers

### Overview

Introduce `CardBounds`, re-point every reader at it, and remove the properties and configuration
keys that no longer have a consumer.

### Changes Required:

#### 1. The constants

**File**: `TenExCards/TenExCards/Data/CardBounds.cs` (new)

**Intent**: Hold the two card length limits as the single source they have never had. `Data/` rather
than `Cards/` or `Generation/` because the number is a schema fact — it is what `HasMaxLength`
compiles into the migration, and every other reader is downstream of that.

**Contract**: A static class exposing `public const int MaxPromptCharacters = 500` and
`public const int MaxAnswerCharacters = 1_000`. `const`, never `static readonly`: `S-05`'s
`[MaxLength(...)]` attributes take a compile-time constant and `static readonly` will not compile
there.

Its XML doc carries what the deleted comments said, in one place: that the value is baked into the
`AddCards` migration so changing it needs a migration, that `CandidateBoundsTests` asserts it against
the EF model because the in-memory provider ignores `HasMaxLength`, and that the three readers are
`AppDbContext`, `CandidateBounds` and `GeminiCardCandidateGenerator` — so the next agent adding a
fourth finds the list.

#### 2. The schema reader

**File**: `TenExCards/TenExCards/Data/AppDbContext.cs`

**Intent**: Stop the entity configuration from restating the number as a literal.

**Contract**: `HasMaxLength(CardBounds.MaxPromptCharacters)` and
`HasMaxLength(CardBounds.MaxAnswerCharacters)`. The existing comment above them is replaced by a
one-line pointer to `CardBounds`, since its content now lives there.

#### 3. The candidate gate

**File**: `TenExCards/TenExCards/Generation/CandidateBounds.cs`

**Intent**: Read the constants directly, losing a parameter that no longer carries information.

**Contract**: `WithinColumnLimits(IReadOnlyList<CandidateCard> candidates)` — the `GenerationOptions`
parameter is removed and the two comparisons read `CardBounds`. Behaviour is unchanged: over-long
candidates are dropped rather than truncated, order preserved. The class keeps its name and stays in
`Generation/`.

#### 4. The generator

**File**: `TenExCards/TenExCards/Generation/GeminiCardCandidateGenerator.cs`

**Intent**: Interpolate the constants into the response schema and drop the argument at the call
site.

**Contract**: `BuildOptions`' schema string reads `CardBounds.MaxPromptCharacters` /
`CardBounds.MaxAnswerCharacters` in place of `_options.*` at lines 152-153, and line 195 becomes
`CandidateBounds.WithinColumnLimits(kept)`. The generator keeps its `IOptions<GenerationOptions>`
injection — it still reads six other properties.

#### 5. The options class

**File**: `TenExCards/TenExCards/Generation/GenerationOptions.cs`

**Intent**: Remove two properties that describe a card rather than a generation.

**Contract**: `MaxPromptCharacters` and `MaxAnswerCharacters` are deleted along with the comment
above them. The remaining six properties are untouched.

#### 6. The configuration

**File**: `TenExCards/TenExCards/appsettings.json`

**Intent**: Remove keys that now bind to nothing.

**Contract**: `MaxPromptCharacters` and `MaxAnswerCharacters` are removed from the `Generation`
section, which keeps `MaxPassageCharacters`, `MaxFocusHintCharacters`, `MinCandidates`,
`MaxCandidates`, `WordsPerCandidate` and `TimeoutSeconds`. **They must be removed rather than left
behind** — the binder ignores an unmatched key silently, so leaving them shows two numbers that
appear configured and are not.

#### 7. The tests

**File**: `TenExCards/TenExCards.Tests/CandidateBoundsTests.cs`

**Intent**: Keep the guard that matters, delete the one that no longer has two sides.

**Contract**: Every `WithinColumnLimits(candidates, Options)` call drops its second argument, and
the `Card(...)` helper and length literals read `CardBounds` rather than `Options`. The EF-model
assertion compares `GetMaxLength()` against the constants, with its reason string rewritten to name
`CardBounds` instead of `Generation:MaxPromptCharacters`. The configured-equals-bound assertion at
lines 87-88 is deleted: with no configuration key and no property, it has nothing left to compare.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build TenExCards/TenExCards.slnx`
- Tests pass: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
- No pending model change: `dotnet ef migrations has-pending-model-changes --project TenExCards/TenExCards/TenExCards.csproj`
  reports none, proving the constants equal the literals they replaced
- `grep -rn "MaxPromptCharacters\|MaxAnswerCharacters" TenExCards --include=*.cs --include=*.json`
  returns only `CardBounds.cs` and its three readers, plus the test file — no `GenerationOptions`
  property and no `appsettings.json` key
- The EF-model assertion is observed failing with `CardBounds.MaxPromptCharacters` changed to `501`,
  proving it still guards the pair

#### Manual Verification:

- A local generation against `sqldb-tenexcards-dev` still produces candidates and saves an accepted
  one, with no visible change to bounds behaviour
- The request sent to Gemini still carries `maxLength: 500` / `maxLength: 1000` in its response
  schema, read from the logged or captured request rather than inferred from the code

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 2: Deploy, verify, and record

### Overview

The push deploys. Confirm the refactor changed nothing on the live instance, and record the decision
where the next agent will find it.

### Changes Required:

#### 1. The repository record

**File**: `TenExCards/AGENTS.md`

**Intent**: State the rule, so the next agent proposing a configurable bound finds the reason it is
not one.

**Contract**: A short entry recording that the two card length bounds are `const int` in
`CardBounds` and deliberately not configurable — because the value is compiled into the `AddCards`
migration, so a configuration key could only ever widen the gate in front of a column it cannot
widen, and a card that passes the gate then fails at `SaveAsync`. It names the three readers and
says that changing a bound means a migration.

#### 2. The change record

**File**: `context/changes/card-bounds/change.md`

**Intent**: Close the change.

**Contract**: `status` and `updated` stamped on completion.

### Success Criteria:

#### Automated Verification:

- The `main` workflow run is green across the full step list
- `scripts/verify_deploy.py` passes in that run

#### Manual Verification:

- A generation on the deployed instance still returns candidates and saves an accepted card
- The live `Cards` table columns are unchanged — still `nvarchar(500)` and `nvarchar(1000)` —
  confirmed by query against `sqldb-tenexcards`
- The App Service startup log reports no pending migrations, confirming the model snapshot did not
  move
- `TenExCards/AGENTS.md` states the rule and names the three readers

---

## Testing Strategy

### Unit Tests:

- `CandidateBounds`: candidates at exactly each limit are kept, one over either limit is dropped,
  only the offender is dropped and order is preserved, and a dropped candidate is not truncated —
  all existing assertions, re-pointed at `CardBounds`
- The EF model's `GetMaxLength()` for `Prompt` and `Answer` equals the constants — the one guard
  that survives the middle term's removal, and the reason `CardBounds`' XML doc points at it

### Integration Tests:

None added. The change has no runtime behaviour of its own; the empty-migration check in Phase 1 is
the assertion that stands in for one, and it is about the model rather than the code.

### Manual Testing Steps:

1. Run `has-pending-model-changes` — "none" is the pass, and anything else is the defect
2. Generate locally and accept a card; confirm nothing about bounds behaviour is visibly different
3. Capture the outgoing Gemini request and confirm the schema still carries both `maxLength` values
4. After the deploy, query `sqldb-tenexcards` for the two column definitions and confirm neither
   moved

### What is deliberately not tested

That a configuration key no longer works. There is no key and no property, so there is nothing to
assert against — the absence is proven by the `grep` in Phase 1's automated criteria, not by a test.

## Performance Considerations

None. A `const int` is inlined at the call site, replacing an `IOptions<>` property read.

## Migration Notes

There is no migration, and proving that is Phase 1's third automated criterion. `HasMaxLength(500)`
and `HasMaxLength(CardBounds.MaxPromptCharacters)` produce an identical model snapshot; if
`has-pending-model-changes` reports one, the constant does not equal the literal and the fix is the
constant, never the migration.

The deployed database is untouched by this change, so there is no forward-only step and no rehearsal
against `sqldb-tenexcards-dev` beyond the ordinary local run.

## References

- Blocked slices, each of which loses its bounds-extraction work once this lands:
  `context/changes/edit-before-accepting/plan.md` (Phase 2),
  `context/changes/manage-saved-cards/plan.md` (Phase 1),
  `context/changes/manual-card-entry/plan.md` (Phase 1)
- The compile-time-constant requirement: `context/changes/manual-card-entry/plan.md:91-92`
- The silent-unmatched-key trap, identified first for the opposite move:
  `context/changes/manage-saved-cards/plan.md:183-187`
- The current readers: `TenExCards/TenExCards/Data/AppDbContext.cs:47-49`,
  `TenExCards/TenExCards/Generation/CandidateBounds.cs:11-20`,
  `TenExCards/TenExCards/Generation/GeminiCardCandidateGenerator.cs:152-153`
- Repository rules: `TenExCards/AGENTS.md`, `TenExCards/TenExCards.Tests/AGENTS.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not
> rename step titles. See `references/progress-format.md`.

### Phase 1: One constant, three readers

#### Automated

- [x] 1.1 Solution builds — 2d843df
- [x] 1.2 Tests pass — 2d843df
- [x] 1.3 `has-pending-model-changes` reports none — 2d843df
- [x] 1.4 `grep` finds no `GenerationOptions` property and no `appsettings.json` key for either bound — 2d843df
- [x] 1.5 EF-model assertion observed failing with a constant changed — 2d843df

#### Manual

- [x] 1.6 Local generation and accept still work against `sqldb-tenexcards-dev` — 2d843df
- [x] 1.7 The outgoing Gemini request still carries both `maxLength` values — 2d843df

### Phase 2: Deploy, verify, and record

#### Automated

- [ ] 2.1 `main` workflow run green across the full step list
- [ ] 2.2 `scripts/verify_deploy.py` passes in that run

#### Manual

- [ ] 2.3 Live generation and accept still work
- [ ] 2.4 Live `Cards` columns unchanged at `nvarchar(500)` / `nvarchar(1000)`
- [ ] 2.5 Startup log reports no pending migrations
- [ ] 2.6 `TenExCards/AGENTS.md` states the rule and names the three readers
