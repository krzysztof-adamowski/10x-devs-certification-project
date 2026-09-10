<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Persistence Spine (second pass)

- **Plan**: `context/changes/persistence-spine/plan.md`
- **Mode**: Deep
- **Date**: 2026-09-08
- **Verdict**: REVISE → SOUND after triage
- **Findings**: 2 critical, 5 warnings, 1 observation — all fixed
- **Prior pass**: `plan-review-pass1.md` (10 findings, triaged; preserved rather than overwritten)

This pass reviewed the plan *as revised by* the first pass's triage. It verifies that those fixes
landed and looks for what the first pass missed. Findings already settled in pass 1 — notably F6
(`/db-check` as an anonymous write endpoint, accepted as risk) and F3 (the firewall rule) — were not
re-raised.

## Verdicts

| Dimension | Verdict | After triage |
|-----------|---------|--------------|
| End-State Alignment | PASS | PASS |
| Lean Execution | PASS | PASS |
| Architectural Fitness | FAIL | PASS |
| Blind Spots | FAIL | PASS |
| Plan Completeness | WARNING | PASS |

Both FAILs were bounded. The approach — provision first, prove the Key Vault reference resolves
before any code depends on it, verify against a development database, deploy into an empty one — was
sound throughout; two mechanisms did not deliver what the plan claimed for them.

## Grounding

12/12 paths ✓ · Progress↔Phase 47/47 ✓ (48/48 after fixes) · phase names exact ✓ · no stray
checkboxes in body ✓ · brief↔plan ✓ · 1 symbol check **contradicted** the plan (no antiforgery form
exists anywhere in `TenExCards` — see F1).

Subagent verification confirmed: the Key Vault reference prescription (`infrastructure.md:323`), the
auto-pausing exclusion (`:286`), forward-only migrations (`:238`), the human-only approval bucket
(`:240-243`), Azure SQL S0 as one of two sanctioned tiers (`:286`, `:320-322`), the 0.152 s TTFB
floor (`baseline.md:28`) and its stated exclusion of persistence (`:7`), and the three archive shape
assertions (`deploy-plan.md:442-444`).

Checked and **not** raised: the `AddDbContextFactory` + scoped-context registration (pass 1's F4 fix
is correct — `PersistKeysToDbContext` does resolve from a scope, not the factory); the rollback
artifact (`TenExCards/bin/publish.zip`, 497,672 bytes, and F-01's `publish-scaffold-rollback.zip`
both exist on disk, so Phase 3 change 1 is executable).

## Findings

### F1 — The key-persistence check has no form to run against

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 3 change 5 / criteria 3.11, 3.10, 2.10 vs. Phase 2 verification page
- **Detail**: Criterion 3.11 — "a form rendered before the restart is still accepted when submitted
  after it" — is the plan's headline proof that Data Protection key persistence works, introduced by
  pass 1's F7 fix and explicitly handed to `S-01` as the check it inherits. There is no form in this
  application and Phase 2 did not build one: grep across `TenExCards` for `EditForm`, `<form` and
  `AntiforgeryToken` returns nothing, the only hit being `app.UseAntiforgery()` at `Program.cs:24`.
  `/db-check` was specified as `@rendermode InteractiveServer` with "a control that writes a
  `SpineProbe` row"; an interactive button dispatches over the circuit, issues no antiforgery token
  and performs no form post, so there was nothing to render before the restart and nothing to submit
  after it. Criterion 2.10 was worse than unexecutable — interactive component descriptors are signed
  with the same key ring, so it could have passed without a token ever being minted.
- **Fix A ⭐ Recommended**: Make `/db-check` statically rendered with an
  `<EditForm method="post" @formname="write-probe">`; drop "without a page reload" from criterion 3.9.
  - Strength: Blazor SSR emits the antiforgery token automatically, so one page proves write, read
    and key survival with no new surface. Circuit liveness is already proven by `CircuitCheck.razor`,
    which lives until `S-01` — 3.9 was re-proving F-01's result.
  - Tradeoff: `/db-check` no longer demonstrates interactivity; a page reload per probe write.
  - Confidence: HIGH — the absence of any form is verified, and SSR `EditForm` antiforgery behaviour
    is framework default.
  - Blind spot: The antiforgery *cookie* must also survive, but that is browser-side and unaffected
    by the key ring rotating.
- **Fix B**: Keep `/db-check` interactive; add a second static-rendered page carrying a trivial form.
  - Strength: Criterion 3.9 survives intact; the two proofs stay independent.
  - Tradeoff: A third throwaway surface for `S-01` to delete.
  - Confidence: HIGH.
