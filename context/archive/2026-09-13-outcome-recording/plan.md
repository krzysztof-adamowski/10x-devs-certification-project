# Triage Outcomes and Card Origin Are Recorded — Implementation Plan

## Overview

`FR-013` requires that the product owner can determine three rates: the **acceptance rate** on
generated candidates, the **share of cards produced by generation** rather than typed by hand, and
the **edit rate** on accepted cards. Two of the three are already recorded. The third is not, and
nothing in the product can currently produce it.

This slice adds a content-free `TriageBatches` table — one row per generation batch, carrying
counts and nothing else — writes to it best-effort from the triage path, and ships a committed SQL
query plus a runbook that turns those rows and the existing `Cards` columns into the three rates.

It adds **no in-product surface and no operator role**, per the PRD scope note on `FR-013` and
`## Access Control`.

## Current State Analysis

**Already recorded.** `Card.Origin` (`Data/Card.cs`) distinguishes `Generated` from `Manual`, and
`Card.Edited` records whether the learner reworded a candidate before accepting
it. `S-05` writes `Manual` with `edited: false`; `S-03` writes `Generated` with the real edit flag.
`CardStore.UpdateForOwnerAsync` deliberately leaves both untouched,
because they record how a card was *created* and a later repair is neither.

**Not recorded at all.** A rejection leaves no trace. `TriageSession` (`Generation/TriageSession.cs`)
counts `Saved`, `Discarded` and `EditedCount` in memory, and `Generate.razor.cs`'s `RejectAsync`
simply drops the candidate from the pending list. Those counters die with the circuit. **The
acceptance rate is therefore uncomputable today**, and that is the gap this slice closes.

**Logs cannot hold this.** The `siteConfig` logging block in `infra/main.bicep` configures App
Service filesystem logging only
— `retentionInDays: 3`, `retentionInMb: 100`. There is no Application Insights resource and no Log
Analytics workspace in the template. A rate that accumulates over the product's life cannot live in
a three-day rotating buffer, which is why the record is a table.

**Deletion is now a live force on these numbers.** `S-04` shipped delete, and
`CardStore.DeleteForOwnerAsync` removes the row outright, taking its
`Origin` and `Edited` with it. The two PRD criteria pull in opposite directions on this, and the
plan resolves it by reading them from different sources — see `## Implementation Approach`.

**No UI change is needed.** The `Stage.Summary` branch of `Components/Pages/Generate.razor`
already shows the learner
"Saved N card(s), discarded M" at the Summary stage. That is a per-batch confirmation of their own
triage, not an operator view, and it stays exactly as it is.

## Desired End State

A completed generation batch leaves one row in `TriageBatches` carrying its candidate count and the
accepted / rejected / edited tallies, owned by the account that triaged it and containing no text of
any kind. An abandoned batch leaves a row whose counters do not reach its candidate count, which is
what makes untriaged candidates countable.

The product owner runs `scripts/outcome_rates.sql` against `sqldb-tenexcards` and reads three
numbers. Verification is that this has actually been done against the live database and the numbers
recorded in this change — not that the query exists.

### Key Discoveries

- `TriageSession`'s `Saved`, `Discarded` and `EditedCount` already track exactly the three counters
  the record needs.
  It holds no I/O by design, so the component — not the session — calls the recorder.
- `E2EHarness.UseE2EHarness` swaps the **`AppDbContext` factory**, not `ICardStore`. Anything built
  on `IDbContextFactory<AppDbContext>` therefore works under both the E2E harness and
  `TenExCardsWebApplicationFactory` with no harness change.
- `Cards/CardStore.cs` is the pattern to follow: it takes `IDbContextFactory<AppDbContext>` rather
  than a scoped context, because a DI scope in Blazor Server is the whole circuit.
