<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Manual Card Entry

- **Plan**: `context/changes/manual-card-entry/plan.md`
- **Scope**: Phases 1 and 2 (complete), Phase 3 partial — deploy half outstanding
- **Date**: 2026-09-13
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 5 warnings, 6 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

Automated criteria re-run at review time: `dotnet build` clean, unit suite 94/94, E2E suite 6/6,
`has-pending-model-changes` reports none, suite green with `secrets.json` moved aside.

## Findings

### F1 — A double-click still writes two identical cards

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `TenExCards/TenExCards/Components/Pages/CardEntry.razor.cs:52`
- **Detail**: Post-redirect-get closes duplication on **refresh**, which is what the plan asked for.
  It does not close duplication on a **double-click** of the submit button, or Back-then-Save from
  the redirect target: antiforgery tokens are not single-use, so both POSTs succeed and write two
  rows with different `Guid`s. `Generate.razor.cs:62` has a `_busy` guard for exactly this on the
  triage path; a static form cannot have a circuit-based equivalent. With `S-04` unbuilt there is no
  way to find or delete the duplicate. The AGENTS.md block added by this change reads as though
  duplication is fully closed.
- **Fix A ⭐ Recommended**: Correct the AGENTS.md claim and record the double-submit window as a
  known, accepted gap.
  - Strength: Honest about what PRG does and does not cover; costs nothing and misleads nobody.
  - Tradeoff: The gap stays open until `S-04` gives the learner a way to delete.
  - Confidence: HIGH — the mechanism is well understood and the blast radius is one extra row.
  - Blind spot: How often a learner actually double-clicks this button is unmeasured.
- **Fix B**: Carry a submission nonce in the form and use it as `Card.Id`, so the second POST
  collides on the primary key.
  - Strength: Actually closes it server-side.
  - Tradeoff: Needs an `ICardStore` signature change, which the plan's "What We're NOT Doing"
    explicitly forbids, and a swallowed-PK-violation path of its own.
  - Confidence: MEDIUM — correct in principle, but it crosses a stated plan boundary.
  - Blind spot: Interaction with EF's `EnableRetryOnFailure` on a PK collision is unverified.
- **Decision**: FIXED via Fix A

### F2 — A failed save logs nothing at all

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `TenExCards/TenExCards/Components/Pages/CardEntry.razor.cs:55-60`
- **Detail**: `catch (Exception)` swallows everything with no `ILogger`. Both pages this one is
  modelled on inject one (`Login.razor.cs:16`, `Register.razor.cs:19`). A SQL outage, a schema drift
  after a forward-only migration, or a column-width mismatch all produce the same learner-facing
  sentence and nothing in the App Service log stream.
- **Fix**: Inject `ILogger<CardEntry>` and log the exception with the owner id before returning.
- **Decision**: FIXED

### F3 — Cancellation token diverges from the other write route

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `TenExCards/TenExCards/Components/Pages/CardEntry.razor.cs:53`
- **Detail**: `Generate.razor.cs:236` passes `CancellationToken.None` to the same `SaveAsync`,
  deliberately, so an in-flight save completes. This page passes `HttpContext.RequestAborted`, so
  navigating away mid-POST cancels the insert. If cancellation lands after the commit the learner is
  told "Nothing was lost — try again", retries, and gets the permanent duplicate of F1.
- **Fix**: Pass `CancellationToken.None` to match the sibling route, with a comment saying why.
- **Decision**: FIXED

### F4 — Success banner renders above the failure banner

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `TenExCards/TenExCards/Components/Pages/CardEntry.razor:13-23`
- **Detail**: The success banner is gated on `WrittenThisVisit > 0`, a GET-time query value, not on
  this request having saved anything. Save card 1, land on `?written=1`, type card 2, have the save
  fail: the form POSTs back to the same URL including the query string, so the re-render shows
  "Card saved. 1 card written this visit." directly above "That card could not be saved just now."
  A learner reading top-down concludes it saved.
- **Fix**: Gate the banner on `_saveError is null` as well, and word it as a running tally rather
  than a per-request confirmation.
- **Decision**: FIXED

