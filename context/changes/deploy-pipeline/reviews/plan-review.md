<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Merges Deploy Themselves

- **Plan**: `context/changes/deploy-pipeline/plan.md`
- **Mode**: Deep
- **Date**: 2026-09-08
- **Verdict**: REVISE → **SOUND after fixes**
- **Findings**: 2 critical, 6 warnings, 2 observations (9 fixed, 1 accepted as risk)

## Verdicts

| Dimension | Verdict (at review) | After triage |
|-----------|--------------------|--------------|
| End-State Alignment | WARNING | PASS |
| Lean Execution | WARNING | PASS |
| Architectural Fitness | WARNING | PASS |
| Blind Spots | FAIL | WARNING (F3 accepted) |
| Plan Completeness | FAIL | PASS |

## Grounding

7/7 claimed paths exist (`scripts/`, `.github/` correctly absent); 6/6 symbols verified; brief↔plan
consistent. Toolchain re-verified on the development machine: Python 3.10.11 present, `zip` absent
and `unzip` present (confirming the plan's central packer-language argument), `gh` 2.98.0,
`dotnet` 10.0.400, remote `krzysztof-adamowski/10x-devs-certification-project` matching the OIDC
subject string character-for-character, `.gitignore:12` = `[Bb]in/`.

Two plan claims were checked and came back **clean**, and should not be re-litigated:

- The `/no-such-path` negative test is valid. Unmatched routes return **404** — confirmed both from
  `Program.cs:18` (`UseStatusCodePagesWithReExecute`, which preserves the original status) and live
  against the running site. The 404 does carry a full HTML body, so the status check is what does the
  work, not the absence of assets.
- The three-vs-four assertion mismatch with `TenExCards/AGENTS.md` is already handled explicitly in
  the plan (`Phase 2 §1` and `Phase 5 §3`).

## Findings

### F1 — Rollback drill selects the deliberately-failed run

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 5 §1 vs Phase 4 negative test
- **Detail**: Phase 4's negative test left run history as `[0] revert=success, [1] broken=FAILURE,
  [2] p4=success`. Phase 5 selected `$PREVIOUS` from index `[1]` with no status filter — a failed run
  that uploaded no artifact, because `pack.py` never ran. The drill would fail at artifact download
  and present as a broken rollback path rather than a bad selection.
- **Fix A ⭐ Recommended**: Filter run selection with `--status success` (verified available on gh 2.98.0).
- **Fix B**: Move the broken-build test to the end of Phase 5.
- **Decision**: FIXED via Fix A — subsequently **superseded by F9**, which removed the drill entirely.
  The `--status success` filter survives in the Phase 5 cold-restore rehearsal, where the same
  failed-run hazard applies to picking a rollback candidate.

### F2 — Phase 4/5 verification commands don't execute as written

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Detail**: Three verified defects, all in blocks the plan declares are Windows PowerShell 5.1:
  (a) `grep -n`/`grep -rn` — PowerShell 5.1 has no `grep`, and the plan already uses `Select-String`
  correctly elsewhere; (b) `gh run list … -o json` — gh 2.98.0 has no `-o` flag, an `az` idiom that
  leaked in; (c) `gh run watch` called bare in five places, when usage is `gh run watch <run-id>`
  with a required positional — which also collides with the attach-to-wrong-run race the plan itself
  documents.
- **Fix**: `Select-String -Path`; drop `-o json`; resolve run ids explicitly via a
  `Wait-RunForCommit` helper keyed on the triggering commit, removing the race structurally rather
  than sleeping through it.
- **Decision**: FIXED

### F3 — pack.py's assertions miss the static-asset manifest

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 2 §1
- **Detail**: All six root-page assets are referenced through `@Assets[...]` (`App.razor:9-11,20`,
  `ReconnectModal.razor:1`), emitting fingerprinted URLs resolved at runtime from
  `TenExCards.staticwebassets.endpoints.json` — which lives at the **archive root**, not in
  `wwwroot/`. Verified live: `GET /app.khy4lop6wu.css` → 200 while the only physical file is
  `wwwroot/app.css`; the manifest is present in the current publish output. An archive containing
  `wwwroot/` but missing that manifest passes all four assertions and then 404s every stylesheet and
  script — the exact failure class this change exists to eliminate, and the one shape the pre-deploy
  gate does not check. `verify_deploy.py` would catch it, but only after the bad build is live.
- **Fix**: Add a fifth assertion for the manifest at archive root.
- **Decision**: ACCEPTED AS RISK — the post-deploy verifier is the safety net; the gate keeps four
  assertions. Worth revisiting if a deploy ever 404s its assets despite a green `pack.py`.

### F4 — Criterion 4.1 needs PyYAML, which is not installed

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Detail**: `import yaml` raises `ModuleNotFoundError` on the development machine's Python 3.10.11,
  and the check contradicts the plan's own standard-library-only stance.
- **Fix**: Replaced with `gh workflow view deploy.yml` — a workflow that fails to parse never
  registers, which is a stronger check than `yaml.safe_load` (that would accept valid YAML which is
  not a valid workflow).
- **Decision**: FIXED

### F5 — Negative test 1 names the wrong assertion

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Detail**: A `publish/`-nested tree violates assertion 1 (dll at root) *and* assertion 2. A
  fail-fast script names assertion 1, so the criterion demanding "assertion 2" reads as failed on
  correct behaviour.
- **Fix**: `pack.py` evaluates and prints all four assertions before exiting; criterion relaxed to
  "assertions 1 and 2". Also serves criterion 2.9 ("failure output names what to fix").
- **Decision**: FIXED

### F6 — The rollback drill has nothing observable to observe

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: End-State Alignment
- **Detail**: The two candidate runs built byte-identical output — a revert restores identical
  source — so "confirmed by observing the difference, not by trusting the run's colour" was
  unsatisfiable. The same gap hit criterion 4.8 and the Manual Testing step.
- **Fix**: Land a build marker on `/` (short commit SHA in the footer) before the drill.
- **Decision**: FIXED — and the marker was **retained** when F9 removed the drill, because it
  independently makes "which build is live" answerable for 4.8 and manual testing.

### F7 — Phase 2's scripts land under a Phase 4 commit

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architectural Fitness
- **Detail**: Phase 2 created `scripts/` but specified no commit; Phase 4 ran `git add .github
  scripts` under a `(p4)` message, misattributing Phase 2's deliverables and breaking the
  `TenExCards/AGENTS.md` rule that `(p<N>)` traces to the authorising phase.
- **Fix**: Explicit `(p2)` commit at the end of Phase 2; Phase 4 narrowed to `git add .github`.
- **Decision**: FIXED

### F8 — Two superseded documents outside the plan's blast radius

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Detail**: A repo-wide sweep found two live references Phase 5 did not cover: `infra/main.bicep:19`
  (header comment carrying `az webapp deploy … --track-status true`, inside the file CLAUDE.md names
  the infrastructure source of truth) and `context/deployment/deploy-plan.md` `## Rollback` (still
  presenting the git-ignored zip as *the* rollback path).
