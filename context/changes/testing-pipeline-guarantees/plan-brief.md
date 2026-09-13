# Pipeline Guarantees Under Test — Plan Brief

> Full plan: `context/changes/testing-pipeline-guarantees/plan.md`
> Phase brief: `context/changes/testing-pipeline-guarantees/change.md`
> Research: `context/changes/testing-pipeline-guarantees/research.md`

## What & Why

Rollout Phase 1 of `context/foundation/test-plan.md`. Two risks, one change, because they are one
mechanism: a request-pipeline change can remove an authorization or transport guarantee while the
site still answers `200` (risk #1), and a configuration change can make the deployed container fail
to boot on a tier with no slot to roll back to (risk #5). The app launders a gated request into
`302 → sign-in → 200 text/html`; the deploy gate follows redirects and reads only the integer.
Fixing either side alone leaves the hole open from the other.

## Starting Point

`TenExCardsWebApplicationFactory` already boots the real `Program.cs` — real authentication, real
fallback policy, real endpoint graph, no network. `AuthBoundaryTests` pins `302`-never-`401` on the
gated routes. What is *not* pinned is the set of guarantees whose presence and absence produce the
same status code: six of sixteen, of which **two are enforced by an absence** — no `.AllowAnonymous()`
on the render-mode builder (guarded today by a human running `grep` against a ticked checklist line)
and `ASPNETCORE_ENVIRONMENT` being unset on the live site. `DataProtection:KeyIdentifier` is
exercised in none of the three environments.

## Desired End State

Removing `.AllowAnonymous()` from `MapStaticAssets()`, adding a third `.AllowAnonymous()` anywhere,
dropping a new page into `Components/Account/Pages/`, or merging code that reads a boot-path setting
not yet set — each fails a test locally and in CI, before anything deploys. A deploy serving a fully
gated site, or one no longer running as Production, fails `Verify` instead of printing
`DEPLOY VERIFIED`. Every new assertion has been observed failing for its intended reason.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| How to pin metadata guarantees (G4/G6/G7) | Enumerate the endpoint graph as an exact ANON/GATED set | The only shape that catches a marker nobody wrote a probe for — including one inherited from an `_Imports.razor` | Plan (probe) |
| Control for the D6 guard | The **same absence under Development**, asserted not to throw | A Production host given a key identifier was measured reaching Key Vault from a dev machine — the control research proposed is the wall the Development pin exists to hold | Plan (probe) |
| `verify_deploy.py` root redirect | Fail on a **path** change; a scheme-only upgrade still passes | Keeps the 2026-09-10 narrowing that protects `httpsOnly` | Research |
| `ASPNETCORE_ENVIRONMENT` unset | Assert `Strict-Transport-Security` on the live `https` root | `UseHsts()` excludes `localhost` by default, so this is provably unassertable in-process | Plan (probe) |
| Deploy-script coverage | `--self-test` plus a CI step before `Test` | A compile check kills syntax errors but proves nothing about whether the new assertions can fail | Plan |
| F4 — dead circuits pass the gate | **Re-deferred**, trigger rewritten to E2E promotion | `TenExCards.E2E` already drives real circuits; a gate-side probe duplicates it at higher cost | Plan |
| `RedirectToLogin` / the anonymous `403` | Pin the behaviour, document the components, change no production code | Both measured to have no trigger; both become correct if `Routes.razor` ever goes interactive | Plan (probe) |

## Scope

**In scope:** the endpoint-metadata contract; the enhanced-navigation assertion; per-demand
boot-path guard assertions under a second, Production-shaped host; content-type, landed-path and
HSTS oracles in `verify_deploy.py`; a `--self-test` mode and its CI step; deliberate-failure proof
for every new gate; cookbook §6.1 and the stale records.

**Out of scope:** G2/G3 pipeline ordering (loud and self-reporting); F4; HSTS through the test host;
constructing any working key-ring encryptor; deleting `RedirectToLogin` or branching the status-code
re-execute; promoting `TenExCards.E2E` to gating; tests for `pack.py`; any `env:` block in
`deploy.yml`.

## Architecture / Approach

Two test layers plus two script oracles. The **metadata layer** enumerates `EndpointDataSource` from
the already-booted host and asserts an exact partition — 44 static-asset endpoints all anonymous
with the count as its own control, and 15 routable endpoints split 6 anonymous / 9 gated, the gated
group being G6 made visible. The **boot-path layer** adds a bare factory parameterised by
environment, asserting each guard fires *by message, not by type* — the `MigrationGuardTests`
lesson — with the same absence under Development as the in-run control. The **gate layer** changes
`fetch()` to surface the content type, uses the landed URL already being discarded, and gains a
`--self-test` mode so the new predicates have a control.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Endpoint metadata contract | `PipelineMetadataTests` — exact ANON/GATED partition, asset-count control, enhanced-nav assertion | A legitimate new page fails the test until classified (intended, but must read as the test working) |
| 2. Boot-path guards | `BootPathGuardTests` — D1/D4/D5/D6 by message, D6's Development control | A new bare factory that omits a guarded setting passes locally, fails only in CI |
| 3. Second oracle in the gate | Content-type, landed-path and HSTS assertions in `verify_deploy.py` | Over-tightening the redirect rule would break `httpsOnly` |
| 4. `--self-test` + CI step | Synthetic pass/fail cases; a step before `Test` | Self-test grows into a second suite to maintain |
| 5. Prove every gate can fail | Deliberate red for each assertion; one CI run read from the **step list** | Deliberate failure must sit in the self-test step, never in `Verify`, which runs after `Deploy` |
| 6. Cookbook and records | §6.1 pattern, §3/§5/§7/§8 updates, `AGENTS.md` grep replaced | Stale records outliving the change that made them stale |

**Prerequisites:** none — `TenExCards.Tests`, `Microsoft.AspNetCore.Mvc.Testing` and
`public partial class Program;` are all already in place. Sub-phases 3 and 5 need access to the live
site and a push to `main`.
**Estimated effort:** ~2 sessions across six sub-phases; sub-phase 5 spans a CI round trip.

## Open Risks & Assumptions

- **The routable endpoint list is a maintenance surface.** Adding a page fails sub-phase 1's test
  until someone classifies it. That is the design, but it will read as friction the first time.
- **Sub-phase 5 requires two pushes to `main`**, one deliberately failing. It never deploys — the
  self-test step precedes `Test` — but it does put a red run in the history, as `S-01`'s proof did.
- **The HSTS assertion depends on `azurewebsites.net` not being HSTS-preloaded in a way that makes
  the header redundant.** It asserts the response header directly, so preloading does not affect it,
  but the assertion's *meaning* as an `ASPNETCORE_ENVIRONMENT` proxy rests on `UseHsts()` being the
  only thing that emits it.
- **F4's residue is real and stated rather than closed.** `TenExCards.E2E` is `continue-on-error`,
  so a deploy with every circuit dead still reports green in CI today.

## Success Criteria (Summary)

- A removed or added authorization marker anywhere in the pipeline makes a test red before it makes
  a deploy green.
- A boot-path setting read by merged code but absent from the app fails at `Test`, not at the
  container that will not serve.
- Every assertion added by this phase has been observed failing for its intended reason, with the CI
  proof read from the step list rather than the colour.
