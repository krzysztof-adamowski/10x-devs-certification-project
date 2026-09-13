# Edit Before Accepting — Plan Brief

> Full plan: `context/changes/edit-before-accepting/plan.md`

## What & Why

`S-03` lets a learner correct a candidate card's wording and then accept the corrected version
(FR-008). It closes the last gap in US-01's first acceptance criterion, which requires **accept,
reject and edit** at equal prominence and equal effort — a criterion `S-02` could not satisfy
because edit did not exist. It also lands the externally required learner-perspective end-to-end
test, which the roadmap moved onto this slice for exactly that reason.

## Starting Point

Triage works and is live: one `@rendermode InteractiveServer` component holds the batch, renders one
candidate read-only, and offers two identical buttons. `CandidateBounds` keeps over-long *generated*
candidates out of the 500/1000-character columns, but nothing sits between an edit and `SaveAsync`.
`Card` records `Origin` and nothing about editing. There is no browser-driven test anywhere and no
tool chosen for one.

## Desired End State

Mid-triage the learner sees three identical buttons — Keep, Discard, Edit wording. Edit swaps the
card body in place for two bounded, counted fields seeded from the generated text, offering Keep and
Cancel. Keep saves the edited wording and advances; Cancel restores the original and every original
option. A card can never be saved empty or over-length, and a failed save leaves the learner's
typing intact. Every accepted card records whether it was edited. A Playwright suite walks the whole
journey — register, paste, discard one, edit and accept one, accept one untouched, read the summary
— in CI, without gating the deploy.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Edit affordance shape | Three sibling buttons, in-place editor | The only shape that literally satisfies "equal prominence and equal effort" without a new layout. |
| Commit model | Keep + Cancel inside edit mode | FR-008 makes edit a step *before* accepting, and the original wording stays recoverable until commit. |
| Invalid edit | Block accept, keep reject open | Mirrors the compose form's counter-and-recheck pattern; truncation would be silent loss, which the guardrail forbids. |
| What counts as "edited" | Ordinal comparison after trimming | Measures what the Secondary criterion means, without a stray newline inflating a 25% threshold. |
| Where the flag lives | New `Edited` column on `Card` | FR-013 wants origin share and edit rate as independent numbers; widening `CardOrigin` would conflate them. |
| Who captures it | This slice, not `S-06` | The candidate is discarded at triage, so acceptance is the only moment the fact exists — `S-06` cannot backfill it. |
| E2E test scope | In this slice, in CI, non-gating | Satisfies the requirement with visible evidence, without putting a browser test on the path that blocks production deploys. |
| E2E tool | Playwright for .NET | Auto-waiting suits Blazor Server's async DOM patching, and it keeps the suite in C#. |
| E2E harness | `Testing:E2E` flag, `#if DEBUG` | User's choice over a Kestrel-hosted factory; the `#if DEBUG` guard plus a Debug-only package reference keeps the seam out of the Release artifact. |

## Scope

**In scope:** the edit sub-state and its bounds gate; the `Edited` column and its migration; the two
pure functions that decide committability and edit-ness; the `TenExCards.E2E` project, the Debug-only
harness, and one non-gating CI step; live verification; the repository record.

**Out of scope:** editing or deleting an already-saved card (`S-04`); manual card entry (`S-05`);
reporting any rate (`S-06`); recording rejections; persisting an in-progress edit; regenerating a
candidate; any friction discouraging edits; infrastructure or app-setting changes.

## Architecture / Approach

Inside-out, the order `S-02` used. The component stays thin because the two decisions worth testing
— *does this fit the columns* and *was this actually changed* — become pure functions beside the
existing `CandidateBounds`, since an `InteractiveServer` component cannot be driven by the HTTP
harness. The edit buffer lives in the component and commits only on a successful save, so a failure
leaves the buffer, the edit mode and the original candidate all intact. The end-to-end harness
registers a scripted generator and an in-memory store behind a flag that is both Development-gated
and `#if DEBUG`-gated, with the in-memory package referenced only in Debug — so the seam is
physically absent from the Release archive CI publishes.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. The `Edited` column | Entity, store parameter, forward-only migration | Migrations are one-way and run on the production boot path; a bad one means the container does not serve |
| 2. The edit affordance | Three buttons, in-place editor, bounds gate, the flag written | An edit bypassing the bounds gate reaches a `nvarchar(500)` column and blocks the learner mid-triage |
| 3. The end-to-end test | `TenExCards.E2E`, the Debug-only harness, one non-gating CI step | The harness leaking into a Release build would ship a test provider to production |
| 4. Deploy and verify | Both flag values confirmed in the app's database, live | Only this phase can prove the column and the circuit behave on B1 |
| 5. Update the record | `AGENTS.md` ×3, roadmap, lessons | Facts duplicated across files drift apart silently |

**Prerequisites:** the `card-bounds` change must land first — it extracts the 500/1,000 limits to
`const int` and drops the `GenerationOptions` parameter from `CandidateBounds`, which is the shape
Phase 2 is signed against. `S-02` complete — its Phase 5 record update is still uncommitted in the
working tree and should land first, since this slice edits the same roadmap entry. Access to
`sqldb-tenexcards-dev` (the dev-machine firewall rule must match the current IP) and to
`sqldb-tenexcards` for Phase 4's queries.

**Estimated effort:** ~3–4 sessions across 5 phases; Phase 3 is the largest by a clear margin.

## Open Risks & Assumptions

- **The premise that this instance has no learners still holds.** Every phase commit deploys to
  production and Phases 1–3 carry no post-push gate beyond Phase 1's startup-log check. The first
  real learner retires that premise.
- **Playwright in CI is new ground here.** Browser install, a spawned application process and a
  fixed port are three things that have never run in this workflow; `continue-on-error` is what
  keeps a surprise from blocking a deploy.
- **The `#if DEBUG` guard is enforced by a `Condition` on a package reference.** Phase 3's
  Release-publish check is the only thing that proves it; if that check is skipped, nothing else
  reports a leak.
- **`S-06` may want a richer shape than a boolean.** The column is additive and cheap to leave in
  place if so, but it is a commitment made before `S-06` is designed.

## Success Criteria (Summary)

- A learner can fix a candidate's wording and keep it, in no more steps than accepting one
  untouched, and rejecting stays the cheapest action of the three.
- No accepted card is ever empty, truncated, or lost to a failed save — the learner's typing survives
  and a retry saves exactly once.
- The edit rate becomes measurable: every card accepted from now on records whether the learner had
  to intervene.
