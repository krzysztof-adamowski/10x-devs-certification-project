---
project: "10xCards"
version: 1
status: draft
created: 2026-09-02
updated: 2026-09-13
prd_version: 1
main_goal: speed
top_blocker: time
milestone_id: mvp-paste-to-saved-cards
milestone_seq: 1
milestone_status: open
---

# Roadmap: 10xCards

> Derived from `context/foundation/prd.md` (v1) + auto-researched codebase baseline.
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Milestone

**M-01: MVP — pasted text to saved cards** — Status: open

- **Intent:** Prove that a stated, enforced definition of card quality turns a passage a learner
  has just read into cards they keep — end to end, in a private account, on the live deployment.
- **Source materials:** `context/foundation/prd.md` (v1), with `context/foundation/tech-stack.md`,
  `context/foundation/infrastructure.md`, and `context/deployment/deploy-plan.md` as supporting
  inputs.
- **Done when:** every F-NN and S-NN below is `done`.
- **Scope anchors:** FR-001 through FR-013 (all thirteen marked must-have), US-01, the six
  Non-Functional Requirements, and the single rule stated in `## Business Logic`.

## Vision recap

A self-directed adult learner finishes reading something, opens a blank card editor, and stalls —
not from laziness but because they cannot convert prose into good retrieval prompts. The cards they
force out bundle several facts together and copy the source verbatim, so weeks later the reviews
test recognition rather than recall and they quit a method that works.

The bet is that the missing thing is not speed but a **specification of card quality, enforced
identically every time** — something a general-purpose chat window cannot offer, because it holds
no consistent, checkable definition of what makes a card good. That specification is the product,
and it is written out in the PRD's `## Business Logic`.

## North star

**S-02: Learner turns a pasted passage into saved cards** — placed as early as its prerequisites
allow, because it is the one slice whose success or failure decides whether the product has a
reason to exist.

> **North star**, in this document, means: the smallest end-to-end flow whose successful delivery
> would prove the product's central bet — that a stated, enforced definition of card quality
> produces cards a learner keeps. Everything else here only matters if this one works, so it is
> sequenced as early as dependencies permit rather than placed for symmetry.

## At a glance

| ID   | Change ID                | Outcome (user can …)                                             | Prerequisites    | PRD refs                                               | Status   |
| ---- | ------------------------ | ---------------------------------------------------------------- | ---------------- | ------------------------------------------------------ | -------- |
| F-01 | `blazor-server-shell`    | (foundation) the deployed app serves an interactive Blazor page  | —                | NFR (2s acknowledgement), NFR (desktop browsers)        | done |
| F-02 | `persistence-spine`      | (foundation) the deployed app reads and writes a real database   | —                | NFR (accepted card durable), Guardrail (no silent loss) | done |
| F-03 | `deploy-pipeline`        | (foundation) a merge to main deploys without hand-built archives | F-01             | NFR (2s acknowledgement)                                | done |
| S-01 | `accounts-and-sessions`  | register, sign in, and sign out of a private account             | F-01, F-02       | FR-001, FR-002, FR-003, Access Control                  | done |
| S-02 | `passage-to-saved-cards` | paste a passage and finish with accepted cards saved             | S-01             | FR-004, FR-005, FR-006, FR-007, US-01, Business Logic   | in-progress |
| S-03 | `edit-before-accepting`  | fix a candidate's wording before accepting it                    | S-02             | FR-008, US-01                                           | done |
| S-04 | `manage-saved-cards`     | find a saved card in order to edit or delete it                  | S-02             | FR-009, FR-010, FR-011                                  | done |
| S-05 | `manual-card-entry`      | write a card by hand without generating one                      | S-02             | FR-012                                                  | done |
| S-06 | `outcome-recording`      | determine the acceptance, AI-origin, and edit rates              | S-03, S-04, S-05 | FR-013, Success Criteria                                | proposed |

## Streams

Navigation aid — groups items that share a Prerequisites chain. Canonical ordering still lives in
the dependency graph below; this table is the proposed reading order across parallel tracks.

