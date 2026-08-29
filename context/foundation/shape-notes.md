---
project: "10xCards"
context_type: greenfield
created: 2026-08-22
updated: 2026-08-22
checkpoint:
  current_phase: 8
  phases_completed: [1, 2, 3, 4, 5, 6, 7]
  gray_areas_resolved:
    - topic: "pain category"
      decision: "quality gap, not speed — cards bundle too much and are copied verbatim"
    - topic: "insight / why now"
      decision: "built for the author's own workflow first; generalization deferred"
    - topic: "primary persona scope"
      decision: "self-directed adult learner"
    - topic: "access model"
      decision: "login with email + password; per-learner personal space from day one"
    - topic: "role separation"
      decision: "flat — every account identical, no admin surface in MVP"
    - topic: "MVP scope"
      decision: "scoped down — review session and separate card manager cut to v2; accounts and manual creation kept"
    - topic: "payoff point"
      decision: "value lands at accepted cards saved, not at a completed review"
    - topic: "secondary criterion"
      decision: "low edit rate on accepted cards — high edits mean generation is not landing"
    - topic: "authentication mechanism"
      decision: "email + password with no recovery path; magic-link considered and rejected on cost and deliverability grounds"
    - topic: "hard constraints"
      decision: "user accounts are a non-negotiable requirement; recurring paid services are to be avoided"
    - topic: "triage granularity"
      decision: "one candidate at a time, no bulk-accept affordance"
    - topic: "abandoned-batch handling"
      decision: "untriaged candidates are discarded at session end; resume-triage dropped in the Socratic round because it conflicted with the retention guardrail"
    - topic: "saved-card list purpose"
      decision: "the list serves finding a card to edit or delete, not browsing"
    - topic: "card immutability"
      decision: "saved cards stay editable in v1; immutability revisited when the scheduler lands in v2"
    - topic: "domain rule ownership"
      decision: "quality is specified by the product, not delegated to whatever generates the cards"
    - topic: "generation steering"
      decision: "passage plus an optional focus hint"
    - topic: "batch size"
      decision: "proportional to passage length, capped"
    - topic: "input ceiling"
      decision: "about an article or book section — a few thousand words"
    - topic: "product type"
      decision: "web app, desktop browsers only — carried from the seed notes"
    - topic: "target scale"
      decision: "dozens to a hundred learners"
    - topic: "stranded cards"
      decision: "no export in v1; cards wait in the personal space until the v2 scheduler lands"
    - topic: "session lifetime"
      decision: "expires after a period of inactivity"
    - topic: "card immutability in v2"
      decision: "a card becomes immutable once it has been reviewed for the first time"
    - topic: "generation time ceiling"
      decision: "30 seconds for a full-length submission, then it must report failure"
    - topic: "competitive justification"
      decision: "the consistently enforced specification of card quality is the differentiator, not the act of generating text"
    - topic: "measurement"
      decision: "added after the session — triage outcomes and card origin must be recorded so the rates can be determined; recording only, no in-product surface"
  frs_drafted: 13
  quality_check_status: accepted
---

# Shape Notes

## Seed idea (verbatim, from idea-notes.md)

### Main problem
Manually creating high-quality flashcards is time-consuming, discouraging people from using the effective learning method of spaced repetition.

### Minimal functional requirements
- Generating flashcards by AI based on copy-pasted input
- Manual flashcard creation
- Reviewing, editing and removal of flashcards
- Simple account system so that each user has their personal space
- Integration with an existing spaced repetition algorithm

### What is NOT part of the MVP
- Proprietary spaced repetition algorithm
- Importing flashcards from files (e.g. pdf, docx)
- Sharing flashcards between users
- Integration with other educational platforms
- Mobile app (just desktop web UI for MVP)

### Success criteria
- 75% of the AI-generated flashcards are accepted by the user
- Users create 75% of the flashcards using AI.

---

## Vision & Problem Statement

A self-directed adult learner decides to study something they have just read, opens a
blank card editor, and stalls. They do not know which facts are worth carding or how to
phrase them. When they push through anyway, the cards they produce bundle several facts
onto one card and reproduce the source text verbatim. Weeks later those cards test
recognition rather than recall, reviews feel like punishment, and the learner abandons
spaced repetition — a method that demonstrably works — because their own inputs to it
were bad.

