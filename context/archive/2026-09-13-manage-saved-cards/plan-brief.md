# Manage Saved Cards — Plan Brief

> Full plan: `context/changes/manage-saved-cards/plan.md`

## What & Why

`S-04` lets a learner **find one of their own saved cards in order to edit or delete it** —
`FR-009`, `FR-010`, `FR-011`. Until now the product is append-only: a card can be created and
counted, never read back, changed or removed. A learner who accepts a card with a typo, or one that
turns out to be wrong, currently has no recourse at all.

The requirement's exact wording is load-bearing. `FR-009` was rewritten during shaping from "view
every card" to "find a card in order to edit or delete it", because nobody browses a card deck, and
`## Non-Goals` says the list "exists to find a card for editing or deletion rather than to browse".
Every scope decision below follows from that narrowing.

## Starting Point

`S-02` landed and deployed the write path on 2026-09-13. `Card` exists with `OwnerId`, `Prompt`
(max 500), `Answer` (max 1000), `Origin` and `CreatedAt`. `ICardStore` has two members — `SaveAsync`
and `CountForOwnerAsync` — each taking `ownerId` as a required first parameter, with no ambient-user
overload by design. `/generate` is the one interactive page and the pattern to follow; its summary
screen deliberately links nowhere because this surface did not exist. Authorization defaults to
protected, so a new page is gated without an attribute.

## Desired End State

A signed-in learner opens `/cards` and sees their twenty most recent cards, newest first. Typing
narrows to cards whose prompt or answer contains what they typed, case-insensitively. Any row offers
Edit — which turns the row into a bounded form saved in place — and Delete, which becomes "Delete
permanently / Cancel" and, on the second click, removes the card with no undo. No card belonging to
another account is reachable, and an attempt is indistinguishable from a card that never existed.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Find surface | Search box **plus** the ~20 most recent; no paging, no sort controls | The realistic v1 repair trigger is a card just accepted, which recency serves, while search serves "I remember the topic" — and the cap is what keeps the browse Non-Goal out. |
| Search matching | Case-insensitive substring over **prompt and answer**, lowered on both sides inside the query | The in-memory test provider is case-sensitive and Azure SQL's collation is not, so lowering both sides is what makes the suite assert what production actually does. |
| Edit surface | **Inline on the row**, one `/cards` page, `@rendermode InteractiveServer` | One component and no guessable-Guid route to defend across accounts; matches `/generate`'s single-interactive-page shape. |
| Delete friction | **Two-step in place** — Delete becomes "Delete permanently / Cancel" | Deliberate without a modal or interop, and nothing extra to persist; the PRD forbids only *silent* loss. |
| Schema | **No migration** | The PRD's tracked edit rate is edit-*before*-saving (`S-03`), so no named requirement buys a post-save marker — and this removes the slice's only boot-path risk. |
| Length bounds | **Not this slice** — `CardBounds` consts, landed by the prerequisite `card-bounds` change | They are compiled into `AddCards`, so a settable mirror could only widen a gate in front of a column it cannot widen; `S-05` also needs a compile-time constant. |
| Result cap | **`CardOptions.MaxResults`**, a new `Cards` section carrying that key alone | Genuinely a runtime tuning decision, unlike the lengths, and shared by the recency list and search. |
| Store contract | `Card?` / `bool`, every member owner-filtered | "Not yours" and "does not exist" answer identically, so nothing leaks the existence of another account's card. |
| Find shape | **One method**, a null term meaning "most recent" | The default list and search share a single owner-filtered query; there is no second query shape that could be written without the filter. |
| Testing | Store ownership tests + route gating + a pure `CardEdit` rule; **no bUnit** | Follows the `PassageBounds`/`CandidateBounds` pattern; a component-rendering library would widen a deliberately narrow test-double rule and bypass the real auth pipeline. |
| Sequencing | Edit and delete ship **together, one merge** | They share the find surface and three of the four store members; splitting duplicates the review, deploy and verification for almost no saving. |

## Scope