| Stream | Theme                         | Chain                             | Note                                                                                                                         |
| ------ | ----------------------------- | --------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| A      | Spine to the north star       | `F-01` → `F-02` → `S-01` → `S-02` | `F-01` and `F-02` are independent of each other and can run in parallel; both gate `S-01`. The stream ends at the north star. |
| B      | Delivery safety               | `F-03`                            | Standalone; needs only `F-01`. Removes the hand-built-archive step every later slice would otherwise repeat.                  |
| C      | Completing the card lifecycle | `S-03` → `S-04` → `S-05` → `S-06` | All join Stream A at `S-02`. `S-03`, `S-04` and `S-05` are mutually parallel; `S-06` needs all three to exist first.          |

## Baseline

What's already in place in the codebase as of `2026-09-02` (auto-researched + user-confirmed).
Foundations below assume these are present and do NOT re-scaffold them.

- **Frontend:** absent — no Razor or Blazor components, no `wwwroot/`, no JS tooling. The web SDK is
  set but nothing registers Razor, Blazor, or static files.
- **Backend / API:** partial — ASP.NET Core minimal API on `net10.0`. The only route is the
  untouched template scaffold at `TenExCards/Program.cs:22-34`; the sole package is
  `Microsoft.AspNetCore.OpenApi`. No controllers, no DI registrations.
- **Data:** absent — no ORM or database driver, no context, no entities beyond the template record,
  no migrations directory, and no connection-string configuration in either settings file.
- **Auth:** absent — no identity package, no authentication or authorization middleware, no
  authorization attributes, and no Data Protection key persistence. The single endpoint is
  anonymous.
- **Deploy / infra:** partial — `infra/main.bicep` provisions a B1 Linux App Service plan in
  `polandcentral` with HTTPS-only and Always On, and the site is live. Deployment is **no longer
  manual CLI**: `F-03` landed `.github/workflows/deploy.yml`, which builds, packs, retains and
  verifies on every push to `main` via OIDC, delivering the auto-deploy-on-merge decision
  `tech-stack.md` records. Infrastructure and app settings stay deliberately outside it, so
  `az deployment group create` remains human work. No container definition.
- **Observability:** partial (platform-only) — App Service filesystem logs at `Information`, retained
  3 days / 100 MB (`retentionInDays` / `retentionInMb` on the `logs` resource in
  `infra/main.bicep`). No telemetry service, no workspace, no health endpoint,
  and no logging configuration inside the application.

Confirmed by the user, with one amendment recorded: the Blazor Server, Identity, Azure App Service,
and GitHub Actions entries in `tech-stack.md` are **deliberate decisions already taken**, not agent
speculation. They are unwired, not unchosen — so the Foundations below implement settled choices
rather than reopening them.

## Foundations

### F-01: Interactive Blazor Server shell replaces the API scaffold

- **Outcome:** (foundation) the deployed app serves an interactive Blazor Server page over a live
  circuit, and the template sample route is gone.
- **Change ID:** `blazor-server-shell`
- **PRD refs:** NFR (2s acknowledgement), NFR (desktop browsers)
- **Unlocks:** `S-01`, `S-02`, `S-03`, `S-04`, `S-05`, and `F-03`. Also enables the only honest
  verification path for the 2-second acknowledgement requirement — a real circuit on the deployed B1
  Linux instance, since that latency cannot be measured against a local run.
- **Prerequisites:** —
- **Parallel with:** F-02
- **Blockers:** —
- **Unknowns:** — (the HTTPS-redirect question is resolved; see Open Roadmap Question 4)
- **Risk:** Sequenced first because nothing renders without it. The failure mode is scope creep —
  this establishes the host and nothing else, and every slice still builds its own surface. The
  secondary risk is carrying the scaffold's incidental decisions forward untouched while rewriting
  around them.
- **Landed 2026-09-08:** Blazor Web App with **per-page interactivity** — pages are static-rendered
  unless they carry `@rendermode InteractiveServer`, which is what lets `S-01`'s Identity pages write
  cookies to the response without a carve-out. `app.UseHttpsRedirection()` was removed and the
  platform is now the sole enforcement point (Open Roadmap Question 4). A throwaway page proved the
  circuit was live; `S-01` retired it on 2026-09-12, as this slice always intended.
- **Status:** done

### F-02: Persistence spine — provisioned database reachable from the deployed app

- **Outcome:** (foundation) the deployed app reads and writes a provisioned database, with the
  connection string set outside infrastructure-as-code and a repeatable migration path in place.
- **Change ID:** `persistence-spine`
- **PRD refs:** NFR (accepted card durable), Guardrail (no silent loss)
- **Unlocks:** `S-01`, `S-02`, `S-04`, `S-05`, `S-06`. Also reduces Open Roadmap Question 1 (which
  database provider), which currently sits under every persisting slice.