- `ICardStore` requires `ownerId` on every member with no ambient-user overload
  (`ICardStore`'s own doc comment says so). The recorder follows the same rule.
- `Program.cs` registers `ICardStore` **outside** the `if (!e2e)` block. The recorder must be
  registered the same way or the E2E suite loses it.
- `Data/Card.cs` stores `CardOrigin` as its underlying `int`: `Generated = 1`, `Manual = 2`. The
  origin-share query filters on `Origin = 1`.

## What We're NOT Doing

- **No dashboard, admin page, or operator route.** `FR-013`'s scope note and `## Access Control`
  both forbid it, and `## Never do these` forbids admin views outright.
- **No change to what the learner sees.** The Summary line stays as it is.
- **No passage length, focus hint, candidate text, or candidate identifiers** in the record — see
  `## Critical Implementation Details`.
- **No recording of failed generations.** A submission that yields no candidates belongs to none of
  the three rates; adding `GenerationFailure` here widens the slice past `FR-013`.
- **No backfill.** Cards created before this deploys have `Origin` and `Edited`, but no batch ever
  existed for them. The acceptance rate's epoch is the deploy date and the runbook says so. Seeding
  synthetic batch rows would invent rejection counts nobody observed.
- **No per-decision timestamps or time-to-decide.** Not asked for by `FR-013`.
- **No change to `TriageSession`.** It stays pure and I/O-free; that property is what makes "each
  candidate is triaged exactly once" assertable without a component framework.
- **No E2E test.** That suite runs `continue-on-error: true` and would report a regression without
  stopping the deploy. The assertions here are all decidable without a browser.

## Implementation Approach

**One row per batch, counters updated in place.** The row is created when the batch opens, carrying
the candidate count; each accept and reject increments a counter on it. One row per generation keeps
the table small, makes every rate a `SUM`, and — because the row exists from the moment the batch
opens — leaves an abandoned batch countable even when the circuit vanishes without running
`DisposeAsync`.

**The three rates read from two sources, deliberately, because the two PRD criteria mean different
things:**

| Rate | Source | Deletion behaviour | PRD wording that decides it |
| --- | --- | --- | --- |
| Acceptance rate | `TriageBatches` | immune | "75% of AI-generated candidate cards are accepted" |
| AI-origin share | `Cards` | **sensitive** | "75% of **the cards in a learner's space**" |
| Edit rate | `TriageBatches` | immune | "accepted cards are edited **before saving**" |

Reading the edit rate from `Cards` would let ordinary tidying improve it — deleting an edited card
removes it from the numerator. Reading the origin share from `TriageBatches` would contradict "in a
learner's space" and would have no place to put manually written cards, which belong to no batch.

**The acceptance rate's denominator is the candidate count, not the triaged count.** The query file
reports both, but the documented answer to `FR-013` is `accepted / candidate count`, so untriaged
candidates count as not-accepted. Untriaged is reported alongside, so an unusual reading can be
attributed to abandonment rather than mistaken for card quality.

**The record is written best-effort, and the swallow lives in the recorder.** Measurement must never
cost a learner a card or block triage. Putting the `try`/`catch` inside `TriageRecorder` rather than
at each call site follows `lessons.md`, *"Put the validation gate on the call, not on one route to
it"* — a later caller is safe by construction rather than by remembering. The consequence is that the
numbers **undercount under fault**, and the runbook states that rather than implying they are exact.

**Counts are pooled, with `OwnerId` stored so per-learner grouping stays available.** The query file
ships both forms.

## Critical Implementation Details

**`ExecuteUpdateAsync` is not available here.** The atomic single-`UPDATE` form of a counter
increment throws against the EF in-memory provider that `TenExCardsWebApplicationFactory` uses, so
every recorder test would fail while production worked. The increment therefore loads the row,
mutates it and calls `SaveChangesAsync`. There is no concurrency risk: all triage for a batch runs on
one circuit dispatcher, serialised by the `_busy` re-entrancy guard (`Generate._busy`).

**The swallow must be prompt, not merely eventual.** The recorder shares the application's context
factory, and that factory is built with `sql.EnableRetryOnFailure()` and no arguments
(the `AddDbContextFactory` call in `Program.cs`) — six retries with a cumulative delay reaching 30 seconds. A `catch` alone
therefore makes the fault invisible only *after* the learner has already waited for it, behind the
`_busy` guard, on a reject path that carries no I/O today and shows no progress at all. The
batch-open call is worse: it sits in `Stage.Generating` after `StopElapsedTimer()` has run, so the
elapsed counter is frozen while the screen still reads "Reading your passage and writing cards" —
the freeze `## Never do these` forbids. Each member therefore links the caller's token to a
2-second `CancellationTokenSource` and passes the linked token to EF. The bound lives at the swallow
site for the same reason the `try`/`catch` does: a later caller inherits it by construction rather
than by remembering.

**Where the privacy line actually falls.** The roadmap's constraint reads "recording a rejection must
not retain content derived from the passage", but taken literally that forbids `CandidateCount` too —
the number of candidates is proportional to passage length per `## Business Logic`. The workable line,
and the one this plan holds, is **nothing from which passage content could be reconstructed**. Counts
satisfy it; text, candidate identifiers, focus hints and passage length do not. The last is excluded
even though it is only a number, because it is a direct measurement of the discarded passage and
admitting one such column makes the next one arguable.

**The absence check must be an exact-set assertion, not a "does not contain".** `lessons.md` records
that `strings | grep -c` answered `0` both for an absent type and for a type that was certainly
present — a broken command returning the desired answer. An assertion that `TriageBatch` has no text
property fails the same way if the model read returns nothing at all. Asserting that its mapped
property set **equals** the expected seven names cannot pass vacuously: an empty read fails equality.
The same helper applied to `Card`, returning its known property set, is the explicit control.

**Ordering on the triage path.** The accept counter is incremented only *after* `Store.SaveAsync`
has returned successfully and `_session.Accept(edited)` has run. `AcceptAsync` deliberately does not
advance the session on a failed save — its `catch` around `Store.SaveAsync` returns without touching
`_session` — so a retry re-issues the same
save; incrementing before the save would count a card that was never written, and counting inside
the `catch` would double-count on retry.

---

## Phase 1: Record the batch

### Overview

Add the entity, its model configuration, the migration, and the recorder seam with its tests. Nothing
calls the recorder yet, so the application's behaviour is unchanged at the end of this phase.

### Changes Required

#### 1. The entity

**File**: `TenExCards/TenExCards/Data/TriageBatch.cs`

**Intent**: One generation batch's triage outcome, owned by one account. Mirrors `Card.cs`'s framing
— a class comment states that there is deliberately no column for any text, for the same reason
`Card` has none for the passage.

**Contract**: Seven mapped properties and no more: `Id` (`Guid`), `OwnerId` (`string`, FK to
`AspNetUsers.Id`), `OpenedAt` (`DateTimeOffset`), `CandidateCount`, `AcceptedCount`,
`RejectedCount`, `EditedCount` (all `int`). `OpenedAt` rather than `CreatedAt` — deliberately unlike
`Card`, because the row is mutated after it is written and the name should not suggest otherwise.
The property set is pinned by a test, so adding an eighth is a red build.

#### 2. Model configuration

**File**: `TenExCards/TenExCards/Data/AppDbContext.cs`

**Intent**: Map the entity alongside `Card` with the same ownership shape, so the account boundary
and the cascade behave identically.

**Contract**: `DbSet<TriageBatch> TriageBatches`. In `OnModelCreating`: `OwnerId` required, indexed;
`HasOne<ApplicationUser>().WithMany().HasForeignKey(b => b.OwnerId).IsRequired().OnDelete(DeleteBehavior.Cascade)`
— no navigation on `ApplicationUser`, which stays empty. No `HasMaxLength` anywhere, because no
property here is text the application writes.

#### 3. Migration

**File**: `TenExCards/TenExCards/Migrations/<timestamp>_AddTriageBatches.cs` (generated)

**Intent**: Create the table. Generated with `dotnet ef migrations add AddTriageBatches`, then read
before it is trusted.

**Contract**: One `CreateTable` for `TriageBatches` plus the `OwnerId` index and FK. Review the
generated file for exactly one `nvarchar` column — `OwnerId` — and no other text column. Treat it as
forward-only: `Database.MigrateAsync()` runs on the boot path (the `Database.MigrateAsync()` block in `Program.cs`) and the `Down()` EF
generates is not a rollback story.

#### 4. The recorder seam

**File**: `TenExCards/TenExCards/Generation/ITriageRecorder.cs`

**Intent**: The only type that touches `TriageBatches`, mirroring `ICardStore`'s rule that `ownerId`
is required on every member with no ambient-user overload.

**Contract**:

```csharp
Task<Guid?> OpenBatchAsync(string ownerId, int candidateCount, CancellationToken ct);
Task RecordAcceptAsync(string ownerId, Guid? batchId, bool edited, CancellationToken ct);
Task RecordRejectAsync(string ownerId, Guid? batchId, CancellationToken ct);
```

The nullable `Guid?` is the contract that matters: `OpenBatchAsync` answers `null` when the write
failed, and the record methods no-op on `null`. The component therefore never branches on whether
recording is working, and a future caller cannot forget to.

#### 5. The recorder

**File**: `TenExCards/TenExCards/Generation/TriageRecorder.cs`

**Intent**: Implement the seam over `IDbContextFactory<AppDbContext>`, swallowing every fault so
measurement can never reach the learner.

**Contract**: Constructor takes `IDbContextFactory<AppDbContext>` and `ILogger<TriageRecorder>`.
Every member wraps its work in `try`/`catch`, logs at warning with the batch id, and returns
normally — `OpenBatchAsync` returning `null`. Every member also **bounds its own wait**: it links the
caller's token to a `CancellationTokenSource` of `RecorderTimeoutSeconds` (2, a `const int` on the
recorder) and passes the linked token to EF, so a retrying database costs the learner a bounded pause
rather than EF's full retry budget — see `## Critical Implementation Details`. The resulting
`OperationCanceledException` is swallowed like any other fault. Counter increments are read-modify-write
(`SingleOrDefaultAsync` scoped by **both** `Id` and `OwnerId`, mutate, `SaveChangesAsync`), never
`ExecuteUpdateAsync` — see `## Critical Implementation Details`. A row that does not match both
predicates is left alone and nothing is reported, matching `ICardStore`'s deliberate refusal to
distinguish "not yours" from "already gone".