- **Decision**: FIXED via Fix A. Phase 2 change 7 now specifies static rendering with an `EditForm`
  and states why in the contract; Phase 3 change 5's check 2 names `/db-check` as its subject;
  criteria 2.10, 3.9 and 3.11 were reworded, with their Progress entries.

### F2 — The dev/prod database split has no credential boundary

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Architectural Fitness
- **Location**: Phase 1 change 3 × Phase 2 local configuration; Phase 4 edit (e)
- **Detail**: The direct successor to pass 1's F2. The template declares one logical server, one
  `@secure()` admin password, and two databases — and nothing created a second login or contained
  user. The development connection string therefore differed from the live one by `Initial Catalog`
  alone and carried server-wide administrative rights, making the split a default rather than a
  boundary. The plan and brief claimed more: "nothing a developer runs can reach the schema the live
  site serves from", and Phase 4 edit (e) would have written that framing into `AGENTS.md` as a
  standing rule. Second consequence: the development machine would hold the credential for the drop,
  scale and restore operations `infrastructure.md:240-243` places in the human-only approval bucket —
  the very bucket the plan cites as reasoning for the split.
- **Fix A ⭐ Recommended**: A contained database user inside the development database only
  (`db_owner` there, no server-level login), used by user-secrets; the admin password never leaves
  the vault.
  - Strength: Contained users authenticate against their own database and cannot address another on
    the same server — this converts the claim the plan already makes into a mechanism. Dialect
    divergence stays zero.
  - Tradeoff: One manual T-SQL step living in no template, to be recreated after a teardown.
  - Confidence: HIGH on the isolation property; MEDIUM that `db_owner` on a contained user suffices
    for EF migrations — to be confirmed in Phase 2 before relying on it.
  - Blind spot: The declined passwordless/Entra option would have removed this class entirely.
- **Fix B**: Keep the admin credential; downgrade the claim in brief, plan and `AGENTS.md` edit (e).
  - Strength: The plan stops asserting a guarantee it does not deliver, which is the actual defect.
  - Tradeoff: `S-01` inherits a development machine holding admin rights over the database that will
    hold real accounts.
  - Confidence: HIGH.
- **Decision**: FIXED via Fix A. New Phase 1 change 8 ("Development database login") creates the
  contained user and states the `db_owner` caveat; Phase 2 change 4 points user-secrets at it; a new
  *Critical Implementation Details* paragraph names why two databases alone are not a boundary; new
  manual criterion 1.15 checks that the development user is **refused** by the app database; the
  brief's decision table, scope and end-state wording were updated to match.

### F3 — The Phase 1 gate can false-negative, and the failure caches

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 1 change 6 / criterion 1.6
- **Detail**: The plan warned about RBAC propagation delay in exactly one place — granting *yourself*
  `Key Vault Secrets Officer`. The `Key Vault Secrets User` assignment to the *site's* managed
  identity (change 3, in the template) has the same eventual consistency, and App Service resolves
  Key Vault references at app start and caches the outcome. A reference set before that assignment
  propagates reports unresolved and does not re-heal on the timescale the check runs at. Criterion
  1.6 is the declared gate for the entire change, carrying an explicit "do not begin Phase 2", so a
  benign false negative either stalls the change or sends the implementer into a correct template.
- **Fix**: After setting the app setting, `az webapp restart` and re-check; treat the first
  non-`Resolved` reading as expected and escalate only after a restart.
- **Decision**: FIXED. Phase 1 change 6 gained a "Then `az webapp restart` and read the reference
  again" paragraph covering the site identity's propagation and App Service's caching.

### F4 — Phase 4 leaves two records this change falsifies

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 4 changes 3 and 4
- **Detail**: Same class as pass 1's F9, two documents further on. (a) `infrastructure.md:324-327`
  assigns Data Protection key persistence to "the same change that adds Identity" — i.e. `S-01`. This
  change moves it to `F-02`; Phase 4 change 3 edited only Getting Started item 2 and the risk
  register, so the item stayed wrong even though `AGENTS.md` edit (b) corrects its twin.
  (b) `roadmap.md:146-147`, the `F-02` Risk bullet, says the change "Deliberately designs no schema";
  it now creates two tables.
- **Fix**: Add both edits and make criteria 4.4 and 4.5 countable, the way 4.2 already is for
  `AGENTS.md`.
- **Decision**: FIXED. Phase 4 change 3 restructured into three enumerated edits including the
  Data Protection item; change 4 gained the `F-02` Risk bullet with an instruction to restate rather
  than delete it (no *domain* schema is still true); criteria 4.4 and 4.5 and their Progress entries
  extended.