- **Fix**: Added as Phase 5 §5 and §6, with the `main.bicep` edit scoped explicitly to the comment
  block and the `## What We're NOT Doing` line adjusted so the no-touch rule stays intact.
- **Not proposed**: `Personal/azure-deployment-notes-for-personal-study.md` also carries a
  `Compress-Archive` recipe — personal study notes, deliberately outside this change.
- **Decision**: FIXED

### F9 — Dispatch-redeploy exceeds the roadmap's stated outcome

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Lean Execution
- **Detail**: `roadmap.md:154-155` states F-03's outcome as "builds, packages, and deploys the app,
  and the deployable artifact is retained." Redeploy-by-dispatch appears only under *Unlocks*, as
  rationale. It was also the most expensive part of the change — driving `actions: read`, a
  conditional download-artifact branch, a `run_id` input, and the whole production-mutating drill,
  and it was the root of F1 and F6.
- **Fix A**: Keep it (B1 has no slots; the drill is safer now than later).
- **Fix B ⭐ chosen**: Ship retention now, defer dispatch-redeploy.
- **Decision**: FIXED via Fix B. Removed the `run_id` input, `actions: read`, the conditional
  redeploy branch and the drill; `permissions` narrowed to `id-token: write` + `contents: read`.
  Phase 5 now does a **cold-restore rehearsal** — download the previous successful run's artifact and
  assert its shape locally, proving the rollback candidate is real and retrievable without mutating
  production. The emergency restore procedure is written into `deploy-plan.md` as literal commands,
  since an undocumented manual path would re-create the very failure this change removes.
  `plan-brief.md` was realigned to match.

### F10 — The publish-profile contingency has no success criteria

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 3 §1 contingency
- **Detail**: The fallback was well specified as a trigger and an action but never carried forward.
  If it fired, Phase 3's criteria 3.1, 3.2, 3.3, 3.5 and 3.6 would assert OIDC artifacts that cannot
  exist (unanswerable either way), and Phase 4's step list, header-comment contract and
  `--track-status false` decision were written only for the `az` CLI path. The plan's defence — "the
  same shape as the region fallback" — doesn't hold, because that one was recorded *after* the fact
  whereas this must be executable in advance.
- **Fix**: Stated the alternative criteria inline (mark the five `n/a (publish-profile path)`; assert
  the publish-profile secret present, the three OIDC secrets absent, temp file deleted, no app
  registration created) and the alternative Phase 4 step list (`azure/webapps-deploy` with
  `publish-profile`; `permissions` drops `id-token: write`; `--track-status false` has no equivalent
  because the hang was an `az` CLI behaviour, so `timeout-minutes` matters more).
- **Decision**: FIXED

## Triage Summary

| Outcome | Findings |
|---|---|
| Fixed | F1 (A, later superseded), F2, F4, F5, F6, F7, F8, F9 (B), F10 |
| Accepted as risk | F3 |

**Verdict after fixes: SOUND.** Both criticals were mechanical and are resolved. The one open item
is F3, accepted deliberately with the post-deploy verifier as the compensating control.
