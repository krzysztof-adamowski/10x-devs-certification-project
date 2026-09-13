# Test Plan

> Phased test rollout for this project. Strategy is frozen at the top
> (§1–§5); cookbook patterns at the bottom (§6) fill in as phases ship.
> Read before writing any new test.
>
> Refresh: re-run `/10x-test-plan --refresh` when stale (see §8).
>
> Last updated: 2026-09-14

## 1. Strategy

Tests follow three non-negotiable principles for this project:

1. **Cost × signal.** The cheapest test that gives a real signal for the risk wins. Do not
   promote to e2e because e2e "feels safer." Do not put a vision model on top of a
   deterministic visual diff that already catches the regression.
2. **User concerns are first-class evidence.** Risks anchored in "the team is worried about
   X, and the failure would surface somewhere in `<area>`" carry the same weight as PRD
   lines or hot-spot data.
3. **Risks are scenarios, not code locations.** This plan documents *what could fail* and
   *why we believe it's likely* — drawn from documents, interview, and codebase *signal*
   (churn, structure, test base). It does NOT claim to know which line owns the failure.
   That knowledge is produced by `/10x-research` during each rollout phase. If the plan and
   research disagree about where the failure lives, research is the ground truth.

A fourth rule is specific to this repository, and it is the reason several rows below exist
at all: **a check that cannot fail is worse than no check.** `context/foundation/lessons.md`
records two instances — an absence check whose broken command returned the desired answer,
and a gate that stayed green because no test entered the route that bypassed it. Every test
this rollout adds must be shown capable of failing for the right reason.

Hot-spot scope used for likelihood weighting: `TenExCards/TenExCards/`,
`TenExCards/TenExCards.Tests/`, `TenExCards/TenExCards.E2E/`, `infra/`, `scripts/`,
`.github/workflows/`. Excluded: `context/`, `.claude/`, `.agents/`, `Personal/`, and all
build output. 110 commits in the 30 days to 2026-09-13.

## 2. Risk Map

The top failure scenarios this project must protect against, ordered by risk = impact ×
likelihood. Risks are failure scenarios in user / business terms, not test names. The Source
column cites the *evidence that surfaced this risk* — never a specific file as "where the
failure lives" (that is research's job, see §1 principle #3).

| # | Risk (failure scenario) | Impact | Likelihood | Source (evidence — not anchor) |
|---|---|---|---|---|
| 1 | A request-pipeline change silently removes an authorization or transport guarantee. The site still answers `200`, and the guarantee is gone. | High | High | interview Q3; hot-spot dir `TenExCards/TenExCards/` root — 16 commits/30d, its top file 8; hot-spot dir `Components/Account/` — 13 commits/30d; `TenExCards/AGENTS.md` `### Authorization defaults to protected` (anonymous-by-folder inheritance; the static-asset case the deploy gate structurally cannot see) |
| 2 | A learner's generation fails for a provider condition the rotation exists to absorb — or spends the timeout budget re-asking a model that is already overloaded. | High | High | interview Q4; roadmap Open Question 2 (free tier measured at 20 requests/day/model from a `429` body — quota exhaustion is the expected path, not the exceptional one); hot-spot dir `Generation/` — 19 commits/30d; PRD Guardrail "generation never blocks without feedback" |
| 3 | A signed-in learner reaches cards belonging to a different account. | High | Medium | interview Q1; PRD `## Access Control` ("visible to no one else"); `lessons.md` "Put the validation gate on the call, not on one route to it" — this repository has already shipped a slice that routed around a seam-gate with every existing test still green; hot-spot dirs `Cards/` 11 and `Data/` 13 commits/30d; roadmap `S-06` landed 2026-09-14 and reads across cards |
| 4 | The three numbers the product's central bet is judged on are permanently wrong, because they were recorded wrong at the moment of the write. | High | Medium | PRD `## Success Criteria` (all three primary, plus the secondary edit rate) and FR-013; roadmap `S-06 outcome-recording` landed 2026-09-14, so the numbers are accruing in production now and `TenExCards/AGENTS.md` records that the edited fact, observable only at acceptance, cannot be backfilled; `S-06` also added a **second** counting surface beside the per-card fields |
| 5 | A configuration or schema change makes the deployed container fail to boot, on a tier with no slot to roll back to. | High | Medium | interview Q2; hot-spot dirs `Migrations/` 15 and `TenExCards/TenExCards/` root 16 commits/30d; `tech-stack.md` + `infrastructure.md` constraint (B1 Linux, no deployment slots, migrations forward-only on the boot path); `lessons.md` "Prove the check before trusting the result" |
| 6 | A learner's save produces two cards they did not ask for, or none, and nothing tells them which. | High | Medium | PRD Guardrail "No accepted card is ever silently lost" and FR-012; `context/archive/2026-09-13-manual-card-entry/plan.md`; `TenExCards/AGENTS.md` records the double-submit gap as an accepted gap with `S-04` named as its revisit trigger — and `S-04` has since landed, so the trigger has fired |
| 7 | A learner submits content that clears the interface's own bound, and the save throws in their face on a card only they can fix. | Medium | Medium | `lessons.md` "Put the validation gate on the call, not on one route to it" — the recorded incident, where a new edit path reached the save directly and every existing test stayed green; hot-spot dir `Components/Pages/` — 30 commits/30d, the highest-churn directory in the repository; PRD NFR (no silent truncation) |