The pain is quality, not speed. This is a deliberate reframe of the original note, which
described the problem as "time-consuming". Faster card production that preserves the
same defects would not solve this problem. The insight is personal rather than market-
shaped: the author is the primary learner, and the product is built to fix the author's
own card-making failure first. Whether it outperforms the existing ChatGPT-plus-Anki
path for a broader audience is treated as an open question, not a founding assumption.

> Socratic (Phase 1): Anki has existed since 2006, Quizlet ships AI generation, and a
> learner can paste a chapter into a general chat model and get cards in four minutes.
> Resolution: a general chat model produces cards but holds no consistent, checkable
> definition of a good card across batches. The differentiator is the specification in
> `## Business Logic`, enforced identically every time — not the act of generating text.

## User & Persona

Primary persona: a **self-directed adult learner**. No institution assigns their
material, no external deadline paces them, and nobody checks whether they studied. They
choose their own sources — articles, documentation, book chapters, course notes — and
they are the only person who will ever see their cards.

The moment they reach for this product is immediately after deciding to study: material
in hand, intent present, blank editor open, no idea what to write. Their failure mode is
not laziness but formulation — they cannot convert prose into good retrieval prompts, and
they have no feedback loop telling them their cards are badly formed until the reviews
already hurt.

## Success Criteria

### Primary
- At least 75% of AI-generated candidate cards are accepted by the learner rather than
  rejected.
- At least 75% of the cards in a learner's space were produced by generation rather than
  typed manually.
- The end-to-end flow completes: a learner who has never used the product can arrive,
  register, paste a source text, and finish with saved cards in their personal space.

### Secondary
- Fewer than a quarter of accepted cards are edited before saving. A high edit rate means
  generation is not landing and the learner is doing the formulation work anyway — which
  is the exact pain the product exists to remove.

### Guardrails
- Source text submitted for generation is not retained after the cards derived from it
  have been produced.
- No accepted card is ever silently lost — not to a failed generation, an interrupted
  session, or a page refresh.
- Generation never blocks without feedback: the learner always sees progress, and a
  failure says so while leaving the pasted text recoverable.
- Rejecting a candidate is no harder than accepting one. Triage must not bias toward
  acceptance, or the 75% acceptance criterion measures the interface rather than the
  cards.

## User Stories

### US-01: Learner turns a source text into saved cards

- **Given** a signed-in learner holding a passage they have just read
- **When** they paste that passage into the form and submit it for generation
- **Then** they are shown candidate cards one at a time, and every candidate they accept is
  saved to their personal space

#### Acceptance Criteria
- Each candidate is presented on its own, with accept, reject, and edit offered at equal
  prominence and equal effort.
- An accepted card is saved immediately and survives a page refresh or a lost connection.
- Rejecting a candidate discards it without an extra confirmation step, so rejecting is
  never more work than accepting.
- The pasted source text is discarded once generation has completed.
- Candidates left untriaged when the session ends are discarded, and the learner is not
  led to believe otherwise.
- A generation failure reports itself rather than hanging, and leaves the pasted text
  recoverable so the learner does not have to find and copy it again.
- A submission longer than the input bound is refused before generation begins, not after.

## Functional Requirements

### Access
- FR-001: Learner can register an account with an email address and a password. Priority: must-have
  > Socratic: Counter-argument considered: "the email gate kills the demo moment — the
  > learner must hand over an email before seeing a single card." Resolution: rejected; the
  > FR stands. Accounts are a hard requirement, so an anonymous first paste is not
  > available as a trade.
- FR-002: Learner can sign in with their email address and password. Priority: must-have
  > Socratic: Counter-arguments considered: "a password with no recovery is a trapdoor —
  > every account is one lapse from permanently dead", and "you now own credential storage,
  > and people reuse passwords". Resolution: rejected; the FR stands. The absence of
  > recovery is recorded as an explicit non-goal rather than left implicit.
- FR-003: Learner can sign out. Priority: must-have
  > Socratic: Counter-argument considered: "sign-out is a shared-device requirement in
  > disguise — if the learner uses a machine someone else touches, that implies session
  > timeout behaviour too, which is more than a button." Resolution: accepted as a real
  > implication, and resolved: a signed-in session expires after a period of inactivity
  > rather than lasting indefinitely. The exact inactivity window is left to planning.

### Generation & triage
- FR-004: Learner can paste source text, optionally with a focus hint, and submit it for card generation. Priority: must-have
  > Socratic: Counter-argument considered: "unbounded input is a cost and latency trap —
  > nothing stops a learner pasting a whole book, and a long wait breaks the
  > never-blocks-blind guardrail." Resolution: accepted; an input length bound is required,
  > captured as a quality property in Non-Functional Requirements.