### F5 — Nothing asserts that a manual card is `Edited = false`

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `TenExCards/TenExCards.Tests/ManualCardEntryTests.cs:76-94`
- **Detail**: The code carries a comment saying `edited: false` is load-bearing for `S-06`'s edit
  rate, but no test pins it. Flip the argument to `true` and all 94 tests stay green while the
  "edit rate measures generation" invariant is silently corrupted — and `S-06` cannot backfill it,
  because the flag is only observable at acceptance. `TenExCards.Tests/AGENTS.md` says a comment is
  exactly what a later agent removes as an inconsistency; the assertion is what stops that.
- **Fix**: One line — assert `cards[0].Edited` is false with the reason.
- **Decision**: FIXED

### F6 — Whitespace handling differs between the two write routes

- **Severity**: 💬 OBSERVATION
- **Dimension**: Pattern Consistency
- **Location**: `TenExCards/TenExCards/Components/Pages/CardEntry.razor.cs:53`
- **Detail**: The triage edit path trims prompt and answer before saving (`Generate.razor.cs:216`);
  manual entry saves verbatim, so hand-written cards can carry leading and trailing whitespace that
  generated ones cannot.
- **Fix**: Trim before calling the store, matching the other route.
- **Decision**: FIXED

### F7 — `[ExcludeFromInteractiveRouting]` difference is unrecorded

- **Severity**: 💬 OBSERVATION
- **Dimension**: Pattern Consistency
- **Location**: `TenExCards/TenExCards/Components/Pages/CardEntry.razor:1`
- **Detail**: The Identity pages carry the marker via their `_Imports.razor`; this page does not.
  Inert today — `App.razor` never calls `AcceptsInteractiveRouting()` — but if global interactive
  routing is ever adopted, the Identity forms are protected and `/cards/new` is not: `HttpContext`
  would be null and the save would throw inside a circuit handler. AGENTS.md claims the page follows
  the Identity `EditForm`-POST pattern without noting the difference.
- **Fix**: One sentence in the AGENTS.md block, so it is a recorded decision rather than an omission.
- **Decision**: FIXED

### F8 — The refusal test does not pin the message

- **Severity**: 💬 OBSERVATION
- **Dimension**: Success Criteria
- **Location**: `TenExCards/TenExCards.Tests/ManualCardEntryTests.cs:130`
- **Detail**: The test asserts `200` and an unchanged row count, which does discriminate a missing
  gate, but cannot tell "the learner was told which field and what limit" from "a generic error
  banner appeared". `lessons.md` → *prove the check before trusting the result*.
- **Fix**: Assert the rendered body contains the limit text.
- **Decision**: FIXED

### F9 — E2E test never follows the link it is testing

- **Severity**: 💬 OBSERVATION
- **Dimension**: Success Criteria
- **Location**: `TenExCards/TenExCards.E2E/LearnerJourneyTests.cs:139-145`
- **Detail**: The one thing only a browser can prove — that clicking this link under enhanced
  navigation lands on a working statically-rendered `/cards/new` — is asserted nowhere. The
  integration tests reach the route directly, which is a different code path. There is also a
  redundant navigation before `GenerateAsync` re-navigates.
- **Fix**: Click the link and expect the "Write a card" heading before generating.
- **Decision**: FIXED

### F10 — Unplanned E2E test and at-the-limits test

- **Severity**: 💬 OBSERVATION
- **Dimension**: Scope Discipline
- **Location**: `TenExCards/TenExCards.E2E/LearnerJourneyTests.cs`, `ManualCardEntryTests.cs:134`
- **Detail**: Neither is in the plan's Changes Required. The E2E test automates the plan's own manual
  criterion 2.11; `Post_AtExactlyTheLimits_IsAccepted` guards the other side of a boundary the plan
  named. Both pin contracts the plan states. Note the E2E suite is `continue-on-error: true` in CI,
  so it cannot fail a deploy.
- **Fix**: Keep both; they are additive verification of stated contracts, not new behaviour.
- **Decision**: ACCEPTED

### F11 — Test helper duplication

- **Severity**: 💬 OBSERVATION
- **Dimension**: Pattern Consistency
- **Location**: `TenExCards/TenExCards.Tests/ManualCardEntryTests.cs:29-72`
- **Detail**: `ValidPassword`, `UniqueEmail`, `CreateNoRedirectClient` and `RegisterAsync` are
  near-verbatim copies of `AuthBoundaryTests.cs:21-44`.
- **Fix**: Lift the register-and-hold-cookie helper into a shared internal type.
- **Decision**: SKIPPED — third copy is the point at which extraction is worth doing, but it touches
  a test file owned by another slice's history; better as its own tidy-up than folded into `S-05`.