#### 6. Registration

**File**: `TenExCards/TenExCards/Program.cs`

**Intent**: Register the recorder where `ICardStore` is registered.

**Contract**: `builder.Services.AddScoped<ITriageRecorder, TriageRecorder>();` immediately after the
`AddScoped<ICardStore, CardStore>()` line in `Program.cs` — **outside** the `if (!e2e)` block, so the E2E harness keeps it.

#### 7. Tests

**File**: `TenExCards/TenExCards.Tests/TriageRecorderTests.cs`

**Intent**: Pin the recorder's behaviour, the account boundary, and the best-effort contract.
AwesomeAssertions, never FluentAssertions.

**Contract**: `OpenBatchAsync` persists candidate count, owner and zeroed counters and returns the
id; `RecordAcceptAsync` increments `AcceptedCount`, and `EditedCount` only when `edited: true`;
`RecordRejectAsync` increments `RejectedCount`; a call carrying a **different** `ownerId` leaves
another account's row untouched; a call with a `null` batch id is a no-op and does not throw; a
recorder over a factory that blocks past `RecorderTimeoutSeconds` returns rather than waiting on it.

**The throwing-factory case is asserted against all three members, not just `OpenBatchAsync`.** It is
the one a value-returning signature invites you to skip, and it is the dangerous one: Phase 2 adds no
`try`/`catch` at any call site, and `AcceptAsync`'s own `catch` wraps only `Store.SaveAsync`
(the `catch` in `AcceptAsync`). An exception escaping `RecordAcceptAsync` therefore leaves the event
handler, faults the circuit and takes the untriaged batch with it — measurement costing a learner
their cards, the single outcome this design exists to prevent. Assert that each of the three
completes normally over a factory that throws, with `OpenBatchAsync` additionally answering `null`.