- **Prerequisites:** —
- **Parallel with:** F-01
- **Blockers:** —
- **Unknowns:**
  - ~~Which database provider and tier? The infrastructure risk register rules out auto-pausing
    tiers, because the first query after an idle period can exceed the entire 2-second
    acknowledgement budget — Owner: user (resolved downstream as course-work). Block: no.~~
    **Resolved 2026-09-10 in `F-02`:** EF Core 10.0.12 against **Azure SQL, S0 provisioned**. Full
    reasoning in Open Roadmap Question 1; do not re-open the provider question.
- **Risk:** Deliberately designs no **domain** schema — identity tables arrived with `S-01` and the
  card entity arrives with `S-02`. As shipped it created two tables, `SpineProbes` and
  `DataProtectionKeys`, neither modelling anything about the product; `S-01` dropped the first on
  2026-09-12, so only `DataProtectionKeys` survives from this foundation. Three recorded traps sit
  here: an auto-pausing tier silently breaks the acknowledgement requirement; declaring application
  settings inside infrastructure-as-code makes a routine, successful-looking deployment delete the
  connection string; and `Database.Migrate()` on the boot path means a bad migration takes the app
  down on a tier with no slot rollback, with no schema reversal available.
- **Landed 2026-09-10:** `sql-tenexcards-plc` in `polandcentral` with **two** databases —
  `sqldb-tenexcards` (S0 provisioned) for the app and `sqldb-tenexcards-dev` (Basic) for local
  development, the split enforced by the contained user `tenexdev`. Key Vault `kv-tenexcards-plc`
  holds the connection strings; the app reads a versionless Key Vault *reference*. The Data
  Protection key ring persists to `DataProtectionKeys` and survives a container restart. As shipped
  it was stored **unencrypted**, which this slice left to `S-01` rather than closing itself;
  `S-01` closed it on 2026-09-12 with `.ProtectKeysWithAzureKeyVault(...)`, so the ring is
  encrypted at rest today.
- **Status:** done

### F-03: Merges deploy themselves

- **Outcome:** (foundation) a merge to the main branch builds, packages, and deploys the app, and the
  deployable artifact is retained.
- **Change ID:** `deploy-pipeline`
- **PRD refs:** NFR (2s acknowledgement)
- **Unlocks:** a named verification path for every slice from `S-01` onward — each becomes checkable
  on the live instance without repeating a hand-assembled upload. Also mitigates the two
  highest-likelihood deployment risks on record: a nested archive that deploys successfully and then
  fails at runtime, and the absence of any slot-based rollback at this tier.
- **Prerequisites:** F-01
- **Parallel with:** — (was planned parallel with `S-01`; `F-03` finished first, so the two never
  overlapped)
- **Blockers:** —
- **Unknowns:** —
- **Carried forward from F-01 (2026-09-08), discharged by this slice:** the packaging step had to
  become an executable script rather than prose. `TenExCards/AGENTS.md` forbids `Compress-Archive`
  (Windows PowerShell 5.1 writes backslash separators into nested entries) and requires pre-upload
  assertions; F-01 verified them by hand and committed no script, so the rule depended on someone
  reading carefully. `scripts/pack.py` now enforces them and is the authority on the list, which has
  since grown to **four** — `TenExCards.dll` at the archive root, no entry prefixed `publish/`, no
  entry containing a backslash, and at least one entry under `wwwroot/`. The failure mode that
  justified all of it stands: a wrong archive **deploys successfully** and then serves a page whose
  every asset 404s, with no signal to catch it, which is why `scripts/verify_deploy.py` runs after
  every deploy. Running the pack on Linux avoids the separator problem but none of the other three.
  Detail: the 2026-09-08 record in `context/deployment/deploy-plan.md`.
- **Risk:** The only foundation here not strictly required before the next slice — manual deployment
  already worked. It earned its place on repetition: every remaining slice gets deployed and
  verified, and the archive-shape trap is a once-per-deploy chance to lose an evening. It was
  nominated as the first foundation to Park if time compressed; it was not parked, and that option
  is now spent.