### F5 — AGENTS.md gains no rule about the boot-path migration

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 4 change 1, edits (a)–(f)
- **Detail**: The six enumerated `AGENTS.md` edits covered the persistence choice, DP keys, secrets in
  tracked files, vault-vs-values, the two databases, and testing. None recorded that
  `Database.Migrate()` now runs on the boot path, that migrations are forward-only, or that a bad one
  means the app does not serve on a tier with no slot rollback. This is the first change to put an
  app-killing mechanism on the boot path, and `## Never do these` is where every trap of comparable
  weight already lives — the archive assertions, the forwarded-headers variable, `--mode Complete`.
  Related: `infrastructure.md:238` allows "forward-only **or paired with a tested down migration**"
  while the plan takes the first branch flatly, so the two records would disagree.
- **Fix**: A seventh `AGENTS.md` edit under `## Never do these`, reconciled against
  `infrastructure.md:238` in the same phase.
- **Decision**: FIXED. Edit (g) added and the count raised to seven in the contract, criterion 4.2 and
  its Progress entry; Phase 4 change 3 gained a "Reconcile the forward-only wording while here"
  paragraph so one wording wins across both files.

### F6 — Phase 2's user-secrets step is ordered after what needs it

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 2, change 8 vs. changes 4–5 (old numbering)
- **Detail**: Local configuration was listed last, but the migration step scaffolds `InitialSpine` by
  building the application host to obtain the `DbContext`, and criteria 2.4 and 2.5 apply and inspect
  that migration — all reading `GetConnectionString("DefaultConnection")`, which is null until
  user-secrets is set. An implementer following the numbering hits this.
- **Fix**: Move local configuration ahead of service registration, renumbering the rest, and state
  the dependency.
- **Decision**: FIXED. Phase 2 changes reordered to 1 packages · 2 context · 3 probe entity ·
  4 local configuration · 5 service registration · 6 initial migration · 7 verification page ·
  8 navigation. The cross-reference in change 5 was updated from "(change 6)" to "(change 7)", and
  change 4 carries an "Ordered ahead of the code that needs it" paragraph.
  (Confidence: HIGH that 2.4/2.5 depend on it; MEDIUM on whether `dotnet ef migrations add` itself
  fails, since recent EF tolerates a null connection string at design time. Reordering costs nothing
  either way.)

### F7 — A prerequisite five criteria rest on was never verified

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Prerequisites (brief); criteria 1.12, 1.15, 2.5, 3.8, manual step 2
- **Detail**: "A SQL client capable of connecting to Azure SQL" was listed and nothing else. The plan
  verified and recorded every other tool state during planning — `docker: command not found`,
  `dotnet tool list --global` empty, three providers `NotRegistered` — which is what makes this
  omission conspicuous rather than harmless. `sqlcmd` ships with neither the .NET SDK nor Windows.
- **Fix**: Name the client and confirm it before Phase 1 ends. The Azure Portal Query Editor needs no
  install and reaches the server through the same "allow Azure services" rule the template declares;
  `winget install sqlcmd` is the alternative.
- **Decision**: FIXED. Phase 1 change 2 gained an "Also confirm a SQL client exists before Phase 1
  ends" paragraph naming the dependent criteria; the brief's Prerequisites line was updated.

### F8 — Two strings an implementer would copy verbatim are wrong

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: criterion 1.10; Phase 4 change 2
- **Detail**: (a) Criterion 1.10's PSReadLine path had lost its backslashes in the source
  (`$env:APPDATAMicrosoftWindows…`). Copy-pasted it resolves to nothing and the secret-leak check
  passes silently — the one failure mode that check exists to catch. The same path is written
  correctly in the change-6 prose. (b) Phase 4 change 2 said "an updated teardown section", but
  `deploy-plan.md` has two: `## Teardown` at `:231`, superseded and deliberately kept unedited as a
  record of what was run, and `## Teardown (supersedes …)` at `:382`.
- **Fix**: Restore the backslashes; name the line-382 section explicitly.
- **Decision**: FIXED. Path restored in criterion 1.10; Phase 4 change 2 now names the current
  teardown section, says the earlier block stays unedited, and adds the contained user and the
  development-machine firewall rule as data-plane objects to recreate after a teardown.

## Triage summary

- **Fixed**: F1 (Fix A), F2 (Fix A), F3, F4, F5, F6, F7, F8 — 8
- **Skipped / accepted / dismissed**: none

**Verdict after fixes: SOUND.** Progress↔Phase re-verified after all edits: 48 success criteria map
1:1 to 48 Progress entries across four phases, numbering contiguous within each phase, phase names
exact, no checkbox bullets outside the Progress block.