**File**: `TenExCards/TenExCards.Tests/TriageRecordShapeTests.cs`

**Intent**: Prove the record carries no text the application writes, in a form that cannot pass
vacuously.

**Contract**: Read the mapped property names of `TriageBatch` from the EF model and assert the set
**equals** exactly the seven expected names. The control, in the same test run and through the same
helper: the same read applied to `Card` returns its seven known names. If the helper is broken both
assertions fail, which is the point — see `## Critical Implementation Details`.

### Success Criteria

#### Automated Verification

- Solution builds: `dotnet build TenExCards/TenExCards.slnx`
- Suite is green: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
- Suite is green with the user-secrets store moved aside, the way CI sees it (see
  `TenExCards.Tests/AGENTS.md`, "A green local run is not a green CI run")
- `TriageRecordShapeTests` fails when an eighth property is added to `TriageBatch` — verified by
  adding one temporarily and reverting

#### Manual Verification

- The generated migration declares exactly one `nvarchar` column (`OwnerId`) and no other text column
- The migration applies cleanly to `sqldb-tenexcards-dev` before it goes anywhere near the app's
  database, per `TenExCards/AGENTS.md`
- `TriageBatches` exists in the dev database with the expected columns and the `OwnerId` index
- **Production startup log names `AddTriageBatches` as the one applied migration**, read from a fresh
  `Application started` line — this phase's push to `main` is the boot that applies it, and nothing
  writes to the table yet, which is the safest possible first contact for a forward-only boot-path
  migration. See `## Deploy cadence`.

**Implementation Note**: Pause here for manual confirmation before Phase 2.

---

## Phase 2: Wire the triage path

### Overview

Call the recorder from the one component that triages. No UI change, no new learner-visible
behaviour.

### Changes Required

#### 1. The triage component

**File**: `TenExCards/TenExCards/Components/Pages/Generate.razor.cs`

**Intent**: Open a batch once generation has succeeded, and record each accept and reject as it
happens.

**Contract**: Inject `ITriageRecorder` alongside `ICardStore`. Add a `private Guid? _batchId` field,
held on the component rather than on `TriageSession` — the session stays I/O-free and knows nothing
about persistence identity.

