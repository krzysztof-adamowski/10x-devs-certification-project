---
project: "10xCards"
version: 1
status: draft
created: 2026-09-02
updated: 2026-09-08
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
| F-01 | `blazor-server-shell`    | (foundation) the deployed app serves an interactive Blazor page  | —                | NFR (2s acknowledgement), NFR (desktop browsers)        | in-progress |
| F-02 | `persistence-spine`      | (foundation) the deployed app reads and writes a real database   | —                | NFR (accepted card durable), Guardrail (no silent loss) | ready    |
| F-03 | `deploy-pipeline`        | (foundation) a merge to main deploys without hand-built archives | F-01             | NFR (2s acknowledgement)                                | proposed |
| S-01 | `accounts-and-sessions`  | register, sign in, and sign out of a private account             | F-01, F-02       | FR-001, FR-002, FR-003, Access Control                  | proposed |
| S-02 | `passage-to-saved-cards` | paste a passage and finish with accepted cards saved             | S-01             | FR-004, FR-005, FR-006, FR-007, US-01, Business Logic   | proposed |
| S-03 | `edit-before-accepting`  | fix a candidate's wording before accepting it                    | S-02             | FR-008, US-01                                           | proposed |
| S-04 | `manage-saved-cards`     | find a saved card in order to edit or delete it                  | S-02             | FR-009, FR-010, FR-011                                  | proposed |
| S-05 | `manual-card-entry`      | write a card by hand without generating one                      | S-02             | FR-012                                                  | proposed |
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
  `polandcentral` with HTTPS-only and Always On, and the site is live. But deployment is manual CLI:
  there is no `.github/` directory at all, so zero CI workflows exist despite `tech-stack.md`
  declaring auto-deploy-on-merge as a decision. No container definition.
- **Observability:** partial (platform-only) — App Service filesystem logs at `Information`, retained
  3 days / 100 MB (`infra/main.bicep:165`). No telemetry service, no workspace, no health endpoint,
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
- **Unknowns:**
  - Should the application keep its own HTTPS redirect, or rely entirely on the platform's
    HTTPS-only enforcement? — Owner: user. Block: no.
- **Risk:** Sequenced first because nothing renders without it. The failure mode is scope creep —
  this establishes the host and nothing else, and every slice still builds its own surface. The
  secondary risk is carrying the scaffold's incidental decisions forward untouched while rewriting
  around them.
- **Status:** in-progress

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
  - Which database provider and tier? The infrastructure risk register rules out auto-pausing tiers,
    because the first query after an idle period can exceed the entire 2-second acknowledgement
    budget — Owner: user (resolved downstream as course-work). Block: no.
- **Risk:** Deliberately designs no schema — identity tables arrive with `S-01`, the card entity with
  `S-02`. Two recorded traps sit here: an auto-pausing tier silently breaks the acknowledgement
  requirement, and declaring application settings inside infrastructure-as-code makes a routine,
  successful-looking deployment delete the connection string.
- **Status:** ready

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
- **Parallel with:** S-01
- **Blockers:** —
- **Unknowns:** —
- **Risk:** The only foundation here not strictly required before the next slice — manual deployment
  already works. It earns its place on repetition: with six slices left and a deadline twelve days
  out, every one of them gets deployed and verified, and the archive-shape trap is a once-per-deploy
  chance to lose an evening. If time compresses, this is the first foundation to Park.
- **Status:** proposed

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
  - How long is the inactivity window before a signed-in session expires? Carried from the PRD's
    `## Open Questions`, which classes it as a planning detail rather than a product decision —
    Owner: user. Block: no.
- **Risk:** Identity's signing keys are not persisted by default, and the failure this causes looks
  like a scaling problem while actually biting at a single instance: every container restart —
  deploy, platform maintenance, recycle — logs every user out and starts rejecting form submissions.
  The repository rule is that key persistence ships in the same change that adds identity, so it
  belongs here rather than in a later hardening pass. This is also the first slice to touch
  persistence, so the test project is created here and its tests ship alongside this code.
- **Status:** proposed

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
  learner's perspective attaches here, with US-01's acceptance criteria as its basis. If this slice
  slips, the milestone slips.
- **Status:** proposed

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
- **Status:** proposed

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
- **Status:** proposed

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
- **Status:** proposed

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
| F-01       | `blazor-server-shell`    | Replace the API scaffold with an interactive Blazor Server shell | yes                   | Run `/10x-plan blazor-server-shell`                   |
| F-02       | `persistence-spine`      | Stand up a provisioned database reachable from the deployed app  | yes                   | Run `/10x-plan persistence-spine`; parallel with F-01 |
| F-03       | `deploy-pipeline`        | Deploy automatically on merge to main                            | no                    | Needs F-01                                            |
| S-01       | `accounts-and-sessions`  | Register, sign in, and sign out of a private account             | no                    | Needs F-01 and F-02                                   |
| S-02       | `passage-to-saved-cards` | Paste a passage and finish with accepted cards saved             | no                    | Needs S-01 — north star                               |
| S-03       | `edit-before-accepting`  | Edit a candidate card before accepting it                        | no                    | Needs S-02                                            |
| S-04       | `manage-saved-cards`     | Find a saved card in order to edit or delete it                  | no                    | Needs S-02                                            |
| S-05       | `manual-card-entry`      | Create a card manually                                           | no                    | Needs S-02                                            |
| S-06       | `outcome-recording`      | Record triage outcomes and card origin                           | no                    | Needs S-03, S-04, S-05                                |

This table is the clean handoff to Jira/Linear or any MCP-backed backlog. It should be compact enough
to copy into issues, but it must not duplicate the detailed roadmap body.

## Open Roadmap Questions

1. **Which database provider and tier?** No provider was ever chosen — the stack rationale names an
   ORM only as an ecosystem strength. The infrastructure risk register rules out auto-pausing tiers,
   because the first query after an idle period can exceed the whole 2-second acknowledgement budget;
   the budget posture also records that a paid tier inside the existing credit costs nothing extra,
   since unspent credit expires worthless. Owner: user — to be resolved downstream as course-work.
   Block: `F-02`, and through it every persisting slice. Non-blocking by the user's explicit decision.
2. **Which model provider generates the candidates?** No client, package, or configuration exists.
   Note that low request volume does not imply low cost — generation is expensive per request
   regardless of how rarely it occurs. Owner: user — to be resolved downstream as course-work.
   Block: `S-02`. Non-blocking by the user's explicit decision.
3. **How long is the inactivity window before a signed-in session expires?** Carried verbatim from the
   PRD's `## Open Questions`. Owner: user. Block: `S-01`. The PRD classes it as a planning detail
   rather than a product decision, so it does not gate the roadmap.
4. **Should the application keep its own HTTPS redirect, or rely entirely on the platform's HTTPS-only
   enforcement?** Recorded as explicitly undecided: removing it couples the app's HTTPS posture to
   that platform setting staying on in every environment it is ever deployed to, so it should be a
   deliberate change rather than a drive-by during a rewrite. Owner: user. Block: `F-01`.

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