- **Landed 2026-09-11:** `.github/workflows/deploy.yml` deploys every push to `main`, as a **thin
  caller** over `scripts/pack.py` then `scripts/verify_deploy.py` — the same two commands a human
  runs, so CI cannot drift from the documented rules without those scripts changing. Auth is OIDC
  with **no stored Azure credential**; the federated credential is exact-match on an immutable
  subject, which is why the app registration deliberately carries two credentials. Three settings are
  deliberate and must not be "fixed": no `-o` on publish, `--track-status false` on deploy, and
  `permissions` of exactly `id-token: write` + `contents: read`. Infrastructure, app settings, and
  redeploying a prior archive stay human work. Phase 5's cold-restore rehearsal was re-performed
  2026-09-12 against a real prior artifact without mutating production.
- **Status:** done

## Slices

### S-01: Learner registers, signs in, and signs out

- **Outcome:** user can register with an email address and a password, sign in with the same pair,
  and sign out — and everything they create belongs to that account and is visible to nobody else.
- **Change ID:** `accounts-and-sessions`
- **PRD refs:** FR-001, FR-002, FR-003, Access Control
- **Prerequisites:** F-01, F-02
- **Parallel with:** F-03
- **Blockers:** —
- **Unknowns:**
  - ~~How long is the inactivity window before a signed-in session expires? Carried from the PRD's
    `## Open Questions`, which classes it as a planning detail rather than a product decision —
    Owner: user. Block: no.~~ **Resolved 2026-09-12 in `S-01`: seven days, sliding.** Set in
    `Program.cs` via `ConfigureApplicationCookie` (`ExpireTimeSpan` seven days,
    `SlidingExpiration` true), so the window renews on activity rather than counting from sign-in.
    Sliding is the decision, not the number: with no password recovery a forgotten password is a
    dead account, so signing an active learner out buys nothing and costs the one credential they
    have. Asserted by a test rather than left to the comment beside it. Full reasoning in Open
    Roadmap Question 3.
- **Risk:** Identity's signing keys are not persisted by default, and the failure this causes looks
  like a scaling problem while actually biting at a single instance: every container restart —
  deploy, platform maintenance, recycle — logs every user out and starts rejecting form submissions.
  **`F-02` already closed this**, because `UseAntiforgery()` meant the ephemeral ring was already
  protecting something; the ring persists to the database and survived a restart. So this slice
  **verifies rather than implements** it — render a form, restart the container, submit the
  already-rendered form and confirm it is accepted rather than rejected with `400`. Checking that
  the key count did not change is necessary but not sufficient: a ring with no reason to rotate
  looks identical to a working one. What `F-02` did **not** do is encrypt the ring at rest — it is
  plaintext in `DataProtectionKeys.Xml`, which only buys token forgery today but becomes session
  forgery the moment this slice makes the same keys sign auth cookies. That is this slice's to
  weigh. This is also the first slice to persist **account-scoped** data, so the test project is
  created here and its tests ship alongside this code; `F-02` touched persistence first but
  contains no deterministic rule to test.
- **Landed 2026-09-12:** Identity on `IdentityUserContext<ApplicationUser>` — the **role-free** base,
  so "never add roles" is structural under forward-only migrations rather than conventional — with
  register and sign in as two hand-written statically rendered pages, and sign-out as a POST
  endpoint rather than a third page — a component action cannot clear the cookie — against the .NET
  template's 47 files. Authorization defaults to **protected** via a fallback policy; four surfaces
  carry `[AllowAnonymous]`, and `MapStaticAssets()` needs it explicitly or every stylesheet
  `302`s to the login path in a way `verify_deploy.py` records as a pass. The risk above was
  discharged in both halves: persistence re-verified in its strongest form (sign in, restart the
  container, reuse the pre-restart cookie), and the ring **encrypted at rest** with a Key Vault key,
  done first while no account existed because closing it means discarding a key. `TenExCards.Tests`
  exists and gates the deploy — proven by failing one deliberately and reading the run's step list.
- **Status:** done

### S-02: Learner turns a pasted passage into saved cards

- **Outcome:** user can paste a passage with an optional focus hint, watch bounded progress while it
  is worked on, review the resulting candidates one at a time, and accept or reject each — with every
  accepted card saved immediately to their own space.
- **Change ID:** `passage-to-saved-cards`
- **PRD refs:** FR-004, FR-005, FR-006, FR-007, US-01, Business Logic
- **Prerequisites:** S-01
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - Which model provider generates the candidates? No client, package, or configuration exists yet —
    Owner: user (resolved downstream as course-work). Block: no.