**In scope:** `CardOptions` carrying the shared result cap; a pure `CardEdit` validation rule; four owner-scoped store members (`FindForOwnerAsync`, `GetForOwnerAsync`,
`UpdateForOwnerAsync`, `DeleteForOwnerAsync`) with cross-account tests on each; a gated interactive
`/cards` page with search, recency, inline edit and two-step delete; nav, Home and triage-summary
entry points; `AGENTS.md`; and live verification including a two-account boundary probe.

**Out of scope:** any schema change or migration; paging, sorting, show-all, decks, tags, export,
bulk operations; undo or soft delete; editing a candidate before accepting (`S-03`); manual card
creation (`S-05`); recording edit or delete outcomes (`S-06`); changing a card's `Origin` on edit;
optimistic concurrency between two tabs; any infrastructure change or new package.

## Architecture / Approach

Built below the component first, as `S-02` was, because an `@rendermode InteractiveServer` page
cannot be driven by the `WebApplicationFactory` HTTP harness — so anything decidable must live in a
type that can be asserted without rendering. Phase 1 adds `CardOptions` for the shared result cap,
adds the pure `CardEdit` rule, and extends `ICardStore` with four members that each filter on
`ownerId` and answer a cross-account request exactly as they answer a request for a card that never
existed. Phase 2 is one component holding presentation and state only: a search term, a result list,
and two `Guid?` fields naming which row is being edited and which is confirming deletion. Phases 3
and 4 wire the entry points and verify on the live instance.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. The cap and the store | `CardOptions.MaxResults`, `CardEdit`, four owner-scoped store members, cross-account tests | The slice's first read, mutation and deletion all land at once — every one of them a place the owner filter can be forgotten |
| 2. The `/cards` page | The slice's outcome as one gated interactive page | Per-keystroke queries can return out of order; per-row state keyed by index breaks when the list shortens |
| 3. Entry points and record | Nav, Home and triage-summary links; `AGENTS.md` | `S-02`'s open Phase 5 also edits `AGENTS.md`, and the file already has uncommitted changes |
| 4. Deploy and verify live | Merge, pipeline, and the checks the verifier cannot make | `verify_deploy.py` asserts status only — the cross-account probe and the edit/delete persistence are manual by necessity |

**Prerequisites:** the `card-bounds` change must land first — it owns the length-bounds extraction
this plan's Phase 1 originally described. `S-02` functionally landed (it has; its Phase 5 repository-record work is still
open, which does not block this). Local development pointed at `sqldb-tenexcards-dev` with the
`dev-machine-krzychu` firewall rule current for your IP. Two accounts on the live site for the
Phase 4 boundary probe.

**Estimated effort:** ~2–3 sessions across four phases; Phase 2 is the largest.

## Open Risks & Assumptions

- **`S-02`'s Phase 5 is still open and edits the same `AGENTS.md` this slice edits.** `AGENTS.md`,
  `deploy-plan.md` and `roadmap.md` all carry uncommitted changes at planning time.
  `lessons.md`'s commit-by-path rule applies to Phase 3 directly — verify the staged diff, prefer
  `git commit --only`, never `git add -A`.
- **The search predicate is non-sargable by choice.** Lowering both sides defeats any index on the
  text columns. Acceptable behind the `OwnerId` index at `target_scale.data_volume: small`, and
  recorded so a future scale problem is recognised rather than rediscovered.
- **Stale-query cancellation is unverifiable locally.** A slow earlier query overwriting a newer
  result needs real latency to appear; it is a Phase 4 live check, not a local one.
- **No optimistic concurrency.** Two tabs of the same learner editing one card is last-write-wins; a
  card deleted in one tab reports as gone in the other. Accepted, not solved.
- **The component's behaviour is manually verified only**, and manual checks decay. Mitigated by the
  fact that almost nothing decidable lives in the component.
- **The twenty-row cap is the whole defence against the browse Non-Goal.** It has no test behind it,
  only the `AGENTS.md` note Phase 3 adds. A later agent adding paging would be doing something that
  looks like an improvement.

## Success Criteria (Summary)

- A learner can find one of their own cards by a word from its prompt *or* its answer, correct it,
  and see the correction survive a refresh on the live site.
- A learner can delete a card deliberately, in two steps, and it is gone.
- No query, on any of the four new store members, crosses an account boundary — asserted in the
  suite, each test observed failing with its filter removed, and probed live with two accounts.
