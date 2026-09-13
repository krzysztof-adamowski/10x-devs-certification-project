---
change_id: card-bounds
title: Card length bounds become compile-time constants
status: archived
created: 2026-09-13
updated: 2026-09-13
archived_at: 2026-09-13T20:37:30Z
---

## Notes

Step 0 for `S-03`, `S-04` and `S-05`. All three open by hoisting the 500/1000 card length bounds
out of `GenerationOptions`, and the two designs that were written down are mutually exclusive:
`manage-saved-cards` makes them config-bound settable properties on a new `CardOptions` and deletes
the `GenerationOptions` ones; `manual-card-entry` makes them `const int` on a new `CardBounds` and
keeps the `GenerationOptions` ones defaulted from them. Whichever landed second would have had its
entire Phase 1 invalidated.

The contradiction is not merely duplicated work. `manual-card-entry` needs a **compile-time
constant**, because `[MaxLength(...)]` DataAnnotations take one — so `CardOptions` as specified
blocks its form-validation approach outright. `edit-before-accepting` is implicated too: its Phase 2
adds `CandidateBounds.IsWithinColumnLimits(..., GenerationOptions)`, signed against the very
properties `manage-saved-cards` deletes.

Resolved here once, ahead of all three: `const int` only, no settable mirror. The number is baked
into the `AddCards` migration, so a configuration key that cannot widen the column is a knob that
only ever breaks the app.
