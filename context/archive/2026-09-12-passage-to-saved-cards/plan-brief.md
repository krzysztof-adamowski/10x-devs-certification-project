# Passage to Saved Cards — Plan Brief

> Full plan: `context/changes/passage-to-saved-cards/plan.md`

## What & Why

`S-02` is the roadmap's **north star** — the smallest end-to-end flow that proves the product's
central bet, that a stated, enforced definition of card quality produces cards a learner keeps. A
signed-in learner pastes a passage with an optional focus hint, watches bounded progress, triages
candidates one at a time, and every accepted card is saved immediately to their own account. It
implements FR-004 through FR-007, US-01 and the PRD's `## Business Logic`. Everything else in the
milestone only matters if this works.

## Starting Point

A deployed Blazor Web App with per-page interactivity, EF Core against Azure SQL, a live account
boundary from `S-01`, an encrypted Data Protection key ring, a test suite that gates the deploy, and
a pipeline that deploys every push to `main`. Missing: **any LLM client** (no package, no config, no
abstraction), **any product entity** (`AppDbContext` holds Identity's tables and the key ring only),
and **any interactive page** — every component in the repository is statically rendered.
`ApplicationUser` was created deliberately empty in `S-01` naming this slice as the reason.

## Desired End State

A learner opens `/generate`, pastes up to 12,000 characters, and within two seconds sees progress
backed by evidence the model is still working. Candidates appear one at a time with accept and reject
at equal prominence; each accepted card is written to the database scoped to that learner before the
next candidate appears. The passage is gone from memory the moment candidates exist. After the last
candidate the learner sees how many were saved and discarded, and a fresh paste box. A failure says
so and hands the passage back.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Model provider | Google Gemini `gemini-3.8-flash`, free tier, via its OpenAI-compatible endpoint | Free at this product's scale with JSON-schema structured output and streaming; resolves Open Roadmap Question 2. |
| Client library | Official `OpenAI` NuGet 2.13.0 with a custom `Endpoint` | A typed .NET client, and moving to a paid provider later becomes a base-URL and model-string change. |
| Output shape | JSON-schema structured output | "The model returned prose" stops being a failure mode to code around; the schema is the contract `S-03`/`S-06` extend. |
| Progress | Stream for liveness, parse only the final message | Progress backed by real evidence the model is producing, rather than a spinner that shows the same thing when the connection is dead. |
| Failure & timeout | Hard cancel at the configured ceiling, report, restore the passage into the textarea | Implements US-01's "leaves the pasted text recoverable" with one code path for timeout and API error. |
| 30-second ceiling | A configured value, explicitly a target | The user recorded it may be loosened if unattainable — but loosening it in production amends a PRD non-functional requirement. |
| Passage bound | **12,000 characters** (~2,000 words), enforced at counter, control and handler | Characters are deterministic and countable in the browser, so "refused before generation begins" is one rule at three layers. |
| Candidate count | Code computes ~1 per 200 words, clamped `[3, 12]`; set re-clipped after parsing | A deterministic rule a test can assert, and an over-producing model cannot grow the circuit's memory. |
| Batch state | Circuit-only, plus a `beforeunload` warning | The only option compatible with "never persist untriaged candidates"; the warning turns a silent loss into a deliberate one. |
| Deduplication | Prompt rule **plus** a normalised-text check in code | `TenExCards.Tests/AGENTS.md` names "duplicate candidates are dropped" as a rule to test, and a prompt alone is untestable. |
| `Card` entity | Minimal plus an `Origin` enum (`Generated`/`Manual`) | `S-05` cannot exist without it; one column now beats a backfill migration on a forward-only boot path. |
| Surface | One gated page at `/generate`, `@rendermode InteractiveServer`, five states | The batch lives in component state, so navigating between routes would need a scoped service that outlives the page. |
| Prompt location | A `const string` in `CardGenerationPrompt.cs` | The PRD calls this specification the product; in code it is reviewed, diffed and asserted by a test. |
| Completion | A summary with counts, then a fresh form | The visible half of the no-silent-loss guardrail — and `S-04`'s card list does not exist to link to. |
| Test approach | Push every rule below the component into pure services | `@rendermode InteractiveServer` cannot be driven by the HTTP harness, and adding one would widen a deliberately narrow test-double rule. |

## Scope

**In scope:** the `Card` entity, its migration and an account-scoped store; the Gemini client behind
`ICardCandidateGenerator`; the card-quality prompt in version control; the length guard, target/cap
and deduplication rules with tests; a gated interactive `/generate` page carrying the form, progress,
triage loop and summary; the unload warning; nav and home-page entry points; live verification of the
2s and 30s bounds; and updating `AGENTS.md`, `deploy-plan.md` and the roadmap.

**Out of scope:** editing a candidate (`S-03`); finding, editing or deleting a saved card (`S-04`);
manual card entry (`S-05`); recording triage outcomes, acceptance rate, origin share or edit rate
(`S-06`); persisting untriaged candidates or resuming a batch; persisting the passage anywhere;
decks, tags, sharing, export; a second data store or background queue; deploying `infra/main.bicep`;
branch protection.

## Architecture / Approach

Built inside out. A `Card` entity and an `ICardStore` whose every member takes `ownerId` as a
required first parameter go in first, so the one forward-only migration lands while nothing depends
on it. The model sits behind `ICardCandidateGenerator` — the only seam the tests stub — with the
three deterministic rules as pure functions in their own types beside it, because an interactive
component cannot be driven by the HTTP harness and the rules must be testable without rendering
anything. The surface is a single gated `/generate` component with a five-state machine
(composing → generating → triaging → summary, plus failed), holding the batch in its own state and
calling `ICardStore.SaveAsync` on each accept before advancing. The generation call streams for
liveness, parses at the end, and returns a result value rather than throwing — a thrown exception in
a circuit surfaces as the generic Blazor error UI, which is the never-blocks-blind guardrail failing.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Card entity and account boundary | `Card` + `Origin`, migration, account-scoped store, ownership tests | Forward-only migration on the boot path with no deployment slots |
| 2. Generation client and quality spec | Vault secret + app setting, `OpenAI` 2.13.0, the prompt, the three rules, generator | The app setting must exist **before** this code merges, or the container does not serve |
| 3. Paste, generate, triage | The slice's actual outcome as one gated interactive page | First interactive circuit in the repo; passage-clearing and passage-retaining pull in opposite directions |
| 4. Deploy and verify live | Measured 2s acknowledgement and generation ceiling on B1 | The deploy verifier structurally cannot see content types or a broken generation path |
| 5. Update the repository record | `AGENTS.md`, `deploy-plan.md`, roadmap, PRD if the ceiling moved | Two files that must not both hold the same fact |

**Prerequisites:** `S-01` landed (it has). A Google AI Studio API key. Azure access to write a Key
Vault secret and an app setting. The development-machine firewall rule `dev-machine-krzychu` current
for your IP.

**Estimated effort:** ~4–5 sessions across five phases; Phase 3 is by far the largest.

## Open Risks & Assumptions

- **Free-tier content may be used for model improvement.** The PRD guardrail is about *this product*
  not retaining the passage, so it is not strictly violated — but a learner's pasted text leaves the
  system to a provider on a free plan, and that is a disclosure-shaped risk recorded rather than
  resolved.
- **Free-tier rate limits are per-account and no longer published in Google's docs.** The plan asserts
  no numbers. An exhausted limit presents as a generation failure, which the failure path handles —
  but it is not distinguishable from a provider outage to the learner.
- **Whether JSON-schema structured output works together with streaming on Gemini's
  OpenAI-compatible endpoint is unconfirmed.** Phase 2 settles it with one throwaway call before the
  component depends on either shape; the recorded fallback is non-streaming with elapsed-time
  progress.
- **The 30-second ceiling is a PRD requirement being treated as a target.** If Phase 4's measurement
  misses it, the timeout is raised and the PRD is amended in Phase 5 — the two must not end up
  silently disagreeing.
- **The component's own behaviour is manually verified only**, and manual checks decay. The
  mitigation is that almost nothing decidable lives in the component.
- **Blazor Server memory is per-user on a 1.75 GB tier with no back-pressure.** Every number here is
  bounded for that reason; a full batch is under 100 KB of application state and shrinks as triage
  proceeds.
- **Deployment trust is unchanged.** Anyone who can push to `main` can cause Azure to mint a
  `Contributor` token; branch protection is still absent and this slice does not add it.

## Success Criteria (Summary)

- A learner can paste a passage and finish with accepted cards saved to their own account, on the
  live deployment, without the form ever freezing.
- An over-length submission is refused **before** generation begins, and the passage is unrecoverable
  once candidates exist.
- An accepted card survives a refresh; an untriaged candidate does not, and the learner was told so.
- The suite asserts the length guard, the target/cap, the deduplication rule, the prompt's four
  quality properties, and that no card query crosses an account boundary.