- **Risk:** The north star, and the heaviest item here — kept whole rather than split because
  candidates that cannot be accepted are worth nothing, so no intermediate split ships anything.
  Nearly every launch-gating requirement lands in this one slice: the form must acknowledge within
  two seconds and never freeze, the wait must be bounded at thirty seconds, an over-length submission
  must be refused *before* generation begins rather than after, the passage must be discarded once
  its candidates exist, and no accepted card may be lost to a refresh or a dropped connection.
  Untriaged candidates are discarded when the session ends, and the interface must say so rather than
  imply they will return. Rejecting must cost no more effort than accepting, or the acceptance target
  measures the interface instead of the cards. The externally required test written from the
  learner's perspective **did not attach here** — it moved to `S-03`, see that entry. If this slice
  slips, the milestone slips.
- **Landed 2026-09-13:** a signed-in learner pastes up to 12,000 characters with an optional focus
  hint, watches progress backed by a streamed chunk count, and triages candidates one at a time with
  accept and reject at equal prominence; each accepted card is written before the next candidate
  appears, and the passage is cleared **before** the triage state is entered, so no moment exists in
  which both it and its candidates are live. One gated `@rendermode InteractiveServer` component
  holds the batch, because navigating between routes would dispose it. The four deterministic rules
  — the length guard, the target-and-cap computation, the deduplicator and the column bounds — are
  pure functions with their own tests, which is what keeps the component thin enough to be verified
  by hand. **Measured on the deployed B1 instance at maximum length (11,984 characters, 11
  candidates): acknowledgement clearly under 2s, generation under 10s against a 30s budget**, so
  `TimeoutSeconds` stayed at 30 and the PRD needed no amendment. What the plan did not anticipate:
  the free tier's 20/day per-model ceiling, which turned one model into a rotation; Gemini answering
  `503` as readily as `429`; and two Blazor traps recorded in `TenExCards/AGENTS.md` — `Assets` is a
  protected `ComponentBase` property rather than an injectable service, and a component must leave
  its state consistent *before* an await, because the renderer runs at the first one that yields.
- **Status:** in-progress

### S-03: Learner edits a candidate before accepting it

- **Outcome:** user can correct a candidate's wording and then accept the corrected version.
- **Change ID:** `edit-before-accepting`
- **PRD refs:** FR-008, US-01
- **Prerequisites:** S-02
- **Parallel with:** S-04, S-05
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Sequenced after the north star because it is an affordance on a loop that must already
  work. The recorded hazard is that editing quietly becomes the path of least resistance: the learner
  repairs weak candidates instead of rejecting them, acceptance stays high, and generation
  underperforms undetected. The PRD answers this by tracking the edit rate as its secondary success
  measure — which is exactly what `S-06` records.
- **Carries the externally required learner-perspective test, moved here from `S-02` on
  2026-09-13.** The roadmap originally attached it to `S-02` "with US-01's acceptance criteria as
  its basis", and `S-02` deliberately did not write it. The reason is US-01's own first acceptance
  criterion, which requires **accept, reject and edit** at equal prominence: edit does not exist
  until this slice, so a test written against US-01 in `S-02` would have had to skip its own opening
  criterion. It is a browser-driven test, so the argument `S-02` makes against component tests — that
  a `@rendermode InteractiveServer` component cannot be driven by the HTTP harness — does not reach
  it and is not a reason to defer it again. **This entry is the only durable record of the move**:
  `S-02`'s plan is archived with its change.
- **Status:** done
- **Landed 2026-09-13:** triage offers three actions at equal prominence, and the edit swaps the
  card body for two bounded, counted fields in place. `Card.Edited` records whether the learner
  reworded an accepted card — captured here because acceptance is the **only** moment it is
  observable, so it is the signal `S-06` will read rather than derive. The counters moved into
  `Generation/TriageSession.cs`, which finally makes "each candidate is triaged exactly once"
  assertable; `S-02`'s review had closed a re-entrancy hole against that invariant but left the
  guard unasserted. The inherited learner-perspective test landed as `TenExCards.E2E` on
  **Playwright**, driving a locally started app against a scripted generator — the Gemini free tier
  caps requests per model per day and returns non-deterministic prose, so it could not supply one.
  It runs in CI **non-gating** (`continue-on-error: true`), the only such step in the workflow;
  revisit once it has a track record.

### S-04: Learner finds a saved card in order to edit or delete it

