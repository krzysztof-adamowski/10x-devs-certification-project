<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Persistence Spine (F-02)

- **Plan**: `context/changes/persistence-spine/plan.md`
- **Mode**: Deep
- **Date**: 2026-09-08
- **Verdict**: REVISE → SOUND after triage
- **Findings**: 2 critical, 6 warnings, 2 observations

## Verdicts

| Dimension | Verdict | After triage |
|-----------|---------|--------------|
| End-State Alignment | FAIL | PASS |
| Lean Execution | PASS | PASS |
| Architectural Fitness | WARNING | PASS |
| Blind Spots | WARNING | PASS (one accepted risk) |
| Plan Completeness | WARNING | PASS |

## Grounding

12/12 paths ✓ (`TenExCards/Data/` new, as planned) · 6/6 symbols ✓ (`UseAntiforgery`, `UseHsts`,
`httpsOnly`, `clientAffinityEnabled`, the `appSettings` omission, no `UseHttpsRedirection`) ·
brief↔plan ✓ · Progress↔Phase 47/47 criteria mapped ✓ · F-02 outcome quoted verbatim from
`roadmap.md` ✓ · `publish.zip` (497,672 B) present on disk and matching the recorded live artifact ✓

Checked and deliberately **not** raised as findings: the S0 tier cost (no numeric ceiling exists to
breach — a $200/30-day credit against a $13.15/mo floor); the uncommitted pack script (explicitly
accepted as risk and assigned to `F-03`); the test-project deferral (the roadmap independently
assigns `TenExCards.Tests` to `S-01`, so the `AGENTS.md` edit aligns a record rather than editing a
rule to excuse the change); and the three archive assertions (correctly stated, and they avoid the
older step-6 variant that `deploy-plan.md` documents as wrong).

## Findings

