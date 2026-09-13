# Manual Card Entry — Plan Brief

> Full plan: `context/changes/manual-card-entry/plan.md`

## What & Why

`S-05` / FR-012: a signed-in learner writes a flashcard by hand — prompt and answer — and saves it
without generating one first. It completes the card lifecycle model and is the reason the PRD's
"three quarters of cards come from generation" target means anything: the manual path has to exist
in order to be the minority.

Scaffolded first under the mis-typed change-id `manual-card-retry`, which matched nothing in the
roadmap, the PRD or the code. Re-created as `manual-card-entry`, the roadmap's own id.

## Starting Point

`S-02` landed generation and triage on 2026-09-13, and it built the receiving end of this slice
already: `ICardStore.SaveAsync` takes an `origin` parameter, `CardOrigin.Manual = 2` is defined with
a comment naming `S-05` as its future writer, and the `AddCards` migration wrote `Origin` as an
`int` column. `/generate` is currently the only path that writes a card. Nothing in
`TenExCards/AGENTS.md` mentions `S-05` at all.

## Desired End State

On `/generate`, before pasting, a secondary line offers to write a card by hand. It leads to
`/cards/new`: a statically rendered form with a prompt field, an answer field, and a two-line
reminder that a card should test one load-bearing claim and admit one defensible answer. Saving
returns an empty form with a count of cards written this visit. Refreshing writes nothing. An
over-length field is refused with the limit named and the typed text intact.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Render mode | Static SSR form POST, no `@rendermode` | Holds zero circuit state on a 1.75 GB B1 box, and `AGENTS.md` says form-posting pages carry no render mode. |
| Entry point | `/cards/new`, linked from the Generate page only — no nav item | Keeps generation the default path, which is what the 75%-generated target depends on. |
| After save | Redirect back to the same page, empty, with a this-visit count | A re-rendering POST would write a duplicate card on `F5`, invisible until `S-04` ships a card list. |
| Count mechanism | Query string, not a hidden field | Same number, but `F5` re-issues a harmless GET instead of re-posting. |
| Character bounds | Extract a shared `CardBounds` const pair | Collapses three copies of 500/1000 to one before the third reader exists. |
| Quality guidance | The two PRD rules that transfer, not all four | "Reformulated rather than copied" has no source to copy from, and "no duplicate in the set" has no set. |
| Duplicate detection | Out of scope | The PRD scopes dedup to a set; checking saved cards needs a query `ICardStore` lacks — `S-04`. |

## Scope

**In scope:** a statically rendered page at `/cards/new`; saving with `CardOrigin.Manual`; a
stage-scoped link from `Generate.razor`; extraction of `CardBounds`; account-boundary, origin and
over-length tests; deploy and live verification; closing the roadmap and `AGENTS.md` record.

**Out of scope:** duplicate detection against saved cards; nav-menu entry or `Home.razor` link; edit
or delete (`S-04`); measuring the AI-origin share (`S-06`); any migration, `ICardStore` change, new
DI registration, package, or interactivity.

## Architecture / Approach

The page follows `Login.razor`, not `Generate.razor`: `EditForm method="post"` with
`[SupplyParameterFromForm]`, `DataAnnotationsValidator`, and a redirect on success through the
already-registered `IdentityRedirectManager`. It lives in `Components/Pages/`, where the fallback
authorization policy gates it with no attribute — **not** `Components/Account/Pages/`, whose
`_Imports.razor` would silently make it anonymous. The write goes straight to the existing
`ICardStore` with `CardOrigin.Manual`; no layer below the page changes.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Confirm the bounds | A gate, not a code change: `CardBounds` exists as `const int` and no settable mirror survives | Re-doing the extraction here instead of consuming `card-bounds` recreates the conflict the sequencing removed |
| 2. The page | `/cards/new`, the stage-scoped link, and the boundary tests | Placing the link in the triage branch would discard an untriaged batch silently |
| 3. Deploy and close | Live verification; `Card.cs`, `AGENTS.md` and roadmap brought up to date | The live duplicate-on-refresh check needs a row count taken *before* the refresh |

**Prerequisites:** the `card-bounds` change must land first — it absorbed this plan's original
Phase 1. `S-02` code is complete (35/39 plan items done; the four open are Phase 5
documentation checks, not code). No app setting, secret, or infrastructure change is needed before
the merge — unusually for this repository, nothing must exist in Azure before the build that reads it.

**Estimated effort:** ~1-2 sessions. Phase 1 is a handful of lines; Phase 2 is one page plus tests;
Phase 3 is a push and a live check.

## Open Risks & Assumptions

- **The unload warning does not survive enhanced navigation.** `Generate.razor.js` registers
  `beforeunload`; Blazor intercepts in-app links with a fetch, which never raises it. The link is
  confined to the compose branch for that reason, and the manual criterion checks the rendered triage
  markup rather than trusting the code review.
- **The this-visit count is learner-editable** — it is in the URL. It is display-only by design and
  no branch may depend on it.
- **The bounds extraction is no longer this slice's work.** `card-bounds` landed `CardBounds` as
  `const int` and deleted the `GenerationOptions` properties and configuration keys outright, so the
  drift assertion is two-way rather than the three-way check this plan originally specified. Phase 2
  must not reintroduce a settable length.

## Success Criteria (Summary)

- A signed-in learner writes a card by hand on the live site, and it is still there after a refresh —
  `Origin = 2`, scoped to their account, invisible to any other.
- Refreshing straight after a save adds no second card.
- Generation is still the obvious path: no nav item, no home-page link, and the manual link never
  appears where leaving the page would cost the learner an untriaged batch.