- **Outcome:** user can locate one of their own saved cards and either correct it or remove it.
- **Change ID:** `manage-saved-cards`
- **PRD refs:** FR-009, FR-010, FR-011
- **Prerequisites:** S-02
- **Parallel with:** S-03, S-05
- **Blockers:** —
- **Unknowns:** —
- **Risk:** The requirement was deliberately rewritten during shaping from "view every card" to "find
  a card in order to edit or delete it", because nobody browses a card deck. Building a
  browse-and-organize surface here would reintroduce a Non-Goal — there are no decks, tags, or
  organization in this version. Every query must be scoped to the owning account; the user model is
  flat and there is no cross-account visibility of any kind.
- **Status:** done

### S-05: Learner creates a card manually

- **Outcome:** user can write a card by hand and save it, without generating one first.
- **Change ID:** `manual-card-entry`
- **PRD refs:** FR-012
- **Prerequisites:** S-02
- **Parallel with:** S-03, S-04
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Sequenced late on purpose. It reopens the blank-editor failure the product exists to fix,
  so it completes the model rather than serving as a primary path — and the PRD's target that three
  quarters of a learner's cards come from generation is only meaningful because this path exists to
  be the minority.
- **Landed 2026-09-13:** `/cards/new`, a **statically rendered** `EditForm` POST on the Identity
  pages' pattern rather than `Generate`'s circuit — a two-field form needs no circuit, and the B1
  memory ceiling arrives without back-pressure. Reached from a link in `Generate`'s compose branch
  only, with no nav item and no home-page link, so generation stays the default path the
  75%-generated target depends on. `CardOrigin.Manual` and `edited: false`; the enum value and the
  column both already existed, so this slice needed **no migration and no `ICardStore` change**.
  Three things the plan did not anticipate. `SaveAsync` had gained an `edited` parameter from `S-03`
  in the meantime. A `[SupplyParameterFromQuery] int?` **throws a `500`** on a non-numeric value, and
  the this-visit tally sits in a learner-editable URL — it binds as `string` and is parsed
  defensively. And post-redirect-get closes duplication on **refresh only**: a double-click still
  writes two rows, recorded in `TenExCards/AGENTS.md` as an accepted gap to revisit with `S-04`,
  since closing it server-side needs an idempotency key through the store and nothing can delete the
  duplicate until `S-04` exists. Verified on the live instance: anonymous `/cards/new` answers `302`,
  and one hand-written card sits in `sqldb-tenexcards` with `Origin = 2` after four refreshes.
- **Status:** done

### S-06: Triage outcomes and card origin are recorded

- **Outcome:** user, acting as the product's owner, can determine the acceptance rate, the share of
  cards produced by generation rather than typed by hand, and the edit rate on accepted cards.
- **Change ID:** `outcome-recording`
- **PRD refs:** FR-013, Success Criteria
- **Prerequisites:** S-03, S-04, S-05
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Last because it needs every path it measures to exist first — the origin share needs
  manual entry, the edit rate needs the edit affordance. Two constraints bound it tightly. The
  requirement is that these outcomes are *recorded*, not displayed: it creates no in-product surface
  and no operator role, and adding a dashboard would contradict the access model, which states there
  is no operator or admin view. And recording a rejection must not retain content derived from the
  passage the product promised to discard — the same argument that removed resumable triage from
  scope applies here.
- **Status:** proposed

## Backlog Handoff

| Roadmap ID | Change ID                | Suggested issue title                                            | Ready for `/10x-plan` | Notes                                                 |
| ---------- | ------------------------ | ---------------------------------------------------------------- | --------------------- | ----------------------------------------------------- |
| F-01       | `blazor-server-shell`    | Replace the API scaffold with an interactive Blazor Server shell | —                     | Done 2026-09-08 — see `## Done`                       |
| F-02       | `persistence-spine`      | Stand up a provisioned database reachable from the deployed app  | —                     | Done 2026-09-10 — see `## Done`                       |
| F-03       | `deploy-pipeline`        | Deploy automatically on merge to main                            | —                     | Done 2026-09-11 — see `## Done`                       |
| S-01       | `accounts-and-sessions`  | Register, sign in, and sign out of a private account             | —                     | Implemented 2026-09-12 — awaiting `/10x-archive`      |
| S-02       | `passage-to-saved-cards` | Paste a passage and finish with accepted cards saved             | no                    | Needs S-01 — north star                               |
| S-03       | `edit-before-accepting`  | Edit a candidate card before accepting it                        | no                    | Needs S-02                                            |
| S-04       | `manage-saved-cards`     | Find a saved card in order to edit or delete it                  | no                    | Needs S-02                                            |
| S-05       | `manual-card-entry`      | Create a card manually                                           | no                    | Needs S-02                                            |
| S-06       | `outcome-recording`      | Record triage outcomes and card origin                           | no                    | Needs S-03, S-04, S-05                                |