### F1 — Phase 3 cannot exercise the startup migration it claims to verify

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: End-State Alignment
- **Location**: Phase 3 Overview vs. Phase 2 criteria 2.3–2.4
- **Detail**: Phase 2 applied `InitialSpine` to the Azure database from the development machine, so
  the Phase 3 deploy would find `__EFMigrationsHistory` already current and `Database.Migrate()`
  would be a no-op. The risk the whole plan is architected around ("a failed startup migration means
  no app", on a tier with no slot rollback) was never exercised on the deploy path, and would first
  fire in `S-01` on a database holding accounts. Criterion 3.6 asserts only the absence of an
  exception, which a no-op also satisfies.
- **Fix A ⭐ Recommended**: Add a second, deliberately-unapplied migration to Phase 3.
  - Strength: Proves the boot path while the database is still empty and disposable.
  - Tradeoff: One extra deploy cycle; a throwaway column carried until `S-01`.
  - Confidence: HIGH — the no-op follows directly from the plan's own ordering.
  - Blind spot: Tests a succeeding migration, not a failing one.
- **Fix B**: Keep the Azure database empty until Phase 3 deploys.
  - Strength: The first deploy performs the real first migration; no extra artifact.
  - Tradeoff: Contradicts the local-dev-against-Azure decision and weakens Phase 2.
  - Confidence: MEDIUM.
- **Decision**: FIXED via Fix A — then **superseded by F2's fix**. With a separate development
  database, Phase 2 leaves the app's database empty, so the *first* deploy performs `InitialSpine`
  on the boot path for real. Change 6 was rewritten as a boot-path migration *proof* against the
  first deploy rather than a throwaway second migration, and it records that the proof is lost if a
  migration is ever applied to the app database ahead of a deploy.

### F2 — Local development and production share one database, unstated

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Architectural Fitness
- **Location**: Phase 2 change 8 × change 4
- **Detail**: Local development pointed at the Azure database via user-secrets. With
  `Database.Migrate()` on the boot path, every local `dotnet run` would apply whatever migrations sit
  in the working tree to the database the live site serves from — irreversibly, since migrations are
  forward-only and the rollback path (the deployment archive) does not reverse schema. A grep across
  all 791 lines never named the coupling, so it was never mitigated, and `S-01` would inherit it with
  real accounts. Second consequence: the EF key repository stores every key row in one table with no
  per-application partition, so the development machine would hold the production Data Protection key
  ring and, once `S-01` lands, could mint and read production auth cookies. `infrastructure.md`
  separately places any operation against the database server in the human-only approval bucket; a
  local `dotnet run` routed around that.
- **Fix A ⭐ Recommended**: A second database on the same SQL server for local development.
  - Strength: Removes the class of problem; keeps dialect divergence at zero; costs one more database
    inside credit that expires worthless — the tradeoff `AGENTS.md`'s budget rule already resolves.
  - Tradeoff: Two databases to migrate and tear down; they can drift if migrations are applied unevenly.
  - Confidence: HIGH — cost reasoning quoted from the repo's own posture.
  - Blind spot: Whether per-database vs. pool pricing changes the delta on this subscription.
- **Fix B**: Keep one database and add guard rails.
  - Strength: No new resource; smallest edit.
  - Tradeoff: Conventions, not mechanisms; gating `Migrate()` to non-Development would break criterion
    2.3 and make F1 worse.
  - Confidence: LOW.
- **Decision**: FIXED via Fix A. Phase 1 change 3 now declares two databases; Phase 2 change 8 points
  user-secrets at the development database; a new *Critical Implementation Details* paragraph names
  the coupling and both consequences; Phase 4 edit (e) records which database is which and why.

### F3 — "Allow Azure services" firewall rule admits every Azure tenant

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 1 change 3; "What We're NOT Doing"
- **Detail**: The rule labelled "for Azure services" is a `0.0.0.0`–`0.0.0.0` entry, which admits
  traffic originating anywhere in Azure — any subscription, any tenant. The plan's line "reachable
  over public networking, restricted by firewall rules" described a network boundary that, for
  Azure-originating traffic, does not exist; the boundary is the SQL admin password. What the rule
  grants is reachability to the login endpoint, not access.
- **Fix A**: Replace with explicit `possibleOutboundIpAddresses` firewall rules.
  - Strength: Narrows the surface to this app's egress.
  - Tradeoff: App Service outbound IPs are shared across a scale unit, so this narrows "all of Azure"
    only to "every app on this scale unit", while adding a rule set that silently breaks the app when
    the IP set rotates on a tier or scale operation.
  - Confidence: MED.
- **Fix B ⭐ Recommended**: Correct the wording and record the exposure.
  - Strength: Carries nearly all the value; no operational fragility.
  - Tradeoff: Hands a broad boundary to `S-01`.
  - Confidence: HIGH.
- **Decision**: FIXED via Fix B. *Reviewer note*: Fix A was initially ranked first; that under-stated
  the scale-unit sharing that limits its value, and the ranking was corrected during triage before the
  decision was taken. The scope bullet now states the `0.0.0.0` semantics and that the password is the
  boundary, and records why outbound-IP pinning was declined; Phase 4 change 3 now adds a fourth risk
  register row for it.

### F4 — PersistKeysToDbContext and IDbContextFactory are incompatible as hedged

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 2 change 4 × change 6
- **Detail**: Change 4 hedged ("or a context factory") and change 6 pointed at `IDbContextFactory`,
  which Blazor Server guidance also favours. But `AddDbContextFactory<AppDbContext>` does not register
  `AppDbContext` itself, and `EntityFrameworkCoreXmlRepository<TContext>` resolves `TContext` from a
  service scope with `GetRequiredService`. A factory-only registration compiles, starts, and throws
  the first time anything protects data — with `UseAntiforgery()` in the pipeline, the first rendered
  form. Criterion 2.9 would catch it locally, so the cost is debugging time, not a live outage.
- **Fix**: Drop the hedge; require both registrations with the reason stated.
- **Decision**: FIXED. Change 4 now requires `AddDbContextFactory<AppDbContext>` *and* a scoped
  `AppDbContext`, states that this is not stylistic, and change 6 names the factory unambiguously.

### F5 — Secrets reach the command line, where `git grep` cannot see them

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 1 change 6; criteria 1.9 and 2.5
- **Detail**: The secret-hygiene criteria check tracked files only, but the admin password reaches
  `az deployment group create -p ...` and the connection string reaches `az keyvault secret set
  --value "..."`. Under Windows PowerShell 5.1, PSReadLine appends every interactive command line
  verbatim to `ConsoleHost_history.txt` and keeps it indefinitely; the CLI warns against `--value`
  for this reason.
- **Fix**: Pass secrets through files or a prompt, and add a history-grep criterion.
- **Decision**: FIXED. Change 6 gained a "never pass a secret as an argument value" paragraph; new
  criterion 1.10 greps the PSReadLine history and checks no scratch secret file remains. The
  paragraph also notes that the Key Vault *reference* is a pointer, not a secret, and is safe inline.

### F6 — /db-check is an anonymous, unbounded write endpoint on the public internet

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 2 change 6; change 7 (nav entry)
- **Detail**: Identity does not exist until `S-01`, so the page that writes a row per click is
  reachable by anyone who finds it, and change 7 adds a nav link. `CircuitCheck.razor` set the
  throwaway-proof-surface precedent, but it holds a counter in a circuit; this one persists to a
  10-DTU database. Compounds with F3: no network boundary for Azure-originating traffic, plus an
  unauthenticated write path from the web.
- **Fix**: Cap the probe table (refuse writes past N rows, or delete-oldest) and record the exposure.
- **Decision**: ACCEPTED AS RISK — the probe table is very short-lived and `S-01` drops it along with
  the page and its nav entry. No plan edit.

### F7 — The restart-survival check can pass without proving what it claims

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 3 change 5; criterion 3.10
- **Detail**: "No new Data Protection key row after restart" is also what you see when the app read an
  existing valid key and never needed to write one. It proves the key ring can be read, not that
  persistence does its job. The property key persistence exists for — a token issued before a restart
  still being accepted after it — was never checked, and Phase 4 edit (b) tells `S-01` to "verify
  rather than implement" key persistence, so `S-01` would inherit a check that was never a real test.
- **Fix**: Add the pre-restart-render / post-restart-submit antiforgery check.
- **Decision**: FIXED. Change 5 now specifies two checks, names check 2 as "the check that can
  actually fail", and states that `S-01` inherits check 2 rather than check 1. New criterion 3.11.

### F8 — Phase 4 tells the implementer to copy a citation style Phase 4 forbids

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 4 change 4 vs. criterion 4.9
- **Detail**: Change 4 said to follow the format of the resolved Question 4, whose resolution includes
  a `file:NN` citation (`infra/main.bicep:96`). Criterion 4.9 forbids introducing exactly that.
  Following the instruction failed the criterion.
- **Fix**: Keep Question 4's structure, drop its citation style.
- **Decision**: FIXED. Change 4 now enumerates the structure to copy and states explicitly that the
  citation style is not part of it.

### F9 — Phase 4 misses a fifth record this change falsifies

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 4; criterion 4.8
- **Detail**: The roadmap says of `S-01`: "This is also the first slice to touch persistence, so the
  test project is created here." `F-02` touches persistence first, so that sentence stops being true —
  and it is the sentence the `AGENTS.md` edit (f) is aligned against. Phase 4 edited only Open
  Question 1 in `roadmap.md`, leaving criterion 4.8 ("all four records agree") false.
- **Fix**: Reword the `S-01` entry to "the first slice to persist account-scoped data".
- **Decision**: FIXED. Added to Phase 4 change 4 as a second edit in the same file; criterion 4.4
  extended to cover it.

### F10 — Version pinning is instruction-only, and 10.0.12 is unevidenced here

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Current State Analysis; Phase 2 change 1
- **Detail**: The plan asserted the four packages are "at 10.0.12, verified available" and required
  `dotnet-ef` as a global tool at that version. This machine has SDK 10.0.400 with runtime 10.0.11,
  `dotnet tool list --global` is empty, and the only patch level evidenced anywhere in the repository
  is 10.0.11. The plan's reasoning about why a version ahead of the shared framework is safe is
  correct; it is the pin itself that was unverified, and it would fail loudly rather than silently.
- **Fix**: Local tool manifest, and re-confirm the package version at the start of Phase 2.
- **Decision**: FIXED. Change 1 now requires a `.config/dotnet-tools.json` manifest instead of a
  global install and says to re-confirm the version before pinning; the Current State Analysis bullet
  is marked as a planning-time reading. New criterion 2.2 checks `dotnet tool restore`.

## Triage summary

| Outcome | Findings |
|---------|----------|
| Fixed | F1 (Fix A, superseded by F2), F2 (Fix A), F3 (Fix B), F4, F5, F7, F8, F9, F10 |
| Accepted as risk | F6 |
| Skipped | — |
| Dismissed | — |

**Verdict after fixes: SOUND.** Progress↔Phase re-verified after all edits: 47 success criteria map
1:1 to 47 Progress items across four phases, one `## Progress` heading, no stray checkboxes in phase
bodies.
