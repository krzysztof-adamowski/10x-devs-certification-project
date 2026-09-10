# Merges Deploy Themselves — Plan Brief

> Full plan: `context/changes/deploy-pipeline/plan.md`
> Roadmap item: `context/foundation/roadmap.md`, `### F-03`

## What & Why

Every deploy today is a hand-assembled zip pushed with a typed CLI command, and the recorded failure
mode is that a *wrong* archive deploys **successfully** and then serves a page whose every asset
404s. There is no signal to catch it. This change makes a push to the default branch build, pack,
deploy, and verify itself — and makes the archive-shape rules that currently live as prose in
`TenExCards/AGENTS.md` into a script that exits non-zero.

## Starting Point

`https://tenexcards-ka.azurewebsites.net` is live on B1 Linux in `polandcentral`, serving the Blazor
Server shell from a manual deploy on 2026-09-08. There is no `.github/` directory at all, no script
of any kind in the repo, and no Azure credential CI could use. `tech-stack.md` has recorded
`github-actions` + `auto-deploy-on-merge` as settled decisions since day one — they are unwired, not
unchosen. Rollback currently depends on a git-ignored zip on one laptop.

## Desired End State

A commit lands on `main` and, with no human action, the app is built on Linux, packed through a
script that refuses to emit a malformed archive, retained as an artifact, deployed, and then checked
by fetching the live root page and asserting `200` on **every stylesheet and script that page
references**. Any break in that chain turns the run red. Every deployed archive is retained for 90 days, so a
rollback candidate always exists off this laptop and is known shape-valid; restoring one is a
documented `gh run download` + `az webapp deploy` procedure.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Azure authentication | OIDC federated credential | No long-lived credential in GitHub to rotate or leak, and it keeps `az` available so CI runs the same commands the record documents. |
| Branch name | Rename `master` → `main` | Two foundation docs already say `main`; pointing CI at `master` would add a third spelling, and there is exactly one branch and no open PRs to break. |
| Packaging | Committed script called by CI | Closes the F-01 finding — the rule becomes executable rather than prose, and stays usable for a manual emergency deploy. |
| Packer language | Python, not shell | **`zip` is not installed on the dev machine** (Git Bash ships only `unzip`); Python 3.10 + `zipfile` is present locally *and* on `ubuntu-latest`, so one script runs both places. |
| Post-deploy check | Root page **plus** every referenced asset | A Blazor page's HTML is mostly references to the scripts that make it work — a `200` on the document proves almost nothing, and this is the exact check that made the last deploy trustworthy. |
| Rollback | Retained artifact + **documented manual restore** | B1 has no deployment slots, so the artifact store is the only rollback substitute; retention alone delivers the roadmap's stated "artifact is retained" outcome. Automating redeploy-by-dispatch is deferred — it needs a `run_id` input, `actions: read`, a conditional download branch and a drill that mutates production, none of which the outcome asks for. |
| Infra in CI | Never — app deploy only | `AGENTS.md` requires human judgment on every predicted deletion because phantoms and real deletions look identical in `what-if` output. |
| Triggers | `push` on `main` + `workflow_dispatch` | Matches how the repo is actually used (direct commits, no PRs); a PR job would ship untested. |
| Hardening | Concurrency guard + update the repo's record | Both cheap; leaving docs pointing at a superseded manual ritual would re-create the same finding one layer up. |

## Scope

**In scope:** branch rename; `scripts/pack.py` (four shape assertions, non-zero exit);
`scripts/verify_deploy.py` (warm-up + asset checks); Entra app registration, resource-group-scoped
`Contributor`, federated credential, three GitHub secrets; `.github/workflows/deploy.yml` with
artifact retention and a concurrency guard; a build marker plus a cold-restore rehearsal; corrections
to `deploy-plan.md`, `TenExCards/AGENTS.md`, `infra/main.bicep`'s header comment, and the roadmap
Baseline.

**Out of scope:** automating redeploy of a prior run's artifact (and therefore any rollback drill
against production); deploying or `what-if`-ing `infra/main.bicep` from CI; any app-settings
mutation; a `pull_request` trigger; a test job (that arrives with `S-01`); `-warnaserror`; action SHA
pinning; path filters; slots, staging environments, containers, Application Insights.

## Architecture / Approach

```
push to main ──► checkout ──► setup-dotnet 10.0.x ──► dotnet publish -c Release
                                                            │
                                    scripts/pack.py ◄────────┘   (4 assertions, exit != 0 on fail)
                                            │
                          upload-artifact (90d, sha-named)
                                            │
                          azure/login (OIDC, no secret) ──► az webapp deploy --track-status false
                                            │
                              scripts/verify_deploy.py  (root 200 + every referenced asset 200)

rollback (manual, documented)  gh run download <run-id> ──► az webapp deploy ──► verify_deploy.py
```

The workflow is a thin caller. Every assertion it makes lives in a script a human can run locally,
so debugging never requires a workflow round-trip — and the emergency manual deploy uses the same
packer CI does.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Rename to `main` | Default branch matches the docs | Deleting `master` before changing the default orphans the branch |
| 2. Pack + verify scripts | The F-01 finding closed; both proven locally | A packer that works in CI but not on Windows — the reason it's Python |
| 3. Azure OIDC identity | CI can deploy with no stored secret | Tenant may block app registration; defined fallback is a publish profile |
| 4. The workflow | Push to `main` deploys itself | `--track-status true` hangs in CI; subject-string typo fails opaquely |
| 5. Prove + correct record | Restore rehearsed cold, docs point at scripts | A marker is needed or "which build is live" is unobservable |

**Prerequisites:** `F-01` complete (done). Permission to create an Entra app registration plus a role
assignment on `rg-tenexcards-plc`. `gh` 2.98.0 and Python 3.10.11 confirmed present.

**One stop-and-ask gate:** the plan carries literal PowerShell for every step, but Phase 3 opens with
an `az account show` session check. If the session has expired — likely, since the last recorded `az`
work was 2026-09-01 — the agent must stop and ask you to run `az login` in your own terminal rather
than attempting it. Everything else runs unattended.

**Estimated effort:** ~1–2 sessions across five phases; Phases 1 and 3 are mostly manual gates.

## Open Risks & Assumptions

- **Tenant may forbid non-admin app registration.** Free Trial subscriptions normally leave this on,
  but it is not verified. The plan carries a defined trigger and a publish-profile fallback rather
  than an open question.
- **A latent false-positive in the existing record.** `deploy-plan.md` step 5 asserts "no native
  `TenExCards` executable" — true on Windows (`TenExCards.exe`), **false on Linux**, where a
  framework-dependent publish legitimately emits an extensionless `TenExCards` apphost. Carrying that
  check into CI would fail every correct build. Phase 5 corrects it.
- **Artifact retention is 90 days**, so this is a rollback window, not an archive.
- **Rollback is not automated in this change.** Retention guarantees a candidate exists; putting one
  back is a manual, documented procedure. The risk is that an undrilled path is slower under
  pressure — accepted deliberately, since the roadmap outcome asks only that the artifact be retained.
- Every deploy still drops all Blazor circuits on container restart. Unchanged from manual deploys
  and out of scope at one worker.

## Success Criteria (Summary)

- A push to `main` reaches the live site with no typed command, and the run is red if anything —
  build, archive shape, deploy, or a single 404ing asset — is wrong.
- A previous build's artifact is retained, retrievable off this laptop, and provably shape-valid;
  the restore commands are written down and runnable.
- `TenExCards/AGENTS.md` points at a script that enforces the archive rules instead of describing
  them.