This table is the clean handoff to Jira/Linear or any MCP-backed backlog. It should be compact enough
to copy into issues, but it must not duplicate the detailed roadmap body.

## Open Roadmap Questions

1. ~~**Which database provider and tier?** No provider was ever chosen — the stack rationale names
   an ORM only as an ecosystem strength.~~ **Resolved 2026-09-10 in `F-02`:** EF Core against
   **Azure SQL, S0 provisioned**, in `polandcentral` beside the app. The tier is the decision, not
   the product: the risk register rules out auto-pausing tiers because the first query after an idle
   period can exceed the whole 2-second acknowledgement budget, and the budget posture records that
   a paid tier inside the existing credit costs nothing extra since unspent credit expires
   worthless. S0 is the cheapest provisioned tier that removes that risk. A second Basic database
   serves local development, so a `Database.Migrate()` on the boot path cannot let a working-tree
   migration reach the database the live site serves from, and the Data Protection key ring — which
   has no per-application partition — stays off development machines. The connection string reaches
   the app as a Key Vault reference resolved through the site's system-assigned identity, so it is
   in neither the repository nor `infra/main.bicep`. Verified on the deployed instance: the
   reference reported `Resolved`, a probe page wrote and read across a container restart, and the
   first migration was applied on the boot path to a database that had zero tables. (That probe page
   was retired by `S-01` on 2026-09-12, once an auth cookie surviving a restart became the stronger
   version of the same check.) Owner: user.
   Was blocking: `F-02`, and through it every persisting slice.
2. ~~**Which model provider generates the candidates?** No client, package, or configuration
   exists.~~ **Resolved 2026-09-13 in `S-02`: Google Gemini on the free tier, reached through its
   OpenAI-compatible endpoint with the official `OpenAI` .NET package.** Chosen because it is free
   at this product's scale, supports JSON-schema structured output together with streaming (both
   established by a throwaway call before the component depended on either), and leaves the provider
   a base-URL-and-model change away from a paid one. The model is **configuration, not a constant**,
   which is what let the answer become an ordered rotation rather than a single string when the
   quota turned out to bind.
   **Two things the question did not anticipate.** The free tier allows **20 requests per day per
   model**, measured from a `429` body rather than documentation, which no longer publishes limits
   at all — so `Gemini:Models` lists `gemini-3.8-flash` first for quality and two `flash-lite`
   models at 500/day behind it, falling through on `429`, `404` or any `5xx`. And the earlier note
   that "low request volume does not imply low cost" turned out to be the wrong axis of worry here:
   cost is zero and **availability** is the constraint.
   **The accepted risk this carries:** free-tier content may be used for model improvement. The
   passage is the learner's own pasted text, and nothing in the product promises otherwise today.
   Revisit if the product ever takes content it does not own.
3. ~~**How long is the inactivity window before a signed-in session expires?** Carried verbatim from
   the PRD's `## Open Questions`.~~ **Resolved 2026-09-12 in `S-01`: seven days, sliding.**
   `ConfigureApplicationCookie` sets `ExpireTimeSpan` to seven days with `SlidingExpiration` true, so
   the window renews on activity rather than counting from sign-in. **Sliding is the load-bearing
   half, not the number.** This product has no password recovery by decision, so a forgotten password
   is a permanently dead account: an expiry that signs an active learner out buys no security worth
   having and risks costing them the one credential they hold. Seven days is then chosen for the
   usage the PRD describes — a learner who reads something worth carding every few days should not
   meet a login screen each time — and it is short enough that a stolen cookie on a shared machine
   does not outlive the person who left it there. The value is asserted by a test rather than left to
   the comment beside it, because a later agent restoring Identity's defaults would otherwise be a
   silent change. Owner: user. Was blocking: `S-01`, non-blocking by the PRD's own classification.