- In `SubmitAsync`, set `_batchId` from
  `OpenBatchAsync(_ownerId!, _session.BatchSize, CancellationToken.None)`. **Placement is
  load-bearing in both directions.** It sits after the `result.IsSuccess` check, so a failed
  generation opens no batch — and it sits *below* the `_passage = string.Empty; _focusHint =
  string.Empty;` pair, not above it. The comment on those two lines states the invariant it would
  otherwise break: "There is no moment in which both candidates and the text they came from exist."
  An `await` between the `TriageSession` construction and that clearing creates exactly such a
  moment, spanning a render and holding a passage of up to `MaxPassageCharacters` alongside its
  candidates in circuit memory — against `## Never do these`, "Never hold more in a circuit than you
  must." Either side of `_stage = Stage.Triaging` is fine; before the passage is cleared is not.
- In `AcceptAsync`, call `RecordAcceptAsync` **after** `Store.SaveAsync` has returned and
  `_session!.Accept(edited)` has run, before `AdvanceAsync()`. The ordering is load-bearing — see
  `## Critical Implementation Details`.
- In `RejectAsync`, call `RecordRejectAsync` after `_session!.Reject()`, before `AdvanceAsync()`.

Both calls sit inside the existing `_busy` guard's `try`/`finally`, so the re-entrancy protection
already covers them. No new `try`/`catch` at any call site: the recorder swallows.

#### 2. Registration test

**File**: `TenExCards/TenExCards.Tests/TriageRecorderTests.cs`

**Intent**: Catch a missing DI registration, which is the one Phase 2 failure mode a test can reach
without a browser.

**Contract**: Resolve `ITriageRecorder` from `TenExCardsWebApplicationFactory`'s service provider and
assert it is the concrete `TriageRecorder`. This boots the real `Program.cs` pipeline, so it fails if
the registration lands inside the `if (!e2e)` block.

### Success Criteria

#### Automated Verification

- Solution builds: `dotnet build TenExCards/TenExCards.slnx`
- Suite is green: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
- `ITriageRecorder` resolves from the real pipeline (the registration test above)
- No regression in `TriageSessionTests`, `CardOwnershipTests` or `CandidateEditTests`

#### Manual Verification

- Run locally in **Development** — `dotnet run --project TenExCards/TenExCards --launch-profile http`;
  a bare `--no-launch-profile` defaults to Production and the page renders unstyled
- Generate a batch, accept some, edit-then-accept one, reject some, and finish triage: one
  `TriageBatches` row in `sqldb-tenexcards-dev` where `AcceptedCount + RejectedCount = CandidateCount`
  and `EditedCount` matches the number reworded
- Abandon a batch mid-triage by closing the tab: the row's counters are below `CandidateCount`, which
  is what makes untriaged candidates countable
- A save failure during accept does not advance the session **and** does not increment the counter.
  **The procedure, because this invariant has no automated falsifier** — it lives inside an
  `InteractiveServer` component, and `TenExCards.Tests/AGENTS.md` forbids reaching for a
  component-rendering library to get at it. Generate a batch so the row opens, then break the
  connection *without restarting the app*: delete the `dev-machine-krzychu` firewall rule with
  `az sql server firewall-rule delete`, wait for the open pool connection to drop, and press "Keep
  this card". Expect all four at once — the save-error message renders, the same candidate is still
  on screen at the same position, no new `Cards` row exists, and `AcceptedCount` is unchanged.
  Restore the firewall rule afterwards and confirm a retry of that same accept now writes exactly
  one card and one increment

**Implementation Note**: Pause here for manual confirmation before Phase 3.

---

## Phase 3: Operator read path

### Overview

Ship the query and the runbook that make "the product owner can determine the rates" true, and prove
the query runs before trusting anything it returns.

### Changes Required

#### 1. The query

**File**: `scripts/outcome_rates.sql`

**Intent**: The three `FR-013` rates in one runnable file, pooled, with the per-learner form
alongside. This is the deliverable that makes `FR-013` satisfiable; the table alone does not.

**Contract**: Five labelled result sets — acceptance rate, the **settled** acceptance rate, AI-origin
share, edit rate, and the per-learner breakdown grouped by `OwnerId`. Each divisor wrapped in
`NULLIF(..., 0)`. Header comments state which PRD criterion each rate serves, its threshold, and
which source it reads. Three facts the file must state, because all three are otherwise read as
breakage:

```sql
-- Acceptance rate — PRD Primary, target >= 75%. Denominator is candidates SHOWN.
SELECT SUM(CAST(AcceptedCount AS float)) / NULLIF(SUM(CandidateCount), 0) AS AcceptanceRate,
       SUM(CandidateCount) AS CandidatesShown,
       SUM(AcceptedCount)  AS Accepted,
       SUM(RejectedCount)  AS Rejected,
       SUM(CandidateCount) - SUM(AcceptedCount) - SUM(RejectedCount) AS Untriaged
FROM   TriageBatches;

-- The same rate over settled batches only. A row has no closed state, so a batch being triaged
-- right now is indistinguishable from an abandoned one; SettledAfterMinutes (30) is the cutoff.
SELECT SUM(CAST(AcceptedCount AS float)) / NULLIF(SUM(CandidateCount), 0) AS SettledAcceptanceRate,
       COUNT(*) AS SettledBatches
FROM   TriageBatches
WHERE  OpenedAt < DATEADD(minute, -30, SYSUTCDATETIME());
```