**Abuse / security lens.** Satisfied by **#3** (authorization / ownership — does the query
verify *this card is yours*, not merely *you are signed in*) and **#7** (untrusted input /
server-side validation parity — a disabled control is a courtesy that dev tools remove).
Secret leakage and resource abuse were considered against this product's surfaces and are
addressed in §7 with reasons, not omitted.

**High-impact × low-likelihood scenarios are absent by intent**, not oversight. A provider
outage, a regional Azure failure, or exhaustion of every model in the rotation on one day
all end at the same place: the learner sees a failure and their pasted text is recoverable.
That is already the specified behaviour, and protecting it further belongs to observability,
not to a test.

### Risk Response Guidance

| Risk | What would prove protection | Must challenge | Context `/10x-research` must ground | Likely cheapest layer | Anti-pattern to avoid |
|---|---|---|---|---|---|
| #1 | A change that removes an authorization or transport guarantee makes a **test** red, not a deploy green. Specifically: an asset served to an anonymous request is still an asset, not a sign-in page. | "It returned `200`, so it works." The gate follows redirects and asserts status alone, so a gated asset — **and the root page itself** — resolves `302 → sign-in → 200` and is recorded as a pass; a fully gated site passes. Content type and landed URL are the oracles, not status. | Which guarantees are status-invisible, and which are enforced by an **absence** rather than a declaration — ordering failures are loud and largely self-reporting; metadata and platform are the fragile surface. Also: what an anonymous request for an asset actually returns, and what the existing deploy gate does and does not assert | integration over the real pipeline (the host that boots it already exists), plus one assertion added to the existing deploy-verify script | Asserting status codes only — that reproduces the exact hole the gate already has. Exercising the authenticated happy path, which cannot fail. |
| #2 | A provider condition meaning "try another model" advances the rotation; one meaning "the request or the key is wrong" stops it — observable as which calls were made, with zero live requests spent. | "It returned candidates, so the rotation works." The happy path never enters the rotation. And "a `5xx` is a server problem, so retry the same model" — this provider signals overload as readily as quota, which is why re-asking spends the budget backwards. | The transport seam at which a provider response can be faked; the current status-to-action matrix and where each branch is decided; where the retry cap sits and what it interacts with | unit / integration against a faked transport — **never** the live API | Spending live quota in a test. Asserting on returned card prose (see §7). Deriving the expected matrix by reading the implementation's own branching — that is the oracle problem; the expected value must come from the recorded decision. |
| #3 | A learner acting on a card that is not theirs is refused *indistinguishably from the card not existing* — and this holds on every route that reaches card data, not only the route that existed when the boundary was written. | "The store is tested, so the boundary holds." The store is the seam; the destination is the data. A new surface can reach the data without passing the seam. | Every current route from a request to card data, and whether any bypasses the ownership members; how "not yours" and "already gone" are made indistinguishable | integration over the real pipeline | Testing only the seam that is already tested. An enumeration test that would still pass if a new unguarded route were added tomorrow. |
| #4 | A saved card records how it was **created** — generated versus typed, reworded versus accepted as offered — correctly and immutably, on every route that creates one, before the slice that reads those numbers is built. | "A later edit changes the card, so it should update these." It must not: both facts describe creation, and a later edit that touches them destroys the measurement. And "we can backfill" — the candidate is discarded and the passage is gone. | Every route that creates a card and what each records; which later operations must leave these fields untouched | integration, one per creation route | Asserting the field equals what the handler just set — tautological. The oracle is the PRD's success criteria, not the code. |
| #5 | A setting or migration the deployed boot path requires is proven present **before** the merge that reads it, by a check that has itself been proven capable of failing. | "It passed locally." The test host runs in Development and loads the developer's own secret store; CI has none, and the deployed container has neither. Three environments, three different answers. | Everything the boot path demands before it will serve; which test hosts supply each and which do not; what the platform does when one is absent | integration asserting the guard fires — via a second, Production-shaped host, because the existing host pins Development by design; assert the guard throws on the absent setting, never construct a working encryptor | A check whose failure mode is indistinguishable from its pass — `lessons.md` records two instances. Every absence check carries a control that must be found. |
| #6 | A repeated or replayed submission produces exactly the cards the learner asked for — and where it provably does not, the boundary of what *is* closed is pinned so it cannot silently reopen. | "It is an accepted gap, so there is nothing to test." The refresh half **is** closed and is a real regression surface; and the gap's own recorded revisit trigger has now fired. | Which save routes exist now; what the post-redirect-get actually protects; whether anything below the handler could make a repeat idempotent | integration on the save route, plus one browser assertion for the refresh guarantee | Writing a test that asserts the duplicate — that pins a defect as intended behaviour. Assert the guarantee that holds; surface the one that does not as a decision, not a green test. |
| #7 | Content exceeding a storage bound is refused by the handler on **every** route to the save, with the learner able to act on the refusal — never truncated, never thrown at them from the database. | "The control is disabled, so it cannot be submitted." Dev tools remove a disabled control. The gate must be on the call, not on one route to it. | Every current route to the save; where the bound is re-checked versus merely displayed; what the database does with an over-long value | unit on the predicate, plus integration per route | Truncating instead of refusing — forbidden by the no-silent-loss guardrail. Testing the route that already passes through the gate and calling the destination covered. |