- FR-005: Learner can review the resulting candidate cards one at a time. Priority: must-have
  > Socratic: Counter-argument considered: "one-at-a-time hides redundancy — the learner
  > cannot see that two candidates ask the same thing in different words, and duplicates in
  > a review deck are exactly the punishment that made people quit." Resolution: accepted;
  > one-at-a-time is kept to protect triage neutrality, and avoiding duplicate candidates
  > within a batch is pushed onto the generation rule in Business Logic.
- FR-006: Learner can accept a candidate card, saving it to their space. Priority: must-have
  > Socratic: Counter-argument considered: "accept is the path of least resistance, so the
  > 75% acceptance target measures compliance rather than card quality." Resolution:
  > rejected; the FR stands as written.
- FR-007: Learner can reject a candidate card, discarding it. Priority: must-have
  > Socratic: Counter-argument considered: "discarding throws away the signal that would
  > explain why generation misses." Resolution: rejected; the FR stands as written.
- FR-008: Learner can edit a candidate card before accepting it. Priority: must-have
  > Socratic: Counter-arguments considered: "editing reintroduces the formulation work the
  > product exists to remove", and "it masks bad generation — the learner fixes weak cards
  > instead of rejecting them, so acceptance stays high while generation underperforms".
  > Resolution: rejected; the FR stands as written. The edit rate is tracked as the
  > Secondary success criterion precisely because of this risk.

### Saved cards
- FR-009: Learner can find a saved card in order to edit or delete it. Priority: must-have
  > Socratic: Counter-argument considered: "nobody browses a card deck — cards are consumed
  > through review, not by reading a list, so framing this as 'view every card' invents a
  > use nobody has." Resolution: accepted; the requirement was rewritten from "view every
  > card" to finding a card for the purpose of editing or deleting it.
- FR-010: Learner can edit a saved card. Priority: must-have
  > Socratic: Counter-arguments considered: "cards should be immutable once accepted, or a
  > card's review history no longer refers to the card that earned it", and "editing weeks
  > later, without the source text, is editing blind". Resolution: accepted in principle but
  > resolved — v1 has no review history to corrupt because the scheduler is out of scope, so
  > the FR stands unchanged for v1. From v2 onward a card becomes immutable once it has been
  > reviewed for the first time: it is freely editable until it enters the schedule, and
  > correcting it afterwards means replacing it rather than altering it.
- FR-011: Learner can delete a saved card. Priority: must-have
  > Socratic: Counter-arguments considered: "deletion without undo breaks the no-silent-loss
  > guardrail", and "deleting is the wrong response to a bad card — rewording usually
  > salvages it". Resolution: rejected; the FR stands. The guardrail forbids *silent* loss,
  > which a deliberate deletion is not.
- FR-012: Learner can create a card manually. Priority: must-have
  > Socratic: Counter-arguments considered: "it reopens the blank-editor failure mode named
  > in Phase 1", and "it exists only to keep the 75%-created-via-AI metric alive".
  > Resolution: rejected; the FR stands as written.

### Measurement
- FR-013: Product owner can determine the acceptance rate, the share of cards created by generation rather than manually, and the edit rate on accepted cards. Priority: must-have
  > Socratic: Counter-arguments considered: "recording rejections retains content derived
  > from a passage the product promised to discard — the argument that removed resume-triage
  > applies here too"; "the rates are noise at this volume, so the measurement cannot support
  > the conclusion it is built for"; and "knowing the acceptance rate is tracked biases the
  > learner toward accepting, which is the dynamic the triage-neutrality guardrail exists to
  > prevent". Resolution: rejected; the FR stands as written.
  >
  > Scope note: the requirement is that these outcomes are recorded, not that they are
  > displayed. It creates no in-product surface and no role, so it does not contradict the
  > absence of an operator view in `## Access Control`. An earlier draft framed this as a
  > learner-facing statistics panel; that was rejected as a screen built for an audience of
  > one who already has direct data access.

Dropped during the Socratic round: "Learner can return to candidates left untriaged in an
earlier session." It was demoted to nice-to-have and then dropped outright once it was
recognised as conflicting with the guardrail that source text is not retained. The
consequence is accepted: candidates left untriaged when a session ends are discarded, and
the learner re-pastes.

## Non-Functional Requirements

- A submitted passage is unrecoverable from storage once the candidate cards derived from
  it exist.
- The learner sees acknowledgement of a submission immediately, and continuous visible
  progress during any wait longer than two seconds. A frozen form is a defect.