1. On an empty table every rate is `NULL`, not `0` and not an error. A `NULL` here means "no batches
   yet", and reading it as a failed query is the exact hazard `lessons.md` records.
2. The rates **undercount under fault**, because the recorder swallows write failures by design.
3. **`Untriaged` pools three different things** and only the first is card-quality signal: candidates
   the learner never reached, a batch open at the moment of the query, and accepts whose record write
   was swallowed. The settled result set removes the second; nothing removes the third.

**Why the settled form is not optional.** Abandonment here is one click, not a rare tab-close:
`Components/Layout/NavMenu.razor` renders throughout triage, and its links are enhanced navigation,
which — per `TenExCards/AGENTS.md` — never fires `beforeunload`. The manual-entry link was confined
to `Composing`/`Failed` for exactly this reason and the nav menu was not, so an abandoned batch is a
routine event whose candidates all land in `Untriaged`. With the denominator set to candidates
*shown*, a handful of them drags the headline rate below the PRD's 75% and reads as a card-quality
failure. Reporting both forms is what lets the product owner tell those two apart.

#### 2. The runbook

**File**: `TenExCards/AGENTS.md`

**Intent**: Record how the rates are read and which source answers which question, so the split
between `Cards` and `TriageBatches` is not "simplified" later by someone reading one table as the
obvious home for all three.

**Contract**: A short section under `## Deployment` covering: the epoch — **the dated Phase 2 deploy,
captured at the time, not this one** (`## Deploy cadence`), with no backfill before it — the
two-source split and why, the `NULL`-on-empty and
undercount facts, **what `Untriaged` pools and why the settled form exists** (`#### 1`), and the
connection procedure. The procedure must obey two standing rules from
`## Never do these` — no connection string in a tracked file, and no secret as a command argument,
since PowerShell 5.1 appends every command line to `ConsoleHost_history.txt`:

```powershell
$env:SQLCMDPASSWORD = az keyvault secret show --vault-name kv-tenexcards-plc `
    --name sql-admin-password --query value -o tsv
sqlcmd -S sql-tenexcards-plc.database.windows.net -d sqldb-tenexcards -U tenexadmin `
    -i scripts/outcome_rates.sql
$env:SQLCMDPASSWORD = $null
```

Note the prerequisite: the `dev-machine-krzychu` firewall rule must be current, and it is deliberately
not in `infra/main.bicep` because it is a property of where you are sitting.

#### 3. Scripts index

**File**: `scripts/README.md`

**Intent**: The folder has an index; a third artifact belongs in it.

**Contract**: One entry naming the file, what it answers, and that it is read-only.

### Success Criteria

#### Automated Verification

- Solution builds and the suite stays green (no code changes in this phase, so this is a guard
  against an accidental edit): `dotnet build TenExCards/TenExCards.slnx` and
  `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`

#### Manual Verification

- `scripts/outcome_rates.sql` runs to completion against `sqldb-tenexcards-dev` and returns five
  result sets — proving the command runs before its output is ever read as a verdict
- Against the dev rows created in Phase 2, the acceptance rate matches a hand-count of the batch,
  and the origin share matches a hand-count of the `Cards` rows. A query that runs but returns the
  wrong number is the failure this step exists to catch
- Deleting one generated card from the dev database moves the origin share and leaves the edit rate
  unchanged — the two-source split behaving as designed
- No connection string or password appears in any tracked file: `git diff` reviewed before commit

**Implementation Note**: Pause here for manual confirmation before Phase 4.

---

## Phase 4: Deploy and verify live

### Overview

Ship it the normal way — a push to `main` — let the migration apply on the boot path, then read the
three rates off the live database and record them.

### Changes Required

#### 1. Deploy

**File**: none (CI)

**Intent**: `.github/workflows/deploy.yml` deploys every push to `main`. Do not hand-build and
hand-deploy a commit going to `main`.

**Contract**: Push; watch `Test` pass, then `Publish`, `Pack`, `Deploy` and `Verify`. The startup
migration applies `AddTriageBatches` on the boot path. If it throws, the container does not serve —
B1 has no deployment slots, and the rollback path is redeploying the retained previous archive, which
does **not** reverse schema.

#### 2. Record the measurement