4. ~~**Should the application keep its own HTTPS redirect, or rely entirely on the platform's
   HTTPS-only enforcement?**~~ **Resolved 2026-09-08 in `F-01`:** rely entirely on the platform.
   `app.UseHttpsRedirection()` was removed from `Program.cs`, deliberately rather than as a
   drive-by — the .NET 10 Blazor template ships that line itself, so this is a considered deletion
   from freshly generated code. The coupling concern that kept the question open is answered by
   *where* the enforcement is declared: the `site` resource in `infra/main.bicep` sets
   `httpsOnly: true`, so it lives
   in the infrastructure source of truth rather than in a CLI flag someone once typed. `UseHsts()`
   is kept, because HSTS and `httpsOnly` cover different moments — the platform redirects after a
   plain-HTTP request has been made, HSTS stops the browser making it. Verified on the deployed
   instance: the `HttpsRedirectionMiddleware[3]` startup warning is gone. Anyone deploying this app
   to an environment that cannot enforce HTTPS at the platform inherits the obligation to
   reinstate it. Owner: user. Was blocking: `F-01`.

## Parked

- **Spaced-repetition review** — Why parked: PRD `## Non-Goals`. The largest scope decision taken
  during shaping, deferred because it sits past the point where value lands.
- **A proprietary scheduling algorithm** — Why parked: PRD `## Non-Goals`. An existing one is
  integrated when scheduling arrives, rather than invented.
- **Password recovery** — Why parked: PRD `## Non-Goals`. A forgotten password means a dead account in
  this version; recorded as a deliberate decision rather than an oversight.
- **Resuming an abandoned triage batch** — Why parked: PRD `## Non-Goals`. Persisting untriaged
  candidates conflicts with the guarantee that a submitted passage is not retained.
- **Importing cards from files** — Why parked: PRD `## Non-Goals`. Pasted text is the only input.
- **Sharing cards between learners** — Why parked: PRD `## Non-Goals`. Every space is private and the
  flat user model has no mechanism for it.
- **Integration with other educational platforms** — Why parked: PRD `## Non-Goals`.
- **Decks, tags, and organization** — Why parked: PRD `## Non-Goals`. Cards live in one flat space.
- **Export** — Why parked: PRD `## Non-Goals`. The consequence is accepted knowingly: until scheduling
  arrives, a curated set cannot be studied anywhere else.
- **Any phone or tablet commitment** — Why parked: PRD `## Non-Goals`. Desktop web only.
- **Telemetry beyond the platform's own logs** — Why parked: no requirement asks for it, and under a
  speed bias it is the kind of work that looks productive without moving a must-have requirement.
  Revisit if a failure proves undiagnosable from the existing filesystem logs.
- **Scaling past one worker** — Why parked: the recorded risk analysis puts thousands of concurrent
  circuits inside this tier's headroom at the PRD's stated scale, and session affinity cannot fail at
  a single instance. Revisit only if concurrent triage sessions reach the low tens.

## Milestone History

(Append-only. Carried forward verbatim into each successor milestone's roadmap; empty on the very
first milestone. Closure entries are written when a milestone closes.)

## Done

(Empty on first generation. `/10x-archive` appends an entry here — and flips that item's `Status` to
`done` — when a change whose `Change ID` matches the item is archived.)

- **F-01: (foundation) the deployed app serves an interactive Blazor Server page over a live
  circuit, and the template sample route is gone.** — Archived 2026-09-12 → `context/archive/2026-09-08-blazor-server-shell/`. Lesson: —.
- **F-02: (foundation) the deployed app reads and writes a provisioned database, with the
  connection string set outside infrastructure-as-code and a repeatable migration path in place.** —
  Archived 2026-09-12 → `context/archive/2026-09-08-persistence-spine/`. Lesson: —.
- **F-03: (foundation) a merge to the main branch builds, packages, and deploys the app, and the
  deployable artifact is retained.** — Archived 2026-09-12 → `context/archive/2026-09-08-deploy-pipeline/`. Lesson: —.
- **S-01: user can register with an email address and a password, sign in with the same pair,
  and sign out — and everything they create belongs to that account and is visible to nobody else.** —
  Archived 2026-09-12 → `context/archive/2026-09-12-accounts-and-sessions/`. Lesson: —.
- **S-04: user can locate one of their own saved cards and either correct it or remove it.** —
  Archived 2026-09-13 → `context/archive/2026-09-13-manage-saved-cards/`. Lesson: —.