- A submission at the maximum accepted length either yields candidates or reports failure
  within 30 seconds. There is no unbounded wait.
- An accepted card is durable from the moment of acceptance: it survives a page refresh, a
  closed tab, and a dropped connection.
- A single submission accepts up to approximately the length of an article or book section
  — a few thousand words. Longer submissions are refused before generation begins rather
  than after.
- The product is usable on the current versions of the mainstream desktop browsers. There
  is no phone or tablet commitment.

## Business Logic

**From a passage the learner supplies, produce a set of cards in which each card tests
exactly one load-bearing claim, phrased in different words from the source so that it has
a single unambiguous answer, and no two cards in the set test the same claim.**

The rule consumes two things the learner provides: the passage itself, and an optional
focus hint narrowing what they want carded ("the causes, not the dates"). It consumes
nothing else — in particular it gets a single pass at the passage, because the passage is
discarded once the cards derived from it exist and cannot be consulted again. Submissions
are bounded at roughly the length of an article or book section.

Its output is a set of candidate cards whose size is proportional to the length of the
passage, up to a cap, rather than a fixed number. Four properties are what make a
candidate worth showing at all, and each is checkable rather than aspirational: it tests
one fact and not several; it is reformulated rather than copied, so the learner retrieves
the fact instead of recognising a sentence; its prompt admits exactly one defensible
answer; and it does not repeat a claim already covered by another card in the same set.
Which claims get carded is itself part of the rule — load-bearing content such as
definitions, causal links, and distinctions the passage treats as central, not incidental
dates, names, or examples.

The learner encounters the rule as triage: candidates arrive one at a time, and each is
accepted, rejected, or edited before acceptance. The rule is what the acceptance rate
measures. Specifying card quality rather than leaving it to whatever produces the cards is
deliberate: without a stated definition of a good card, the 75% acceptance target would
have no lever behind it if it came in low, and both Phase 1 defects would reproduce
faithfully, since verbatim extraction is the path of least resistance for any passage. The
specification of card quality — not the act of generating text — is the part of this
product a learner could not obtain elsewhere in a few minutes.

## Access Control

Access is by account. The learner registers with an email address and a password, signs
in with the same pair, and everything they create belongs to that account and is visible
to no one else. There is no password-recovery path in v1; a learner who forgets their
password cannot regain access to that account. This is a decision rather than an
oversight, and it is recorded in `## Non-Goals`.

Two hard constraints shaped this. Accounts are non-negotiable — they are required
independently of whether learners asked for them — and recurring paid services are to be
avoided. Magic-link sign-in was chosen first and then reversed against those constraints:
emailed sign-in links need a sender identity that mail servers trust, which in practice
means a controlled domain with a recurring cost, plus the debugging burden when links are
filtered as spam. Email and password need neither, provided recovery is out of scope. The
lockout risk that motivated magic-link is small here: the author is the only real user and
has direct data access, and a second learner who loses a password loses a handful of
cards rather than years of work.

This decision may be worth revisiting once a stack is chosen, since some platforms bundle
both password and emailed-link authentication at no additional cost. That cannot inform
the decision now, because no stack has been selected.

The user model is flat. Every account has identical capabilities: create, review, edit,
and delete only its own cards. There are no roles, no sharing, and no operator or admin
view in the MVP, so there is no cross-account visibility of any kind — including for the
author. Anything an operator would want to know about generation quality has no UI surface
in this version.

> Socratic (Phase 2): with the product built for the author first, the smallest useful
> access model would be no auth at all. Resolution: rejected — accounts are a hard
> requirement, and per-learner separation lets a second learner use the product without a
> rebuild.

## Non-Goals

Carried from the seed notes:

- No proprietary spaced-repetition algorithm — an existing one is integrated when
  scheduling lands, rather than invented.
- No importing flashcards from files such as PDF or DOCX — pasted text is the only input.
- No sharing flashcards between learners — every space is private, and the flat user model
  has no mechanism for it.
- No integration with other educational platforms.
- No mobile app — desktop web only for the MVP.

Surfaced during this session:

- No password recovery — a forgotten password means a dead account in v1. Recorded as a
  decision, taken to avoid a recurring paid dependency, not as an oversight.
- No spaced-repetition review in v1 — the largest scope decision of this session,
  deferred to v2 because it sits past the point where value lands.
- No resuming an abandoned triage batch — untriaged candidates are discarded when the
  session ends, because persisting them conflicts with the retention guardrail.