**File**: `context/changes/outcome-recording/plan.md` (this file's `## Progress`) and
`TenExCards/AGENTS.md`

**Intent**: Close the slice on evidence rather than on a green pipeline. `lessons.md`: a verification
command's output is not a verdict until the command is proven to run.

**Contract**: Triage at least one real batch on the live site, run `scripts/outcome_rates.sql`
against `sqldb-tenexcards`, and record the three numbers with the date. Add the dated "recording is
live" paragraph to `TenExCards/AGENTS.md` in the style of the other slices.

#### 3. Roadmap

**File**: `context/foundation/roadmap.md`

**Intent**: Close `S-06`.

**Contract**: Flip the `## At a glance` row and the `### S-06` body `- **Status:**` to `done`, and
bump the frontmatter `updated:`. `M-01`'s "Done when" is every `F-NN` and `S-NN` `done`, so this is
the last one — note whether the milestone can close.

### Success Criteria

#### Automated Verification

- The `main` run is green through `Verify`, with `Test` passing before `Publish`
- `scripts/verify_deploy.py` passes as part of the run
- The startup log reports **no pending migrations** on a boot timestamped after the deploy.
  `AddTriageBatches` was applied by Phase 1's push; zero pending here is the correct reading and not
  a failed migration — see `## Deploy cadence`

#### Manual Verification

- A fresh `Application started` line timestamped after the deploy confirms the container actually
  restarted — never read a `200` as evidence of a restart (`lessons.md`)
- A real batch triaged on `https://tenexcards-ka.azurewebsites.net` produces exactly one
  `TriageBatches` row whose counts match what was done
- `scripts/outcome_rates.sql` returns the three rates against `sqldb-tenexcards`, and the numbers are
  recorded in this change with the date
- The live row contains no text beyond `OwnerId` — confirmed by reading the row, with a `Cards` row
  read in the same session as the control that the read itself works
- `TenExCards/AGENTS.md` and `context/foundation/roadmap.md` reflect the shipped state
- **No `file:line` citation was introduced in any edited document** — the Phase 3 runbook and the
  Phase 4 slice paragraph both cite property, member and section names instead. A line number in a
  durable record is silently invalidated by any edit above it, which is why `persistence-spine` and
  `accounts-and-sessions` each carried this same criterion

---

## Testing Strategy

### Unit Tests

- Recorder writes: candidate count, owner, zeroed counters, returned id
- Counter increments: accept, accept-with-edit, reject
- Account scoping: another account's `ownerId` cannot move a row — the invariant every persisting
  slice in this repo must assert for its own queries
- Best-effort: a throwing factory produces no exception and a `null` batch id
- Null batch id: record calls are no-ops
- Record shape: `TriageBatch`'s mapped property set **equals** the expected seven, with `Card`'s set
  as the same-run control

### Integration Tests

- `ITriageRecorder` resolves from the real `Program.cs` pipeline via `TenExCardsWebApplicationFactory`

### Manual Testing Steps

1. Run locally in Development, generate a batch, accept / edit-accept / reject through it, and check
   the dev row sums to the candidate count
2. Abandon a batch mid-triage; check the counters sit below the candidate count
3. Delete a generated card in the dev database; check the origin share moves and the edit rate does
   not
4. After deploy, triage a live batch and run the query against `sqldb-tenexcards`

## Performance Considerations

Two extra database round-trips per batch-open and one per triage action, on a path that already
writes a card per acceptance. At the PRD's stated scale (`qps: low`, `data_volume: small`) this is
negligible. The increment is read-modify-write rather than a single `UPDATE` statement, which costs
one extra round-trip per action and is accepted for the testability reason in
`## Critical Implementation Details`. The 2-second acknowledgement budget is untouched — the
batch-open write happens after generation has already returned — but it is not the only budget that
matters. Every recorder call is bounded at `RecorderTimeoutSeconds` against EF's 30-second retry
budget, because reject carries no I/O today and shows no progress while `_busy` holds. See
`## Critical Implementation Details`.

## Deploy cadence

**Each phase is pushed to `main` as it lands, and every push to `main` is a production deploy.** This
is stated rather than assumed because the plan's verification steps depend on it, and because the
repository holds both precedents — `manage-saved-cards` landed on a feature branch, while
`edit-before-accepting` pushed per phase and verified its migration in production at Phase 1. This
plan follows the second, so that the migration reaches Azure SQL in a boot where **nothing writes to
the new table**: a schema fault and a write fault then cannot be confused, on a tier with no
deployment slots.

Two consequences, both of which would otherwise be read as breakage:

- **Phase 1's push is the boot that applies `AddTriageBatches`.** By Phase 4 there are **zero**
  pending migrations, and that is the correct reading, not a failed migration — the exact
  false-verdict hazard `lessons.md` records under "Prove the check before trusting the result".
- **The acceptance rate's epoch is the Phase 2 deploy**, the first boot whose code writes rows — not
  Phase 4. It is a date to capture at the time, not one this plan can state in advance, and the
  runbook records the captured value.

## Migration Notes

`AddTriageBatches` is additive — one new table, no change to `Cards`, `AspNetUsers` or
`DataProtectionKeys` — so it cannot fail existing rows. It is still **forward-only**: migrations run
on the boot path and no down migration is authored or relied on. Apply it to `sqldb-tenexcards-dev`
first, then let Phase 1's own push apply it to `sqldb-tenexcards`, keeping the previous archive.

No backfill. The acceptance rate's epoch is the **Phase 2** deploy date — see `## Deploy cadence` —
captured at the time and recorded in the runbook.

## References

- PRD: `context/foundation/prd.md` — `FR-013`, `## Success Criteria`, `## Access Control`
- Roadmap: `context/foundation/roadmap.md` — `### S-06`
- Repository rules: `TenExCards/AGENTS.md`, `TenExCards/TenExCards.Tests/AGENTS.md`
- `context/foundation/lessons.md` — "Put the validation gate on the call, not on one route to it";
  "A negative check needs a control, or it cannot fail"; "Prove the check before trusting the
  result"; "Verify a restart from the log, never from the first 200"
- Pattern to follow: `TenExCards/TenExCards/Cards/CardStore.cs`, `Cards/ICardStore.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not
> rename step titles. See `references/progress-format.md`.

### Phase 1: Record the batch

#### Automated

- [x] 1.1 Solution builds — 8e0f65b
- [x] 1.2 Test suite is green — 8e0f65b
- [x] 1.3 Test suite is green with user-secrets moved aside (the way CI sees it) — 8e0f65b
- [x] 1.4 `TriageRecordShapeTests` fails on an added eighth property, verified and reverted — 8e0f65b

#### Manual

- [x] 1.5 Generated migration declares exactly one `nvarchar` column (`OwnerId`) — 8e0f65b
- [x] 1.6 Migration applies cleanly to `sqldb-tenexcards-dev` — 8e0f65b
- [x] 1.7 `TriageBatches` exists in the dev database with the expected columns and `OwnerId` index — 8e0f65b
- [x] 1.8 Production startup log names `AddTriageBatches` as the one applied migration, read from a
      fresh `Application started` line

### Phase 2: Wire the triage path

#### Automated

- [x] 2.1 Solution builds — 49cfb8f
- [x] 2.2 Test suite is green — 49cfb8f
- [x] 2.3 `ITriageRecorder` resolves from the real pipeline — 49cfb8f
- [x] 2.4 No regression in `TriageSessionTests`, `CardOwnershipTests`, `CandidateEditTests` — 49cfb8f

#### Manual

- [x] 2.5 App runs locally in Development via `--launch-profile http`, not `--no-launch-profile` — 49cfb8f
- [x] 2.6 Completed batch row sums to the candidate count, `EditedCount` matches
- [x] 2.7 Abandoned batch leaves counters below the candidate count
- [x] 2.8 A save failure during accept neither advances the session nor increments the counter, and a
      retry after the connection is restored writes exactly one card and one increment

### Phase 3: Operator read path

#### Automated

- [x] 3.1 Solution builds and the suite stays green — 16ce78b

#### Manual

- [x] 3.2 `scripts/outcome_rates.sql` runs to completion against `sqldb-tenexcards-dev`, five result sets — 16ce78b
- [x] 3.3 Rates match a hand-count of the dev rows — 16ce78b
- [x] 3.4 Deleting a generated dev card moves the origin share and leaves the edit rate unchanged — 16ce78b
- [x] 3.5 No connection string or password in any tracked file — 16ce78b

### Phase 4: Deploy and verify live

#### Automated

- [x] 4.1 `main` run green through `Verify`, with `Test` passing before `Publish`
- [x] 4.2 `scripts/verify_deploy.py` passes in the run
- [x] 4.3 Startup log reports no pending migrations (applied at Phase 1 — zero is correct here)

#### Manual

- [x] 4.4 Fresh `Application started` line timestamped after the deploy
- [x] 4.5 A live triaged batch produces exactly one matching `TriageBatches` row
- [x] 4.6 Three rates read from `sqldb-tenexcards` and recorded here with the date
- [x] 4.7 Live row carries no text beyond `OwnerId`, with a `Cards` read as the control
- [x] 4.8 `TenExCards/AGENTS.md` and `context/foundation/roadmap.md` updated
- [x] 4.9 No new `file:line` citation in any edited document — member and section names instead
