# Triage Outcomes and Card Origin Are Recorded — Plan Brief

> Full plan: `context/changes/outcome-recording/plan.md`

## What & Why

`FR-013` requires the product owner to be able to determine three rates — acceptance, AI-origin
share, and edit rate — because nothing in `FR-001`…`FR-012` records a triage outcome or a card's
origin, leaving every number in the PRD's `## Success Criteria` unmeasurable. Two of the three are
already recorded on the `Cards` table. The third, the acceptance rate, is **uncomputable today**,
because a rejected candidate leaves no trace anywhere.

## Starting Point

`Card.Origin` and `Card.Edited` already record how each saved card was created. `TriageSession`
counts saves, discards and edits in memory, and those counters die with the circuit — `RejectAsync`
simply drops the candidate. App Service keeps only three days of filesystem logs and there is no
Application Insights, so a log line is not a place a multi-week rate can live. `S-04` also shipped
delete, which now removes `Origin` and `Edited` along with the row.

## Desired End State

A generation batch leaves one content-free row in a new `TriageBatches` table: candidate count,
accepted, rejected, edited, owner, opened-at. An abandoned batch leaves a row whose counters never
reach its candidate count, which is what makes untriaged candidates countable. The product owner
runs a committed SQL file against the live database and reads three numbers. No new page, no
operator role, no change to what the learner sees.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Acceptance denominator | Candidates **shown**, with triaged also reported | PRD says "75% of AI-generated candidate cards"; abandonment is signal that is unrecoverable if not captured | Plan |
| Deletion sensitivity | **Split sources**: origin share from `Cards`, edit rate from `TriageBatches` | The two PRD criteria mean different things — "cards in a learner's space" vs "edited before saving" | Plan |
| Population | Store `OwnerId`, query pooled, per-learner available | Matches "a learner's space" without committing a reporting shape while there is one learner | Plan |
| Record shape | One row per batch, counters updated in place | Tiny table, every rate a `SUM`, and the row exists from batch-open so abandonment survives a lost circuit | Plan |
| Write failure | Best-effort — swallow and log, inside the recorder | Measurement must never cost a learner a card; one swallow site keeps future callers safe by construction | Plan |
| Fields stored | Counts, owner, timestamp only | The workable privacy line is "nothing from which passage content could be reconstructed", not the literal "nothing derived from the passage" | Plan |
| Read path | Committed `scripts/outcome_rates.sql` + runbook | Matches the `pack.py` / `verify_deploy.py` convention without adding a SQL driver dependency | Plan |
| Verification | Run it against **production** | `lessons.md`: a command's output is not a verdict until the command is proven to run | Plan |
| Tests | `TenExCards.Tests` only, absence check as exact-set equality | All decidable without a browser; E2E does not gate the deploy, and a "does not contain" assertion cannot fail | Plan |

## Scope

**In scope:** `TriageBatch` entity + migration; `ITriageRecorder` / `TriageRecorder`; three call sites
in `Generate.razor.cs`; tests; `scripts/outcome_rates.sql`; runbook in `TenExCards/AGENTS.md`;
deploy and a live measurement.

**Out of scope:** any dashboard, admin page or operator route; any learner-visible change; passage
length, focus hint, candidate text or candidate ids in the record; recording failed generations;
backfill of pre-deploy batches; per-decision timestamps; changes to `TriageSession`; an E2E test.

## Architecture / Approach

```
Generate.razor.cs ──OpenBatchAsync──►  ITriageRecorder ──► TriageBatches  (counts only)
       │                                     ▲                  │
       ├── Accept ──► ICardStore.SaveAsync ──┘ RecordAccept     │  acceptance rate, edit rate
       └── Reject ─────────────────────────────RecordReject     │
                                                                │
                         Cards (Origin, Edited) ────────────────┴─► AI-origin share
```

`TriageRecorder` sits on `IDbContextFactory<AppDbContext>` exactly as `CardStore` does, so it works
unchanged under both the E2E harness and the test factory — both swap the factory, not the store.
`TriageSession` is untouched and stays I/O-free. Counter increments are read-modify-write, not
`ExecuteUpdateAsync`, which throws against the EF in-memory provider the tests use.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Record the batch | Entity, migration, recorder seam, DI, tests. Behaviour unchanged. | Migration is forward-only on the boot path; `ExecuteUpdateAsync` unusable in tests |
| 2. Wire the triage path | Three call sites in `Generate.razor.cs`. No UI change. | A second write on a path where a failure must never cost a learner a card |
| 3. Operator read path | `scripts/outcome_rates.sql` + runbook, proven against the dev database | A query proven only against an empty table proves syntax, not meaning |
| 4. Deploy and verify live | Push to `main`, migration applies, three rates read off production and recorded | Migration failure means the container does not serve; B1 has no slots |

**Prerequisites:** `S-03`, `S-04`, `S-05` all landed 2026-09-13. For Phase 3–4, the
`dev-machine-krzychu` firewall rule must be current for the operator's IP, plus Key Vault read for
`sql-admin-password` and a `sqlcmd` client.

**Estimated effort:** ~2 sessions across 4 phases. Phases 1–2 are one sitting; 3–4 depend on a real
deploy and a live batch.

## Open Risks & Assumptions

- **The numbers undercount under fault**, by design — the recorder swallows write failures so
  measurement never blocks triage. The runbook must state this rather than imply exactness.
- **No history.** The acceptance rate's epoch is the Phase 4 deploy; there is no backfill and none is
  honest to invent.
- **`NULL` is the empty-table answer**, not `0`. Read as breakage, it is exactly the false-verdict
  hazard `lessons.md` records.
- At this volume the rates are noisy — the PRD's own Socratic note on `FR-013` raises this and
  rejects it as a reason not to record. Recording remains a precondition for ever answering it.
- The two-source split is the most likely thing a later agent "simplifies" into one table. The
  runbook entry exists to stop that.

## Success Criteria (Summary)

- The product owner runs one committed file and gets the acceptance rate, the AI-origin share and the
  edit rate off the live database — verified by having actually done it, not by the file existing.
- A learner sees no difference: no new page, no new prompt, no slower triage, and no accepted card at
  risk from a measurement failure.
- The record provably carries no passage-derived content, asserted in a form that cannot pass
  vacuously.