- No decks, tags, or organization — cards live in one flat space, and the saved-card list
  exists to find a card for editing or deletion rather than to browse.
- No export — cards cannot leave the product in v1. The consequence is accepted knowingly:
  until the v2 scheduler arrives, a curated deck cannot be studied anywhere.

## Open Questions

1. **Session inactivity window** — sessions expire after a period of inactivity, but the
   length of that period is not fixed. Owner: user; a detail for planning rather than a
   product decision.

Resolved during the closing cross-check, and recorded in the sections they belong to:
session lifetime, card immutability from v2 onward, the 30-second generation ceiling, and
the competitive justification deferred in Phase 1.

## MVP flow (locked, Phase 3)

1. Learner lands on the site, having never used it before.
2. Registers with an email address and a password.
3. Signs in.
4. Pastes source text into a form.
5. Submits; candidate cards come back.
6. Triages each candidate: accept, reject, or edit before accepting.
7. Accepted cards are saved to the personal space. **← payoff**
8. A single list shows saved cards: view, delete, edit (reusing the triage editor),
   and create a card manually from the same place.

Deferred to v2: the spaced-repetition review session, and the card manager as a
surface separate from the saved-card list. Kept in v1 despite the scope-down:
accounts, and manual card creation.

## Timeline budget

- `mvp_weeks`: 3 (after the scope-down; within the default target, so no
  sustained-effort acknowledgment is required)
- `hard_deadline`: 2026-09-14
- `after_hours_only`: true

## Product framing

- Product type: web app. Desktop browsers only; mobile was ruled out in the seed notes and
  the browser-support commitment in Non-Functional Requirements matches.
- Target scale: dozens to a hundred learners.
- Timeline: three weeks of after-hours work against a fixed deadline of 2026-09-14, which
  is 23 days from the date of this session. Two days of slack against the estimate, and no
  scope buffer either — the sole nice-to-have was dropped during the Socratic round, so
  every remaining requirement is must-have.

> Socratic (Phase 6): asked how the domain rule would change at a hundred times that
> scale. Resolution: it would not — the rule operates on one learner's passage at a time
> and never looks across learners, so scale changes cost and infrastructure without
> touching the rule.

## Forward: tech-stack

Not part of the PRD. Captured here for whichever step selects the stack.

- Recurring paid services are to be avoided. This constraint has already changed one
  product decision: magic-link sign-in was reversed in favour of email and password
  specifically to avoid a sender domain and a transactional email dependency.
- User accounts are a non-negotiable requirement, independent of learner demand.
- Worth revisiting once a stack is chosen: some platforms bundle password and
  emailed-link authentication at no additional cost, which would make the auth decision
  nearly free either way and could reopen the password-recovery non-goal.
- Generation cost per submission is a live concern behind the input-length bound and the
  proportional-with-cap batch size.
- Reaching the application at a public URL is an optional but desirable outcome, not a
  requirement. It is a real input to stack and hosting selection all the same, because it
  interacts with the no-recurring-cost constraint above: a stack that is pleasant to
  develop but cannot be published for free would be a poor fit.

## Forward: technical-roadmap

Not part of the PRD. Testing and deployment decisions sit downstream of stack selection;
captured here so the step that makes them does not have to rediscover the constraint.

- **At least one test written from the user's perspective is a hard external acceptance
  requirement.** It must exercise the product the way a learner would rather than testing
  a unit in isolation. This is not a preference to be traded away under deadline pressure.
  The acceptance criteria under `## User Stories` (US-01) are already written at that
  altitude and are the natural basis for it.
- The mandatory test is an input to stack selection, not only to planning: frameworks
  differ sharply in how much assembly an end-to-end test requires, and that difference
  should count when the stack is chosen.

## Quality cross-check

Run at the close of the session. All six gate items passed with no gaps:

- **Access Control** — present. Email and password, flat user model, no recovery path,
  with the absence recorded as a deliberate non-goal.
- **Business Logic** — present. A single declarative rule, four checkable card properties,
  and a stated selection principle.
- **Project artifacts** — present.
- **Timeline-cost acknowledged** — present. Three weeks, within the default target, so no
  sustained-effort acknowledgment was required. Noted separately: the fixed deadline of
  2026-09-14 leaves two days of slack and no scope buffer.
- **Non-Goals** — present. Ten entries, five carried from the seed notes and five surfaced
  during this session.
- **Preserved behavior** — not applicable; this is a greenfield session.

`quality_check_status: accepted`.
