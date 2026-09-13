# Card Length Bounds — Plan Brief

> Full plan: `context/changes/card-bounds/plan.md`

## What & Why

The 500/1,000 character limits on a card's prompt and answer live on `GenerationOptions`, which is
wrong — they describe a `Card`, and generation is one of three readers. Three planned slices each
noticed this and each opened with a Phase 1 that fixes it, in two mutually exclusive ways. This
change fixes it once, before any of them starts, so the three can land in any order.

## Starting Point

Two numbers in five places: settable properties on `GenerationOptions`, keys in `appsettings.json`,
literals in `AppDbContext.HasMaxLength`, comparisons in `CandidateBounds`, and interpolations in the
Gemini response schema — plus a test that exists only because the EF in-memory provider ignores
`HasMaxLength` and nothing else would notice them drifting apart.

The configurability was never real: `HasMaxLength(500)` is compiled into the `AddCards` migration, so
raising the key widens the *gate in front of* the column rather than the column, and the card then
throws at `SaveAsync`.

## Desired End State

One static class, `CardBounds`, holds both numbers as `const int`. `AppDbContext`, `CandidateBounds`
and the Gemini schema builder read it. `GenerationOptions` and `appsettings.json` no longer mention
them. Runtime behaviour is byte-identical; the drift test collapses from a three-way check to a
two-way one because the middle term is gone.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Mechanism | `const int`, not an options property | `[MaxLength(...)]` DataAnnotations take a compile-time constant, so `manual-card-entry`'s form validation is blocked by anything else. |
| Configurability | None — no settable mirror anywhere | The value is baked into a migration, so a key that cannot widen the column is a knob that only ever breaks the app. |
| Type name and home | `Data/CardBounds.cs` | The number is a schema fact; every other reader is downstream of `HasMaxLength`. |
| `CardOptions` | Not created here | With the lengths const it would carry only `MaxResults`, used by `S-04` alone — so `S-04` creates it, conflict-free. |
| `appsettings.json` keys | Deleted, not left behind | The binder ignores an unmatched key silently, leaving two numbers that appear configured and are not. |
| `CandidateBounds` | Keeps its name and folder | Moving it would create exactly the churn this change exists to prevent. |

## Scope

**In scope:** `CardBounds`; re-pointing the three readers; deleting the two `GenerationOptions`
properties and the two config keys; updating `CandidateBoundsTests`; one `AGENTS.md` rule.

**Out of scope:** creating `CardOptions`; changing either number; any schema change or migration;
moving or renaming `CandidateBounds`; adding the `IsWithinColumnLimits` overload S-03 needs; a
roadmap slice.

## Architecture / Approach

One phase of code, because the edge is closed — three readers, one test file, one config section —
and splitting it would leave both the constant and the properties in place, which is precisely the
ambiguity being removed. A second phase deploys and verifies, since a push to `main` is a production
deploy and the Gemini response schema is a runtime path no unit test covers.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. One constant, three readers | `CardBounds`, re-pointed readers, deleted properties and keys, updated tests | If the constants do not equal the literals they replace, EF generates a real migration against a live column |
| 2. Deploy, verify, record | Live confirmation that nothing moved, plus the `AGENTS.md` rule | A refactor that deploys is still a deploy |

**Prerequisites:** none beyond `S-02` being in the tree. This change must land **before** the first
of `S-03`, `S-04` or `S-05` starts implementing.

**Estimated effort:** ~1 session across 2 phases.

## Open Risks & Assumptions

- **`has-pending-model-changes` is the only proof that nothing moved**, and it is easy to skip on a
  refactor that "obviously" changes nothing. Skipping it means a schema change discovered on the
  production boot path.
- **The three dependent plans have been amended to consume this** rather than re-derive it, but they
  are amendments to unimplemented plans: if this change lands in a different shape than planned, all
  three need revisiting before their first phase.
- **`S-04` still needs `CardOptions` for `MaxResults`.** That is not deleted scope, only relocated:
  it is now unambiguously S-04's, with nothing to collide with.

## Success Criteria (Summary)

- One place holds each number, and `grep` proves there is no second.
- `S-03`, `S-04` and `S-05` can be implemented in any order without one invalidating another's
  opening phase.
- Nothing about the running application changes — same candidates dropped, same schema sent, same
  columns in the database.