## 3. Phased Rollout

Each row is a discrete rollout phase that will open its own change folder via `/10x-new`.
Status moves left-to-right through the values below; the orchestrator updates Status as
artifacts appear on disk.

| # | Phase name | Goal (one line) | Risks covered | Test types | Status | Change folder |
|---|---|---|---|---|---|---|
| 1 | Pipeline guarantees under test | A change to pipeline ordering or authorization fails a test rather than reaching production green, and the deploy gate stops recording a redirected asset as a pass | #1, #5 | integration, gates | complete | `context/changes/testing-pipeline-guarantees/` |
| 2 | Provider rotation behaviour | The provider status-to-action matrix is enforced by a test instead of by a comment, with zero live quota spent | #2 | unit, integration | not started | — |
| 3 | Write-path integrity | Every route that writes a card is behind the ownership boundary and the storage bounds, and no route produces a card the learner did not ask for | #3, #6, #7 | integration, e2e | not started | — |
| 4 | Measurement surfaces agree | The batch counters `S-06` writes and the per-card origin/edit fields cannot silently diverge, so the three headline numbers read the same from either | #4 | integration | not started | — |
| 5 | AI-native rules-drift review | A change that weakens a rule this repository holds as prose is surfaced at review time — the regression class no test can catch, because the rule is a sentence | cross-cutting (#1, #3, #5) | AI-native review | not started | — |

**Order rationale.** Phase 1 carries the two highest-likelihood risks, reuses a test host
that already boots the real pipeline, and closes the interview's named low-confidence area —
so it is both the cheapest and the most protective. Phase 2 is independent of Phase 1 and is
sequenced second because its risk is equally likely and *entirely* uncovered: every test in
the repository stubs the generator interface, so the rotation lives below all of them.
Phase 3 extends coverage from seams to routes, which is the failure shape `lessons.md` has
already recorded once here. Phase 4 was originally sequenced to land *before* `S-06`, on the
grounds that the data it reads cannot be backfilled. **`S-06` landed on 2026-09-14 and that
window closed**, and re-checking the phase against the code found its stated goal already
met: `CardOwnershipTests` pins origin and the edited flag at the write and again after a
later repair (`UpdateForOwnerAsync_ForOwnCard_ReplacesTextAndPreservesOriginAndEdited`), and
`ManualCardEntryTests` pins the hand-written route — the two routes that call `CardOrigin`.
What `S-06` did introduce is a **second** way to count the same thing: `TriageBatch` carries
`AcceptedCount`, `RejectedCount` and `EditedCount`, written on a different path from the
per-card `Origin` and `Edited` fields, and nothing asserts the two agree. A divergence does
not make a number obviously wrong — it makes it *ambiguous*, which is worse for a metric the
product's central bet is judged on. Phase 4 is re-aimed at that seam. Phase 5 is last because it is a backstop, not a control: its value
is exactly the residue left once Phases 1–4 have converted every rule that *can* be pinned
by a test into a test.

**Status vocabulary** (fixed): `not started` → `change opened` → `researched` → `planned` →
`implementing` → `complete`.

## 4. Stack

The classic test base for this project. AI-native tools carry a `checked:` date so future
readers can see which lines need re-verification. Tool names are examples of a category, not
endorsements.

| Layer | Tool | Version | Notes |
|---|---|---|---|
| unit + integration | xUnit | net10.0 | `TenExCards.Tests` — 19 test files, 153 tests. **Gates the deploy**: named by path in CI before `Publish`. |
| assertions | AwesomeAssertions | — | Apache-2.0 fork of FluentAssertions v7. **Never FluentAssertions** — a licensing rule, see `TenExCards/AGENTS.md`. |
| integration host | `WebApplicationFactory` | net10.0 | Boots the real `Program.cs` pipeline against EF in-memory, no network. The reason Phase 1's layer is integration rather than e2e. |
| test doubles | hand-written stubs | — | Only three are permitted: the data provider, the key protector, and the candidate generator. No mocking library, by convention. |
| e2e | Playwright (.NET) | net10.0 | `TenExCards.E2E` — 2 files, 13 tests, drives the app over HTTP against a scripted generator. **Non-gating** (`continue-on-error: true`); a red run reports green in the step list. Rules in `TenExCards.E2E/AGENTS.md`. |
| provider faking | none yet — see §3 Phase 2 | — | The generator interface is stubbed everywhere, so the provider client's own behaviour has no seam under test today. |
| lint / format | none yet | — | No `dotnet format` step exists in CI. See §5 for why this is optional rather than required here. |
| accessibility | none (partial, incidental) | — | The browser suite selects by accessible role and label, so every assertion doubles as a weak accessibility check. No dedicated tool. |
| AI-native review | Claude Code `/code-review` — checked: 2026-09-13 | n/a | **When NOT to use:** never over a rule that a test already pins — the test is the gate and the reviewer is then noise competing with it. Stays advisory; a non-deterministic required gate teaches the team to re-run until green. |

**Stack grounding tools (current session):**
- Docs: **none** — no Context7 or framework-docs MCP exposed in this session; local manifests, CI, and the three `AGENTS.md` files were used as the stack evidence instead; checked: 2026-09-13
- Search: WebSearch / WebFetch available, **not used** — every stack-sensitive item above is stated as a category hypothesis for per-phase `/10x-research` to verify against current code, which is cheaper and truer than a search result; recorded rather than silently skipped; checked: 2026-09-13
- Runtime/browser: no Playwright **MCP**; Playwright is present as a project test dependency and is already wired; checked: 2026-09-13
- Provider/platform: **none** — no GitHub or Azure MCP; the `az` and `gh` CLIs are available to a human operator; checked: 2026-09-13

## 5. Quality Gates

"Required after §3 Phase `<N>`" means the gate is enforced once that rollout phase lands;
before that it is planned.

| Gate | Where | Required? | Catches |
|---|---|---|---|
| compile / type safety | local + CI | required (wired) | type drift — implicit in `dotnet test` and the Release publish |
| unit + integration | local + CI | required (wired) | logic regressions; gates the deploy before any publish work happens |
| archive shape | CI | required (wired) | the four-assertion check on the deployable archive; each failure deploys successfully and breaks at runtime |
| deployed-site smoke | CI | required (wired); **strengthened by §3 Phase 1, landed** | a page whose assets do not serve, an asset served as `text/html` because it is gated, a root page that redirected to sign-in, and a container no longer running as Production (asserted through the HSTS header). Status alone is no longer the oracle |
| e2e on critical flows | CI | wired, **non-gating** — revisit after §3 Phase 3 | broken learner journey. Promotion bar is recorded in `TenExCards.E2E/AGENTS.md`: stable green across many merges with every red traced to a real defect. That bar is earned over time, not by a rollout phase |
| AI-native rules-drift review | review time | **recommended, never required** — after §3 Phase 5 | weakening of a rule that lives as prose rather than as an assertion |
| deploy-script self-check | CI | required (wired by §3 Phase 1) | a syntax error in either deploy script, and a weakened assertion in `verify_deploy.py`. Runs before `Test`, so a broken gate stops the job rather than a deploy |
| lint / format | local + CI | **optional, deliberately** | style drift only. It maps to none of §2's seven risks, and §1 principle #1 outranks a default checklist. Revisit if formatting churn ever obscures a review |

Not listed, and deliberately: infrastructure-template gates, and a post-edit hook. The first
is human work by recorded decision (see §7). The second is configuration owned by a later
lesson, not by this rollout.

## 6. Cookbook Patterns

How to add new tests in this project. Each sub-section is filled in once the relevant rollout
phase ships; before that it names the pattern it will carry and the phase that will deliver
it.

### 6.1 Adding a pipeline or authorization test

- **Location and naming**: `TenExCards/TenExCards.Tests/`, one file per guarantee class.
  Read `TenExCards.Tests/AGENTS.md` first — it owns the harness rules, and this section
  does not repeat them.
- **Reference tests**: `PipelineMetadataTests` (authorization metadata),
  `BootPathGuardTests` (configuration the boot path demands).
- **Run**: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`, plus
  `python scripts/verify_deploy.py --self-test` when the change touches the deploy gate.
- **The pattern, in one line each.** For an authorization guarantee, enumerate
  `EndpointDataSource` and assert the `IAllowAnonymous` partition as an **exact set**, with
  the static-asset count as its own control — a status-code probe cannot see a marker on an
  endpoint nobody wrote a probe for, and an exact set fails on an addition as well as a
  removal. For a boot-path demand, assert the guard by its **message** under a second,
  Production-shaped host, with *the same absence under Development* as the in-run control.
- **Three things that will bite.** The shared factory pins `Environments.Development`, so
  the Production branch is unreachable through it — that is deliberate, not an oversight, and
  a second bare host is the way in. A bare host built as Development loads the developer's
  user-secrets, so an "omitted" setting is not omitted and the test passes in CI while failing
  locally. And four guards throw `InvalidOperationException` during service configuration, so
  a type-only assertion is satisfied by whichever throws first.
- **The deploy-side twin** lives in `scripts/verify_deploy.py --self-test`. Anything asserted
  about the live site rather than the pipeline belongs there — `UseHsts()` excludes `localhost`
  by default, so HSTS is one of the things only that side can see.
- **Prove it can fail before trusting it.** Break it deliberately, watch it go red for the
  right reason, revert. For a CI gate, read the run's **step list**, not its colour; a
  `workflow_dispatch` on a branch does this without touching production, because the federated
  credential is exact-match on `refs/heads/main`.

### 6.2 Adding a provider-behaviour test without spending quota

- TBD — see §3 Phase 2, for the "a provider status advances or halts the rotation, asserted
  against a faked transport" pattern.

### 6.3 Adding a test for a new route that writes a card

- TBD — see §3 Phase 3, for the "every route to the destination is behind the ownership
  boundary and the storage bounds" pattern.

### 6.4 Adding a browser (end-to-end) assertion

- **Rules live next to the suite**, in `TenExCards.E2E/AGENTS.md` — the collection fixture
  and fixed port, the circuit-fill race, selection by accessible name, and why a red run
  reports green. Read it rather than a summary here.
- **Reference tests**: `TenExCards/TenExCards.E2E/LearnerJourneyTests.cs`,
  `TenExCards/TenExCards.E2E/SavedCardsTests.cs`.
- **When to add one instead of an integration test**: only when the assertion is genuinely
  about what a learner sees across a real page. §3 Phase 3 adds one such assertion; see that
  phase's plan for the boundary it drew.

### 6.5 Adding a test for a creation-provenance field

- TBD — see §3 Phase 4, for the "how a card was created is recorded once, correctly, and is
  untouched by every later operation" pattern.

### 6.6 Per-rollout-phase notes

(Appended by `/10x-implement` as each phase lands — two or three lines on anything the phase
taught that the pattern above does not already say.)

**Phase 1 — Pipeline guarantees under test (2026-09-14).** Three things the pattern above
states but does not explain. A Production-shaped host given a real key identifier *does* reach
Key Vault — measured, failing `keys/wrap/action` from the operator's own `az` session while boot
and `GET /` both still succeeded — so the guard's control cannot be "the setting present ⇒ it
boots"; it is the same absence under Development. `Gemini:Models` could not be tested as an
absence at all: it ships in `appsettings.json` and .NET configuration has no deletion, so the
test asserts the shipped rotation is non-empty rather than faking a state no host can produce.
And the acceptance cases in a self-test are not padding — tightening the root-redirect rule to
reject every same-host redirect was caught only by them, and would otherwise have reported
`httpsOnly` working as a broken deploy.

## 7. What We Deliberately Don't Test

Exclusions agreed during the rollout. Future contributors should respect these unless the
underlying assumption changes.

- **Generated card text.** The model's prose is non-deterministic and card quality is judged
  by the learner at triage. An assertion against generated text is a flaky test, not a
  quality gate. Re-evaluate only if generation ever becomes deterministic. (Source: Phase 2
  interview Q5, and `TenExCards.Tests/AGENTS.md`.)
- **Anything that spends live provider quota.** The free tier is capped per model per day, so
  a suite calling the live API competes with the product for the product's own budget — and
  makes a red run indistinguishable from an exhausted quota. Re-evaluate if the project moves
  to a paid tier. (Source: roadmap Open Question 2.)
- **An offline judge scoring card quality against the four properties.** Tempting, because
  the prompt specification *is* the product and only its text is currently pinned. Rejected
  on cost × signal: the product already produces a truer oracle for free — the acceptance
  rate, which `S-06` now records and which the PRD names as the primary success
  criterion. A judge duplicates that signal, spends capped quota, and substitutes a model's
  opinion for the learner's. Re-evaluate if the acceptance rate ever proves unreadable.
  (Source: this plan's challenger pass, 2026-09-13.)
- **A vision model over the equal-prominence check.** That claim is already asserted
  deterministically by comparing rendered widths. Layering a model on top of a deterministic
  diff that already catches the regression is the anti-pattern §1 principle #1 names.
- **Infrastructure templates.** `what-if` is unreliable in both directions on this resource
  provider — it invents deletions and omits real ones — so the control is a human
  snapshot-deploy-diff, not an assertion. Re-evaluate only if that prediction becomes
  trustworthy. (Source: `TenExCards/AGENTS.md`.)
- **Whether a deployed circuit actually works.** `_framework/blazor.web.js` returns `200`
  whether or not any circuit opens, so a deploy with every circuit dead passes the gate fully
  green. Deferred on 2026-09-10 because circuits carried no product behaviour; `S-01`, `S-03`
  and `S-04` have since landed, so **re-deferred on 2026-09-14 with a new trigger**: revisit
  when `TenExCards.E2E` is promoted to gating (§5), since that suite already drives real
  circuits and a gate-side probe would duplicate it at higher cost while coupling the deploy
  script to a framework-internal protocol with no stability contract. **The residue is real**
  — E2E is `continue-on-error`, so a dead circuit still reports green in CI today.
- **The `Gemini:Models is empty` guard.** `Program.cs` refuses an empty model rotation at
  builder configuration, and **no test pins that guard** — `appsettings.json` ships a populated
  array and .NET configuration has no deletion, so no test host can produce the state the guard
  rejects. `BootPathGuardTests.ShippedModelRotation_IsNotEmpty` asserts the shipped array instead,
  which catches someone emptying it but would **not** catch the guard being removed. Accepted
  2026-09-14 as the honest residue rather than faked with a config-source override that would test
  the override. Re-evaluate if the rotation ever moves out of `appsettings.json`.
- **An anonymous `403`.** `UseStatusCodePagesWithReExecute("/not-found")` catches every `4xx`,
  so a `403` would render "does not exist". Measured 2026-09-14: it has no trigger. The
  fallback policy *challenges* with a `302` rather than forbidding, and no page carries a
  component-level `[Authorize]`, so an anonymous caller never receives a `403`. Re-evaluate the
  first time a policy that can forbid an authenticated user is added.
- **HSTS through the test host.** `UseHsts()`'s default excluded-hosts list contains
  `localhost`, so the header is absent in-process even over an `https` scheme — measured
  2026-09-14. The equivalent assertion lives in `scripts/verify_deploy.py`, against the live
  hostname, where it doubles as the only thing watching `ASPNETCORE_ENVIRONMENT`.
- **Paging, sorting, or browsing the saved-card list.** There is none by decision; the
  requirement was rewritten during shaping from "view every card" to "find a card in order to
  edit or delete it". A test here would be testing a Non-Goal into existence. (Source: PRD
  FR-009 and `## Non-Goals`.)

## 8. Freshness Ledger

- Strategy (§1–§5) last reviewed: 2026-09-14 (§2 risk #3/#4 sources and §3 Phase 4 re-aimed after `S-06` landed)
- Stack versions last verified: 2026-09-13
- AI-native tool references last verified: 2026-09-13

Refresh (`/10x-test-plan --refresh`) when:

- a new top-3 risk surfaces from the roadmap or archive,
- a recommended tool's `checked:` date is older than three months,
- the project's tech stack changes (new framework, new test runner),
- §7 negative-space no longer matches what the team believes.
