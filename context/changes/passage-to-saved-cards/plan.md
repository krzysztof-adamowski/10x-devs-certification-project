# Passage to Saved Cards Implementation Plan

## Overview

`S-02` is the roadmap's north star: the one slice whose success decides whether the product has a
reason to exist. A signed-in learner pastes a passage they have just read, optionally narrows it
with a focus hint, watches bounded and honest progress while it is worked on, reviews the resulting
candidate cards one at a time, and accepts or rejects each — with every accepted card saved
immediately to their own account and the submitted passage discarded once its candidates exist.

It implements FR-004, FR-005, FR-006, FR-007, US-01 and the PRD's `## Business Logic`, and it is the
first change in this repository to introduce an external model dependency, a product entity, and a
genuinely interactive Blazor circuit. It also resolves Open Roadmap Question 2.

## Current State Analysis

The application is a deployed Blazor Web App on `net10.0` with per-page interactivity, EF Core
10.0.12 against Azure SQL, ASP.NET Core Identity on a role-free `IdentityUserContext`, an encrypted
Data Protection key ring, a test project that gates the deploy, and a pipeline that deploys every
push to `main`.

What exists that this slice builds on:

- **An account boundary.** `S-01` landed register, sign in and sign out; authorization defaults to
  protected via a fallback policy in `Program.cs:88-93`, so a new page is gated unless it carries
  `[AllowAnonymous]`. Four surfaces carry it; none of them will be this one.
- **An empty `ApplicationUser`.** `TenExCards/TenExCards/Data/ApplicationUser.cs` was created
  deliberately empty in `S-01`, its XML doc naming `S-02`'s card-to-owner relationship as the reason.
- **A test harness that names this slice.** `TenExCards.Tests/AGENTS.md` says the LLM client becomes
  the only other test double when it arrives in `S-02`, and that from `S-02` onward every persisting
  slice must assert its own queries sit behind the account boundary.
- **A secret and app-setting pattern, with its ordering trap already paid for.**
  `context/deployment/deploy-plan.md` records that `DataProtection__KeyIdentifier` had to be set
  **before** the build reading it merged, because app settings are deliberately outside the pipeline
  while a push to `main` is a production deploy, on a tier with no deployment slots.

What is missing:

- **Any LLM client.** No package, no configuration, no abstraction. `TenExCards.csproj` carries EF
  Core, Identity, Data Protection and Azure.Identity only.
- **Any product entity.** `AppDbContext` holds Identity's four tables and `DataProtectionKeys`.
  `OnModelCreating` calls `base` and nothing else.
- **Any interactive page.** Every component in the repository is statically rendered. `S-01`
  deliberately left `AddInteractiveServerRenderMode()`'s endpoint builder un-anonymous, and
  `TenExCards/AGENTS.md` says to revisit that the first time an **anonymous** page uses
  `@rendermode InteractiveServer`. This slice's page is gated, so the revisit does not trigger.
- **Any measurement of the 2-second and 30-second bounds.** The roadmap notes these cannot be
  measured honestly against a local run — only a real circuit on the deployed B1 instance.

## Desired End State

A signed-in learner opens `/generate`, pastes up to 12,000 characters with an optional focus hint,
and submits. Within two seconds the form acknowledges and shows progress backed by evidence the
model is still producing. Candidates arrive as a set, are shown one at a time with accept and reject
at equal prominence and equal effort, and each accepted card is written to the database scoped to
that learner before the next candidate appears. The passage is gone from memory the moment
candidates exist. When the last candidate is triaged the learner sees how many were saved and how
many discarded, and a fresh paste box. A generation failure says so and hands the passage back.

Verified by: the deployed instance answering within the bounds on a maximum-length submission; a
card accepted on the live site still present in `sqldb-tenexcards` after a refresh; an anonymous
request for `/generate` producing a `302` to the login path; and the suite asserting the length
guard, the target/cap computation, the deduplication rule, the prompt's quality properties and
account-scoped queries.

### Key Discoveries:

- **The fallback authorization policy gates the new page for free** —
  `TenExCards/TenExCards/Program.cs:88-93`. Nothing needs to be added, and nothing must be added to
  the anonymous allowlist.
- **The render-mode `AllowAnonymous` trap does not trigger here.** `TenExCards/AGENTS.md`
  (`### Authorization defaults to protected`) leaves the interactive-render-mode builder
  un-anonymous precisely because no anonymous page uses a circuit. A gated page does not change that
  and must not be used as a reason to add a third `.AllowAnonymous()`.
- **App settings must exist before the build that reads them merges.**
  `context/deployment/deploy-plan.md` (`One app setting, set with az webapp config appsettings set,
  before the build that reads it was merged`) — the ordering is the whole point, and the cost of
  getting it wrong is a container that does not serve with no slot to swap back to.
- **`az keyvault secret set --file`, never `--value` and never as a command argument.** Windows
  PowerShell 5.1 appends every command line to `ConsoleHost_history.txt` indefinitely.
- **`Database.MigrateAsync()` runs on the boot path and is forward-only**
  (`TenExCards/TenExCards/Program.cs:144-168`). A migration reaches `sqldb-tenexcards-dev` first, and
  the `Down()` EF generates is not a rollback story.
- **`verify_deploy.py` cannot see content types.** It follows redirects, so a gated asset resolves
  `302 → /Account/Login → 200` and records a pass. Assets must be checked for `text/css` or a
  JavaScript type rather than `text/html`.
- **`OpenAI` on NuGet is at 2.13.0** (confirmed 2026-09-12 with `dotnet package search`, not copied
  from memory). It accepts a custom `Endpoint` in `OpenAIClientOptions`, which is what makes
  Gemini's OpenAI-compatible URL usable from a typed .NET client.
- **Gemini's OpenAI-compatible endpoint is
  `https://generativelanguage.googleapis.com/v1beta/openai/`** and the current Flash model string is
  `gemini-3.8-flash` (confirmed 2026-09-12 against `ai.google.dev/gemini-api/docs/openai`). Both
  JSON-schema structured output and streaming are documented there.
- **Google no longer publishes free-tier rate limits in its docs** — they are per-account in AI
  Studio. The plan therefore asserts no numbers; confirm yours before relying on them.
- **There is no JS-interop pattern in this repository to follow — `Generate.razor.js` will be the
  first.** `ReconnectModal.razor.js` looks like one and is not: it has **no `export` and no
  `import`**, and `ReconnectModal.razor:1` loads it as plain markup,
  `<script type="module" src="@Assets[...]">`. It wires DOM listeners at module scope and calls
  `Blazor.reconnect()` directly from JS. A repository-wide grep for `IJSRuntime` returns **zero
  hits**. So the unload warning is not "the same shape as the existing one" — it is the first C#
  side to reach into JS here, and the mechanics it needs are set out in Phase 3 §3.

## What We're NOT Doing

- **Editing a candidate before accepting it.** That is `S-03` (`edit-before-accepting`, FR-008). The
  triage surface offers accept and reject only.
- **Finding, editing or deleting a saved card.** That is `S-04` (`manage-saved-cards`). There is no
  card list, and the completion summary must not link to one.
- **Creating a card by hand.** That is `S-05` (`manual-card-entry`, FR-012). The `Origin` column
  exists so `S-05` is additive, but nothing writes `Manual` in this slice.
- **Recording triage outcomes, acceptance rate, origin share or edit rate.** That is `S-06`
  (`outcome-recording`, FR-013), and its constraint — that recording a rejection must not retain
  passage-derived content — is not settled. No rejection is persisted here.
- **The externally required test written from the learner's perspective — deferred to `S-03`, not
  dropped.** `context/foundation/roadmap.md` attaches it to *this* slice ("with US-01's acceptance
  criteria as its basis"), so moving it is a decision and is recorded as one here rather than left
  as an omission. Two reasons for `S-03` specifically. US-01's first acceptance criterion requires
  **accept, reject, and edit** at equal prominence, and edit is `S-03` — so a test written against
  US-01 can only assert it in full once that slice lands, and written here it would have to skip its
  own first criterion. And the tool is a browser-driven test, not the component-test framework
  `## What is deliberately not tested` argues against: that argument is about driving a circuit from
  the HTTP harness and does not reach this. Phase 5 §3 records the move in the roadmap, against the
  `S-03` entry, so it does not evaporate between slices.
- **Persisting untriaged candidates or resuming an abandoned batch.** Forbidden by
  `TenExCards/AGENTS.md` and dropped from the PRD outright.
- **Persisting the submitted passage anywhere, at any point, including in logs.**
- **Decks, tags, organization, sharing, export.** PRD `## Non-Goals`.
- **A second data store, a background job queue, or a cache.** The generation call is synchronous
  within the circuit.
- **Telemetry beyond the platform's own logs.** Parked in the roadmap.
- **Branch protection or narrowing the CI principal's `Contributor` role.** Still open, still out of
  scope, still worth remembering.
- **Deploying `infra/main.bicep`.** The Key Vault and the vault role assignment this slice needs
  already exist; only a new *secret* is added, and secret values are data-plane and never in the
  template.
- **Any phone or tablet commitment.** Desktop web only.

## Implementation Approach

The slice is built from the inside out: the irreversible thing first, the external dependency
second, the surface third.

**Phase 1 takes the migration while nothing depends on it.** A `Card` entity and an account-scoped
store go in ahead of anything that calls them, so the one forward-only step in this change is
rehearsed against `sqldb-tenexcards-dev` and merged before the code that would make a rollback
painful exists.

**Phase 2 puts the model behind an interface and the rules beside it.** `ICardCandidateGenerator` is
the only seam the tests stub, exactly as `TenExCards.Tests/AGENTS.md` anticipates. The three
deterministic rules the suite must assert — the length guard, the target-and-cap computation and the
deduplication check — are pure functions in their own types, not methods on the component, because a
`@rendermode InteractiveServer` component cannot be driven by the HTTP harness. That is a design
constraint, and it is the reason the component ends up thin.

**Phase 3 is one gated component with five states** — composing, generating, triaging, summary,
failed — because the batch lives in that component's own state and nowhere else. Navigating between
routes would dispose it, which is why the form and the triage loop share one page rather than two.

**Phase 4 measures on the deployed instance**, because the 2-second acknowledgement and the
generation ceiling cannot be measured honestly against a local run on different hardware over a
different network.

**Phase 5 writes the record back**, including the sentence in `TenExCards/AGENTS.md` that says the
LLM client is still missing.

## Critical Implementation Details

**Ordering: the app setting precedes the merge.** `Gemini__ApiKey` must be set on the App Service —
and confirmed `Resolved` — *before* Phase 2's commit reaches `main`. App settings are deliberately
outside the pipeline, a push to `main` is a production deploy, and the generator's options binding
fails at service configuration if the key is absent. The setting is inert to a build that does not
read it, so setting it early costs nothing and removes the window entirely. This is the same trap
`S-01` paid for with `DataProtection__KeyIdentifier`.

**Every phase commit deploys to production, and that risk is accepted here — on one premise.**
`.github/workflows/deploy.yml` triggers on every push to `main`, and this repository's history is
linear with one commit per phase, so each phase's commit *is* a production deploy. Phase 1's
forward-only `AddCards` migration therefore runs on the production boot path at the end of Phase 1,
and Phase 3's page is live before Phase 4 measures anything. **Phase 4 is where the deployment is
verified, not where it first happens.**

**No post-push production check is added to Phases 1–3, deliberately.** The accepted reason is that
this instance has **no users**: it is live on Azure, but nothing is lost if a phase's push leaves it
not serving until the next commit fixes it, and the cost of a failed boot is the implementer's time
rather than a learner's data. That premise is the entire justification, so **the first real learner
on this instance retires it.** From that point Phases 1–3 need the check `S-01` performed —
confirming the deploy from the startup log's migration line rather than from a `200`, per
`context/foundation/lessons.md` ("Verify a restart from the log, never from the first 200") and the
record in `context/deployment/deploy-plan.md` ("Each deploy's startup log reported applying exactly
one pending migration by name").

**State sequencing: the passage is cleared on success and retained on failure.** Two requirements
pull in opposite directions — the passage must be unrecoverable once candidates exist (PRD
`## Non-Functional Requirements`), and a failure must leave it recoverable so the learner does not
have to find and copy it again (US-01). The order is therefore: await the generator; on success,
null the passage field *before* entering the triage state; on failure, leave it populated and
re-render the composing state with an error. There is no intermediate state in which both are true.

**Lifecycle: acknowledge before awaiting, and dispose the JS module.** The state transition to
"generating" plus an explicit re-render must happen before the generation call is awaited, or the
2-second acknowledgement measures the model's latency rather than the form's. The unload-warning
module must be unregistered in `DisposeAsync`, and the component must tolerate the circuit being
gone when that runs.

**Observability: the deploy verifier cannot see this slice's failure modes.**
`scripts/verify_deploy.py` asserts status codes and follows redirects. It will record a pass for a
`/generate` page that `302`s to login, for an asset served as `text/html`, and for a generation path
that is entirely broken. Phase 4's manual checks are the only coverage for those.

---

## Phase 1: The card entity and the account boundary

### Overview

Introduces the product's first entity and the query boundary every later slice inherits. Taken first
because the migration is the one irreversible step in this change, and it is cheapest to rehearse
while nothing calls it.

### Changes Required:

#### 1. The card entity

**File**: `TenExCards/TenExCards/Data/Card.cs`

**Intent**: Give the product its first persisted entity, owned by an account, with the origin of the
card recorded from the start so `S-05` is additive rather than a backfilling migration against live
rows on a forward-only path.

**Contract**: A `Card` class with `Guid Id`, `string OwnerId`, `string Prompt`, `string Answer`,
`CardOrigin Origin` and `DateTimeOffset CreatedAt`; plus a `CardOrigin` enum in the same file with
**explicit numeric values** (`Generated = 1`, `Manual = 2`) so that reordering the members cannot
silently remap stored rows. `OwnerId` is the foreign key to `AspNetUsers.Id`, typed `string` to match
`IdentityUser.Id`. Only `Generated` is written by this slice.

#### 2. The context registration and model configuration

**File**: `TenExCards/TenExCards/Data/AppDbContext.cs`

**Intent**: Register the entity and configure the ownership relationship, so that deleting an account
takes its cards with it and every owner-filtered query is index-backed.

**Contract**: A `DbSet<Card> Cards` property following the existing `DataProtectionKeys` style, and
an `OnModelCreating` body — currently a bare `base` call — configuring: required `Prompt`
(max length 500) and `Answer` (max length 1000); a required `OwnerId` with an index; and a
one-to-many relationship from `ApplicationUser` to `Card` with cascade delete. Keep the `base` call
first; Identity's own configuration depends on it.

**Where 500 and 1,000 come from, since nothing else in this repository records a card length.**
They are deliberately generous rather than measured: a flashcard prompt that tests one load-bearing
claim runs well under 200 characters and an answer under 500, so these are roughly twice a typical
card — room for a long definition without room for a pasted paragraph. Two things depend on them, so
they are not free to change casually. They bound the circuit's memory budget in
`## Performance Considerations`, and **they are enforced on the model's output** (Phase 2 §5 and §6):
nothing else stands between a generated candidate and `SaveAsync`, and a truncation error there
throws inside a circuit event handler — the generic Blazor error UI, with the untriaged batch lost
behind it. The EF in-memory provider does **not** enforce `HasMaxLength`, so no test against the
factory can catch a regression here; the enforcement is tested as a pure function instead.

#### 3. The account-scoped store

**File**: `TenExCards/TenExCards/Cards/CardStore.cs` (and `ICardStore.cs` beside it)

**Intent**: Make the account boundary a property of the only type that touches the `Cards` table,
rather than a discipline every caller has to remember. This is the invariant
`TenExCards.Tests/AGENTS.md` calls "the invariant this project exists to protect".

**Contract**: `ICardStore` with `SaveAsync(string ownerId, string prompt, string answer, CardOrigin
origin, CancellationToken ct)` returning the persisted `Card`, and `CountForOwnerAsync(string
ownerId, CancellationToken ct)`. **`ownerId` is a required first parameter on every member** — there
is no ambient-user overload and no parameterless query — and every `Cards` query inside the
implementation filters on it. Registered scoped in `Program.cs` alongside the existing scoped
registrations. The caller obtains the id from the authentication state, never from a form field.

#### 4. The migration

**File**: `TenExCards/TenExCards/Migrations/<timestamp>_AddCards.cs` (+ designer + snapshot)

**Intent**: Create the `Cards` table.

**Contract**: Generated with `dotnet ef migrations add AddCards --project
TenExCards/TenExCards/TenExCards.csproj`. It must create exactly one table and one index and touch
nothing Identity owns — read the generated `Up()` before committing it. Forward-only: the `Down()`
EF writes is not a rollback story and is not relied on.

#### 5. Apply to the development database first

**File**: — (operational step, no file)

**Intent**: Prove the migration applies against real Azure SQL before it reaches the boot path of the
deployed app, where a throw means the container does not serve on a tier with no deployment slots.

**Contract**: Run the app locally with the `sqldb-tenexcards-dev` connection string from
`dotnet user-secrets` and let the boot-path migration apply it; confirm the table exists. The
development-machine firewall rule `dev-machine-krzychu` must be current for your IP first — it is
deliberately not in the template.

#### 6. Ownership tests

**File**: `TenExCards/TenExCards.Tests/CardOwnershipTests.cs`

**Intent**: Assert the boundary rather than trusting it, per `TenExCards.Tests/AGENTS.md`.

**Contract**: Against `TenExCardsWebApplicationFactory`'s in-memory database, with two users: saving
a card for user A then reading for user B returns nothing; `CountForOwnerAsync` counts only the
caller's rows; a saved card carries `Origin.Generated` and a non-default `CreatedAt`. Assertions use
AwesomeAssertions — never FluentAssertions.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build TenExCards/TenExCards.slnx`
- Tests pass: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
- The migration creates exactly one table and one index: read the generated `Up()` and confirm no
  Identity table is altered
- `CardOwnershipTests` fails when the owner filter is removed from `CardStore` (delete it, watch the
  test go red, restore it) — a boundary test that has never been observed failing is not a gate

#### Manual Verification:

- The migration applies cleanly to `sqldb-tenexcards-dev` via a local `dotnet run`, and the `Cards`
  table exists there with the expected columns and index
- Nothing in the entity, the store or the migration can hold a passage: there is no column for one

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation before proceeding.

---

## Phase 2: The generation client and the card-quality specification

### Overview

Brings in the external model dependency, states the card-quality rule in version control, and lands
the three deterministic rules as pure, testable types. Resolves Open Roadmap Question 2.

The provider is **Google Gemini (`gemini-3.8-flash`) on the free tier**, reached through Gemini's
OpenAI-compatible endpoint with the official `OpenAI` .NET package — chosen because it is free at
this product's scale, supports JSON-schema structured output and streaming, and leaves the provider
a base-URL-and-model change away from a paid one if quality or limits demand it.

### Changes Required:

#### 1. The API key, as a vault secret and an app setting — before this phase's code merges

**File**: — (operational step, no file; do this first)

**Intent**: Put the key where every other secret in this project lives, and get the app setting in
place before the build that reads it deploys itself.

**Contract**: A Google AI Studio API key stored as the vault secret `gemini-api-key` in
`kv-tenexcards-plc`, written with `az keyvault secret set --file` — never `--value`, never as a
command argument. Then one app setting via `az webapp config appsettings set`:

```
Gemini__ApiKey=@Microsoft.KeyVault(SecretUri=https://kv-tenexcards-plc.vault.azure.net/secrets/gemini-api-key/)
```

The **trailing slash** makes it versionless; omitting it changes the meaning rather than erroring,
and a malformed reference fails silently. Under Windows PowerShell 5.1 a leading `@` with
parentheses is array-subexpression syntax — quote the value. The site's identity already holds
`Key Vault Secrets User`, so no new role assignment is needed. Locally the same key comes from
`dotnet user-secrets set "Gemini:ApiKey" <key>`. Confirm resolution with the `az rest` GET against
`config/configreferences/appsettings` — `az resource show` answers `Not Found` on that collection
endpoint, indistinguishably from a real failure. Expect the first reading to be non-`Resolved`;
restart and read again.

#### 2. The client package

**File**: `TenExCards/TenExCards/TenExCards.csproj`

**Intent**: Add the typed .NET client.

**Contract**: `<PackageReference Include="OpenAI" Version="2.13.0" />` — the version confirmed with
`dotnet package search` on 2026-09-12, not copied from a plan. It is not part of the shared
framework and does not version with the `10.0.x` pins; do not "align" it with them. Add a comment
recording that the package name is `OpenAI` but the provider is Gemini, reached by base URL, so
nobody deletes it as a mistaken dependency.

#### 3. The card-quality specification

**File**: `TenExCards/TenExCards/Generation/CardGenerationPrompt.cs`

**Intent**: State the product's central rule in version control, where it is reviewed like code and
asserted by a test. The PRD says this specification *is* the product; leaving it in configuration
would put it outside review with no test behind it.

**Contract**: A static class holding a `const string System` — the system prompt — that states, in
the PRD's own terms: each card tests exactly one load-bearing claim; it is reformulated rather than
copied from the source; its prompt admits exactly one defensible answer; and no two cards in the set
test the same claim. It must also state what counts as load-bearing (definitions, causal links,
distinctions the passage treats as central — not incidental dates, names or examples), that the
passage is seen once and cannot be consulted again, and that the focus hint, when present, narrows
what gets carded. Derived from `## Business Logic` in `context/foundation/prd.md`; when that section
changes, this changes with it.

#### 4. The candidate contract and the generation result

**File**: `TenExCards/TenExCards/Generation/CandidateCard.cs`,
`TenExCards/TenExCards/Generation/GenerationResult.cs`

**Intent**: Name the shape the model returns and the shape a failure takes, so the component branches
on a value rather than catching exceptions.

**Contract**: `CandidateCard` is a record with `Prompt` and `Answer` strings. `GenerationResult`
carries either a `IReadOnlyList<CandidateCard>` or a failure, with a `GenerationFailure` enum of
`Timeout`, `ProviderError`, `Malformed` and `Refused`, plus a learner-facing message. **A failure is
returned, never thrown** — a thrown exception in a circuit surfaces as the generic Blazor error UI,
which is the "blocks without feedback" guardrail failing.

`GenerationProgress` is defined here too, since the generator's signature takes one: a record
carrying the count of streamed chunks received so far and nothing derived from their content. It
must not carry model text — the page renders it, and passage-derived content on screen after the
passage was cleared would be the disposal guarantee leaking. Under the non-streaming fallback the
generator reports it once on send, and the page's elapsed-time counter carries liveness alone.

#### 5. The deterministic rules

**File**: `TenExCards/TenExCards/Generation/PassageBounds.cs`,
`TenExCards/TenExCards/Generation/CandidateDeduplicator.cs`,
`TenExCards/TenExCards/Generation/CandidateBounds.cs`

**Intent**: Put the four rules the suite must assert into pure functions, so they are testable
without rendering a component.

**Contract**:
- `PassageBounds.IsWithinLimit(string passage, int maxCharacters)` — the length guard, counting
  characters. **12,000 characters** is the bound (~2,000 words), and this function is what the page,
  the submit button and the generator all consult, so "refused before generation begins" is one rule
  enforced at three layers rather than three rules that can disagree.

  **Why 12,000, and what would move it.** The PRD bounds a submission at "roughly the length of an
  article or book section — a few thousand words", so 12,000 characters sits at the *low* end of the
  stated range; the narrowing is deliberate and recorded here rather than left implicit. Raising it
  is pre-authorised in principle, because expected traffic is minimal — but **the provider's input
  limit is almost certainly not what constrains it.** A Flash-class model's input window is far
  larger than 12,000 characters (Phase 2's throwaway call is the cheap place to confirm the
  practical limit). The two constraints that actually bind are the PRD's **30-second ceiling**,
  which grows with input length because a longer passage yields more candidates to generate, and the
  **circuit memory budget** in `## Performance Considerations`. So a raise happens *after* Phase 4's
  measurement, sized against the measured headroom, and is re-measured — not decided in advance of
  it.
- `PassageBounds.TargetCandidateCount(string passage, GenerationOptions options)` — roughly one
  candidate per 200 words, clamped to `[3, 12]`. The result is passed into the prompt and the parsed
  set is re-clipped to the cap afterwards, so a model that over-produces cannot grow the circuit's
  memory beyond the budget. **The upper clamp is a defensive clip, not a reachable target**: at
  12,000 characters (~2,000 words) and one per 200 words the computation maxes at 10, so 12 is only
  ever reached by a model that over-produces. Test it that way — feeding the "cap" case a passage
  longer than the product accepts would assert against an input that cannot occur. It becomes
  reachable if the passage bound is raised above ~14,400 characters.
- `CandidateDeduplicator.Deduplicate(IReadOnlyList<CandidateCard>)` — drops any candidate whose
  normalised prompt text (trimmed, case-folded, internal whitespace collapsed, trailing punctuation
  removed) collides with an earlier one, preserving order. This catches near-identical wording only;
  two differently-phrased cards testing the same claim are left to the prompt rule and to the
  learner's rejection, and that limit is deliberate rather than an oversight.
- `CandidateBounds.WithinColumnLimits(IReadOnlyList<CandidateCard>, GenerationOptions options)` —
  **drops** any candidate whose `Prompt` exceeds `MaxPromptCharacters` (500) or whose `Answer`
  exceeds `MaxAnswerCharacters` (1000), preserving order. Dropping, not truncating: a clipped card
  reads as a defect the learner cannot fix, and this is the same treatment a duplicate already gets.
  The bound is stated in the JSON schema too (§6), so the model is told it up front and a drop
  should be rare — but the schema is the provider's promise and this function is the guarantee. It
  exists because **nothing else stands between a generated candidate and `SaveAsync`**, where an
  over-long value is a SQL truncation error thrown inside a circuit event handler: the generic
  Blazor error UI, and the untriaged batch gone with it. Phase 1 §2 records where 500 and 1,000
  come from.

#### 6. The generator

**File**: `TenExCards/TenExCards/Generation/ICardCandidateGenerator.cs`,
`TenExCards/TenExCards/Generation/GeminiCardCandidateGenerator.cs`

**Intent**: The one seam the tests stub, and the only type that knows a model provider exists.

**Contract**: `ICardCandidateGenerator.GenerateAsync(string passage, string? focusHint,
IProgress<GenerationProgress> progress, CancellationToken ct)` returning `Task<GenerationResult>`.
The implementation builds an `OpenAIClient`/`ChatClient` with `OpenAIClientOptions.Endpoint` set to
the configured Gemini base URL, sends the system prompt plus a user message carrying the passage and
focus hint, and requests a **JSON-schema response format** describing an object with a `candidates`
array of `{prompt, answer}` objects — each string carrying an explicit `maxLength`, 500 for `prompt`
and 1000 for `answer`, so the model is told the entity's bound rather than left to guess it. It
**streams** the completion and reports progress on each chunk so the page's progress is backed by
evidence the model is still producing, then parses only the final accumulated message. It applies
the cap, the deduplicator and `CandidateBounds.WithinColumnLimits` to the parsed set before
returning — the schema is the provider's promise, that last call is the guarantee. It never logs the
passage, the focus hint or the candidates.

**A set below `MinCandidates` after those three rules is a failure, not a success.** The set is
clipped downward to the cap and nothing currently checks it upward, so a schema-conformant
`candidates: []` — an unsuitable passage, a model that declines, or three rules that between them
drop everything — would enter the triage state with nothing to triage: "0 of 9", or an index past
the end of the list. Return `GenerationFailure.Refused` instead, with a message saying the passage
did not yield usable cards; the failure path then retains the passage exactly as a provider error
does, which is the behaviour the learner needs, since the next thing they will do is edit the
passage or add a focus hint and try again. This is the only thing that produces `Refused`.

The exact `ChatResponseFormat` factory member and streaming method names are to be taken from the
installed 2.13.0 assembly — write the call and let the compiler name it; do not spend the phase
researching type names.

**Decision, so this is not left open**: if Gemini's OpenAI-compatible endpoint refuses
`response_format: json_schema` together with `stream: true`, the fallback is **non-streaming with
elapsed-time progress**, recorded in `change.md` as a measured constraint. Establish which it is with
a single throwaway call at the start of this phase, before the component depends on either shape.

#### 7. Options and registration

**File**: `TenExCards/TenExCards/Generation/GenerationOptions.cs`,
`TenExCards/TenExCards/appsettings.json`, `TenExCards/TenExCards/Program.cs`

**Intent**: Make the tunable numbers configuration rather than constants, and register the generator
so the test factory can replace it.

**Contract**: A `GenerationOptions` class bound from a `Generation` configuration section carrying
`MaxPassageCharacters` (12000), `MaxFocusHintCharacters` (200), `MinCandidates` (3),
`MaxCandidates` (12), `WordsPerCandidate` (200), `MaxPromptCharacters` (500),
`MaxAnswerCharacters` (1000) and `TimeoutSeconds` (30); and a `Gemini` section
carrying `Model` (`gemini-3.8-flash`) and `Endpoint`
(`https://generativelanguage.googleapis.com/v1beta/openai/`). **These non-secret values live in
`appsettings.json`; `Gemini:ApiKey` never does** — it comes from the app setting or user-secrets, and
an **unconditional** guard throws with a named message when it is absent.

**Unconditional is the choice, and the two existing guards are not the same shape.** The
connection-string guard (`Program.cs:23-29`) is unconditional; the key-identifier guard
(`Program.cs:123`) is wrapped in `if (!builder.Environment.IsDevelopment())` because a development
machine must not need vault *key* permissions. That reasoning does not transfer: a development
machine **does** need an API key to generate anything locally, which is exactly what automated
criterion 2.4 checks. Follow the connection-string shape, and fail at boot rather than at the
learner's first submission.

Note that the repository has **no options-binding precedent** — there is no `Configure<T>`,
`.Bind()` or `GetSection` call anywhere in the application today, only delegate-configured framework
options. `GenerationOptions` is therefore a new pattern here, and if it is bound with
`Configure<GenerationOptions>` rather than read eagerly, it needs `ValidateOnStart` for the throw to
land at boot instead of at first use.

Register `ICardStore` **scoped**, alongside the three existing scoped registrations — it resolves
`AppDbContext`, which is scoped, so nothing else is available to it. Register
`ICardCandidateGenerator` **singleton**: it holds an `OpenAIClient`, which is thread-safe and
intended to be reused, and it keeps no per-request state. This is the first service in the project
holding an outbound HTTP client and there is no `IHttpClientFactory` usage to follow, so a scoped or
transient registration would build a new client per circuit interaction — the socket-churn mistake
`IHttpClientFactory` exists to prevent. A singleton consuming `IOptions<GenerationOptions>` is fine;
it must not consume anything scoped.

`MaxPromptCharacters` and `MaxAnswerCharacters` are the one pair here that **must not drift**: they
mirror the `HasMaxLength` values in `AppDbContext` (Phase 1 §2), and a configuration value larger
than the column is the truncation error this rule exists to prevent. Either read both from one place
or assert their equality in a test — do not leave two independent numbers.

`TimeoutSeconds` is configuration rather than a constant because the user has recorded that the
30-second ceiling is a target that may be loosened if it proves unattainable — see `change.md`.
Loosening it in production amends a stated non-functional requirement and belongs in the PRD too.

#### 8. The test host's new requirements

**File**: `TenExCards/TenExCards.Tests/TenExCardsWebApplicationFactory.cs`,
`TenExCards/TenExCards.Tests/StubCardCandidateGenerator.cs`,
`TenExCards/TenExCards.Tests/MigrationGuardTests.cs`

**Intent**: Keep the suite bootable once the guard above exists, and land the one additional test
double this project permits.

**Contract**: The factory boots the **real** `Program.cs` pipeline, so an unconditional guard breaks
every test fixtured on it — all of `AuthBoundaryTests`, `IdentityConfigurationTests`,
`PasswordStorageTests` and this change's own `CardOwnershipTests` — before any service replacement
can run. This is the trap the factory's own comment already records for the connection string
("Program.cs throws on a null connection string before any service replacement below can run, so a
dummy value must exist even though nothing ever reads it for real"). Add
`builder.UseSetting("Gemini:ApiKey", …)` beside the existing two settings, with a comment naming the
guard it satisfies.

Then replace `ICardCandidateGenerator` in the factory's `ConfigureServices` with a deterministic
stub returning a fixed candidate set. This is the **only other test double**
`TenExCards.Tests/AGENTS.md` permits — "it is stubbed because a real model response is
non-deterministic, not because calling out is inconvenient" — and registering it is what stops any
factory-booted test reaching the network, whether or not a test in this slice drives it.

**`MigrationGuardTests` needs the same setting, and for a subtler reason — otherwise it keeps
passing while proving nothing.** It deliberately does *not* use the factory: it builds a bare
`WebApplicationFactory<Program>` with the connection string only, and asserts that touching
`Services` throws `InvalidOperationException` — the proof that the migration-skip flag defaults to
running the migration, since `GetPendingMigrationsAsync()` throws against the in-memory provider.
The new guard throws **the same exception type, earlier**, so the assertion would be satisfied
before the migration block is ever reached: green test, no guarantee. Give that test the dummy
`Gemini:ApiKey` too, and assert on the exception *message* rather than only its type, so it can
never again be satisfied by a different failure. This is
`context/foundation/lessons.md` — "Prove the check before trusting the result" — arriving in a test
instead of a CLI command, and it is worth a comment there saying so.

#### 9. Rule and prompt tests

**File**: `TenExCards/TenExCards.Tests/PassageBoundsTests.cs`,
`TenExCards/TenExCards.Tests/CandidateDeduplicatorTests.cs`,
`TenExCards/TenExCards.Tests/CandidateBoundsTests.cs`,
`TenExCards/TenExCards.Tests/CardGenerationPromptTests.cs`

**Intent**: Assert the deterministic rules, and pin the quality specification so a later agent cannot
quietly soften it.

**Contract**: Boundary cases on the length guard at exactly 12,000 and 12,001 characters; the target
count at the floor, at the defensive clip (an over-producing set, not an over-long passage), and a
mid-range passage; the deduplicator dropping a
differently-cased/punctuated duplicate while preserving order and keeping the first occurrence;
`CandidateBounds.WithinColumnLimits` at exactly 500/501 and 1,000/1,001 characters, dropping only the
offending candidate and preserving the order of the rest, **plus an assertion that the configured
`MaxPromptCharacters`/`MaxAnswerCharacters` equal the entity's `HasMaxLength` values** — this is the
one bound the EF in-memory provider cannot enforce, so nothing else would notice them drifting apart;
and a test asserting `CardGenerationPrompt.System` still mentions each of the four quality properties
— the same reasoning `TenExCards.Tests/AGENTS.md` gives for asserting configured policy that a
comment alone would not protect.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build TenExCards/TenExCards.slnx`
- Tests pass: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
- No secret reaches a tracked file: `git grep -n -E "AQ\.|AIza|gemini-api-key" -- ':!context'`
  returns nothing but documentation references. **`AIza` alone is not sufficient and would have
  missed this project's actual key.** Keys issued by AI Studio on 2026-09-12 carry the newer
  **`AQ.`** prefix and are 53 characters, not the 39-character `AIza…` form every example still
  shows; the pattern was verified against the real key rather than assumed. Keep both prefixes —
  older keys still exist — and drop `-i`, since these prefixes are case-significant and folding case
  only widens the false-positive surface.
- The app still boots locally against `sqldb-tenexcards-dev` with `Gemini:ApiKey` in user-secrets

#### Manual Verification:

- The vault secret `gemini-api-key` exists and the app setting `Gemini__ApiKey` reports
  `"status": "Resolved"` via the `az rest` `config/configreferences/appsettings` GET — **before**
  this phase's commit merges to `main`
- A single throwaway call against the real endpoint establishes whether JSON-schema structured output
  works together with streaming; the answer is recorded in `change.md` either way
- A hand-run generation against a real passage returns candidates that read like cards, not like
  copied sentences — a judgement call, not an assertion, and the reason there is no test for it

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 3: Paste, generate, triage

### Overview

The slice's actual outcome, as one gated interactive component with five states. Merged from what
were two phases because the form and the triage loop share one component's state and no intermediate
split ships anything a learner can use.

### Changes Required:

#### 1. The page

**File**: `TenExCards/TenExCards/Components/Pages/Generate.razor` (+ `.razor.cs`)

**Intent**: The learner's whole loop, from blank paste box to summary.

**Contract**: `@page "/generate"` with `@rendermode InteractiveServer` and **no**
`[AllowAnonymous]` — the fallback policy gates it, and nothing is added to the anonymous allowlist.
A state enum of `Composing`, `Generating`, `Triaging`, `Summary`, `Failed` drives the markup. The
owner id comes from the cascading authentication state, never from a form field.

- **Composing**: a passage textarea and an optional single-line focus hint (max 200 characters), a
  live character counter, and a submit control disabled while the passage is empty or over 12,000
  characters — with `PassageBounds.IsWithinLimit` re-checked in the submit handler before the
  generator is called, because a disabled button is a courtesy and not an enforcement. **The focus
  hint is re-checked in the same handler against `MaxFocusHintCharacters`**, for the same reason:
  two bounds stated in the UI, both enforced where it counts, rather than one of each.
- **Generating**: entered and rendered *before* the generation call is awaited. Shows elapsed time
  and the liveness signal the generator reports, and offers a cancel control. Three mechanics, so
  none of them is invented at the keyboard:
  - **The timeout is the component's, not the generator's.** The handler creates a
    `CancellationTokenSource` cancelling after `TimeoutSeconds` and passes its token to
    `GenerateAsync`; the generator observes the token and returns `GenerationFailure.Timeout`
    rather than letting an `OperationCanceledException` escape. Nothing else produces that failure
    value.
  - **Cancel returns to `Composing` with the passage intact**, using the same
    `CancellationTokenSource` and therefore the same code path as the timeout — a cancelled
    generation is a failure the learner caused, and the one thing they will want next is their text
    back. It is the only exit from `Generating` that is not an outcome.
  - **Elapsed time is driven by a `PeriodicTimer` ticking once a second**, calling
    `InvokeAsync(StateHasChanged)` and stopped when the state leaves `Generating`. Under the
    non-streaming fallback recorded in Phase 2 §6 this counter is the *only* liveness signal, so it
    is not decoration.
- **Triaging**: exactly one candidate, its position in the set (`3 of 9`), and **accept and reject
  as the same control shape at the same size with no confirmation on either** — rejecting must cost
  no more effort than accepting or the acceptance target measures the interface. A plainly worded
  line stating that candidates left untriaged when the session ends are discarded.
- **Summary**: how many were saved and how many discarded, and a control returning to an empty
  composing state. No link to a saved-card list; that surface is `S-04`.
- **Failed**: the failure message, with the passage still in the textarea.

#### 2. Accept, reject, and the passage's disposal

**File**: `TenExCards/TenExCards/Components/Pages/Generate.razor.cs`

**Intent**: Save on accept, discard on reject, and hold nothing longer than necessary.

**Contract**: Accept calls `ICardStore.SaveAsync` with the signed-in owner id and
`CardOrigin.Generated`, **awaits it**, and only then advances to the next candidate — an accepted
card is durable from the moment of acceptance, so advancing before the write completes would make
the guarantee a lie. Reject drops the candidate from memory with no persistence and no confirmation
dialog. The passage and focus hint fields are set to null immediately after a successful generation
and before the triage state is entered; on failure they are left intact. Each triaged candidate is
removed from the in-memory set as it is disposed of, so the circuit's footprint falls as triage
proceeds.

**A save that throws is handled inline, and never moves the state machine.** `SaveAsync` can fail —
Azure SQL produces transient faults, which is why `EnableRetryOnFailure` is configured
(`Program.cs:44-45`), and a retry budget can be exhausted. Unhandled, that exception surfaces as the
generic Blazor error UI, taking the whole untriaged remainder with it: the same guardrail failure the
generator's return-don't-throw rule exists to prevent, on the other path. So accept catches it,
**leaves the candidate in place**, and shows a retryable error beside the accept control. Two things
that would be wrong: advancing to the next candidate (the card was not saved, and the learner would
never know), and transitioning to `Failed` (that state re-renders the passage in the textarea, and
by triage time the passage is gone by design — the learner would be shown an empty box and told
generation failed). A retry must not double-write, so the retry re-issues the same save rather than
queueing a second one.

#### 3. The unload warning

**File**: `TenExCards/TenExCards/Components/Pages/Generate.razor.js`

**Intent**: Turn a silent loss of untriaged candidates into a deliberate one.

**This is the repository's first JS interop, and `ReconnectModal.razor.js` is not a precedent for
it.** That file exports nothing and is loaded by a markup `<script type="module">` tag; there is no
`IJSRuntime` call anywhere in the repository. Two consequences the implementer needs up front.
`Generate.razor.js` must actually `export` its functions, unlike the existing file. And the C# side
must resolve a **fingerprinted** path: `await JS.InvokeAsync<IJSObjectReference>("import", …)` needs
either a plain specifier the `<ImportMap />` in `App.razor:12` resolves, or the physical path taken
from an injected `ResourceAssetCollection` — the `@Assets[…]` helper `ReconnectModal.razor` uses is
a Razor markup helper and is not available from `.razor.cs`. Prove the import resolves with one
throwaway call before building the rest of the module; a failed import is silent in the browser
console and looks like a warning that simply never fires.

**Contract**: A module exporting register/unregister functions over `beforeunload`, imported via
`IJSRuntime` and invoked when the triaging state is entered with candidates remaining, and
unregistered when the set empties, when the state leaves triaging, and in `DisposeAsync`. The
component implements `IAsyncDisposable` and tolerates the circuit already being gone — a disposal
that throws because the browser has left is not an error worth surfacing. The browser's message is
generic and cannot be customised; the on-page warning line is what actually explains the loss.

#### 4. Entry points

**File**: `TenExCards/TenExCards/Components/Layout/NavMenu.razor`,
`TenExCards/TenExCards/Components/Pages/Home.razor`

**Intent**: Make the page reachable, and stop the home page claiming this arrives in a later slice.

**Contract**: A nav entry pointing at `/generate`, following the existing `NavLink` markup. On
`Home`, replace the "Pasting a passage, reviewing candidates, and saving the cards you accept arrive
in later slices" paragraph with a link into the flow inside the existing `<Authorized>` branch —
`Home` is `[AllowAnonymous]` and must stay statically rendered, so it links to the page rather than
hosting any of it.

#### 5. The route-gating test

**File**: `TenExCards/TenExCards.Tests/AuthBoundaryTests.cs`

**Intent**: Assert the new route is gated, in the file that already owns that assertion.

**Contract**: An anonymous `GET /generate` returns `302` to the login path — **never `401`**, which
`scripts/verify_deploy.py` treats as a hard failure and which would burn the whole warm-up budget
with misleading diagnostics. Follow the existing test's shape rather than inventing a second one.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build TenExCards/TenExCards.slnx`
- Tests pass: `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
- `AuthBoundaryTests` shows `/generate` answering `302` to the login path for an anonymous request
- No passage-bearing field is persisted or logged: `git grep -n "passage" -- TenExCards/TenExCards`
  shows no write to a store and no log call carrying it

#### Manual Verification:

- Locally against `sqldb-tenexcards-dev`: paste a real passage, submit, triage the full set, and
  confirm the summary counts match what was accepted and rejected
- An over-length passage is refused with the submit control disabled **and** with the handler's
  re-check — verify by pasting over the bound, then by removing the `disabled` attribute in dev tools
  and submitting anyway
- The passage box is empty the moment triage begins, and still populated after a forced failure
  (temporarily point `Gemini:Endpoint` at an unreachable URL)
- Accept and reject are visually equal in size and prominence, and neither asks for confirmation
- Refreshing mid-triage prompts the browser warning, and after confirming, the untriaged candidates
  are gone and the page is back to a blank paste box
- Cards accepted locally are present in `sqldb-tenexcards-dev` with the correct `OwnerId`

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 4: Deploy and verify on the live instance

### Overview

The 2-second acknowledgement and the generation ceiling cannot be measured honestly against a local
run — different hardware, different network, a B1 instance with 1.75 GB and no back-pressure. This
phase is the only place those requirements are actually tested.

### Changes Required:

#### 1. The deploy

**File**: — (operational step, no file)

**Intent**: Ship through the pipeline rather than by hand.

**Contract**: Push to `main` and let `.github/workflows/deploy.yml` run. Do **not** hand-build and
hand-deploy a commit destined for `main`. Confirm `Gemini__ApiKey` was already set and `Resolved`
before this push — the container will not serve without it. Watch the run's step list, not its
colour.

#### 2. Verification beyond what the verifier can see

**File**: — (operational step, no file)

**Intent**: Cover the failure modes `scripts/verify_deploy.py` structurally cannot catch.

**Contract**: After CI's own `verify_deploy.py` run passes, additionally confirm by hand that each
same-origin asset answers `text/css` or a JavaScript content type rather than `text/html` — the
verifier follows redirects, so a gated asset resolves `302 → /Account/Login → 200` and is recorded
as a pass. Then run the measurements below against the deployed site with a real account.

### Success Criteria:

#### Automated Verification:

- The `main` workflow run succeeds with `Test`, `Publish`, `Pack`, `Retain`, `Azure login`, `Deploy`
  and `Verify` all green — read the step list
- `scripts/verify_deploy.py` passes as part of that run

#### Manual Verification:

- Assets on the live site answer `text/css` / a JavaScript type, not `text/html`
- An anonymous request for `https://tenexcards-ka.azurewebsites.net/generate` returns `302` to the
  login path, not `401`
- **Acknowledgement under 2 seconds**: submitting a maximum-length passage moves the form out of the
  composing state and shows progress within two seconds, measured on the deployed circuit
- **The generation ceiling**: a maximum-length submission yields candidates or reports failure within
  the configured timeout. Record the measured wall-clock time. If it exceeds 30 seconds
  consistently, that is the case the user pre-authorised loosening for — raise `TimeoutSeconds`,
  record the measurement and the new value in `change.md`, and flag the PRD requirement for
  amendment in Phase 5 rather than leaving the two out of step
- **Durability**: accept a card on the live site, refresh, and confirm the row is present in
  `sqldb-tenexcards` scoped to that account. There is no card list yet, so verify with a query
  against the app database
- **Nothing retains the passage**: the deployed schema has no column that could hold one, and the
  App Service log stream carries no passage text across a full generation
- A generation failure on the live site reports itself and leaves the passage recoverable — force one
  by submitting while the free-tier rate limit is exhausted, or by briefly revoking the key

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 5: Update the repository record

### Overview

Several durable records currently state things this slice makes untrue. The most important is the
sentence in `TenExCards/AGENTS.md` saying an LLM client is missing.

### Changes Required:

#### 1. The agent guide

**File**: `TenExCards/AGENTS.md`

**Intent**: Stop the guide describing a state that no longer exists, and record the two new
operational facts an agent would otherwise have to rediscover.

**Contract**: In `## What is wired, and what is not`, replace the "Still missing: an **LLM client**"
paragraph with what is now wired — the provider, the model string, the endpoint, the interface seam,
and where the card-quality specification lives. In `### Persistence`, add `gemini-api-key` to the
four-secrets list (now five) and note that the site's existing `Key Vault Secrets User` assignment
already covers it. Add the `Gemini__ApiKey` app setting beside the existing two, with the same
before-the-merge ordering rule. Do **not** restate the generation rules that already live in the
PRD; point at them.

#### 2. The deployment record

**File**: `context/deployment/deploy-plan.md`

**Intent**: Record what was actually run and what was actually measured, which is that file's job.

**Contract**: A dated `S-02` record covering the vault secret and app setting, the measured
acknowledgement and generation times on the deployed B1 instance, whether streaming and JSON-schema
structured output work together on Gemini's OpenAI-compatible endpoint, and any free-tier limit that
was actually hit. Measurements, not estimates.

#### 3. The roadmap

**File**: `context/foundation/roadmap.md`

**Intent**: Close Open Roadmap Question 2 and record what landed.

**Contract**: Strike through Open Roadmap Question 2 and record the resolution — Gemini
`gemini-3.8-flash` on the free tier via the OpenAI-compatible endpoint, with the reasoning and the
recorded risk that free-tier content may be used for model improvement. Add a **Landed** paragraph
to the `S-02` item in the same style as `S-01`'s. Leave the `Status` field to `/10x-archive`; this
plan's earlier roadmap edit already moved it to `planning` and `/10x-implement` moves it to
`in-progress`.

**Move the externally required learner-perspective test to `S-03`, in the roadmap itself.** The
`S-02` risk block currently says it "attaches here"; amend that sentence to record that it did not,
and add it to the `S-03` entry with the reason — US-01's first acceptance criterion names edit at
equal prominence, and edit is `S-03`, so the test cannot assert US-01 in full before that slice.
This is the only place the move is durable: `## What We're NOT Doing` in this plan is archived with
the change, and a deferral recorded only there is a deferral nobody reads again.

#### 4. The PRD, only if the ceiling moved

**File**: `context/foundation/prd.md`

**Intent**: Keep the requirement and the implementation from silently disagreeing.

**Contract**: If Phase 4's measurement forced `TimeoutSeconds` above 30, amend the
`## Non-Functional Requirements` bullet to the measured value and record why in `## Open Questions`,
in the style of the resolved session-window entry. If the ceiling held, change nothing here.

#### 5. The change record

**File**: `context/changes/passage-to-saved-cards/change.md`

**Intent**: Capture what the plan did not predict.

**Contract**: Append findings — in particular the streaming/structured-output answer, any free-tier
limit encountered, and anything about card quality that the four stated properties did not cover.

### Success Criteria:

#### Automated Verification:

- Solution builds and tests pass: `dotnet build TenExCards/TenExCards.slnx` and
  `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
- `git grep -n "Still missing" TenExCards/AGENTS.md` returns nothing about an LLM client
- `git grep -n "arrive in later slices" TenExCards/TenExCards/Components/Pages/Home.razor` returns
  nothing

#### Manual Verification:

- `TenExCards/AGENTS.md` reads correctly end to end for someone who has never seen this slice —
  in particular, the secret count and the app-setting list match reality
- The roadmap's `S-02` entry and Open Roadmap Question 2 tell the same story as
  `context/deployment/deploy-plan.md`
- The roadmap's `S-03` entry now carries the learner-perspective test, and `S-02`'s risk block no
  longer claims it attached here
- Every fact appears in exactly one of `AGENTS.md` or `deploy-plan.md`, never both

---

## Testing Strategy

### Unit Tests:

- `PassageBounds.IsWithinLimit` at exactly 12,000 and 12,001 characters, and on an empty passage
- `PassageBounds.TargetCandidateCount` at the floor (very short passage → 3), at the cap (very long
  passage → 12), and mid-range
- `CandidateDeduplicator` dropping a duplicate that differs only in case, trailing punctuation or
  internal whitespace; preserving order; keeping the first occurrence
- `CandidateBounds.WithinColumnLimits` at exactly 500/501 and 1,000/1,001 characters, dropping only
  the offending candidate; and the configured limits matching the entity's `HasMaxLength` values
- `CardGenerationPrompt.System` still stating each of the four quality properties

### Integration Tests:

- Card ownership against the in-memory database: user A's card is invisible to user B;
  `CountForOwnerAsync` counts only the caller's rows
- Route gating: anonymous `GET /generate` → `302` to the login path, never `401`

### Manual Testing Steps:

1. Sign in locally, paste a real article-length passage, submit, and triage the whole set
2. Paste over 12,000 characters and confirm refusal before any generation call — then remove the
   button's `disabled` attribute in dev tools and submit, confirming the handler refuses too
3. Force a generation failure and confirm the passage comes back in the textarea
4. Refresh mid-triage; confirm the warning, then confirm the untriaged candidates are gone
5. On the deployed site: measure acknowledgement (<2s) and total generation time at maximum length
6. Accept a card on the deployed site, refresh, and confirm the row exists in `sqldb-tenexcards`
7. Read the App Service log stream across a full generation and confirm no passage text appears

### What is deliberately not tested

The component's own behaviour — progress appearing within two seconds, the unload warning firing,
accept and reject being equally cheap — is verified manually only. `@rendermode InteractiveServer`
components cannot be driven by the HTTP harness `TenExCards.Tests` uses, and adding a component-test
framework would widen the deliberately narrow test-double rule in `TenExCards.Tests/AGENTS.md`. The
mitigation is that almost nothing decidable lives in the component: the rules are pure functions
below it, each with its own test.

**This argument does not cover the externally required learner-perspective test, and must not be
read as covering it.** That test is browser-driven, not a component test, so the harness limitation
above is beside the point for it. It is deferred to `S-03` by an explicit decision recorded in
`## What We're NOT Doing`, for a reason of its own — US-01's first acceptance criterion names edit at
equal prominence, and edit is `S-03`.

Generated card *text* is never asserted. `TenExCards.Tests/AGENTS.md` is explicit — card quality is
judged by the learner at triage, and an assertion against model prose is a flaky test, not a quality
gate.

## Performance Considerations

Blazor Server memory is per-user — roughly 250 KB per circuit before application state — and B1 gives
1.75 GB with no back-pressure, so the ceiling arrives as OOM restarts that look like random
disconnects, and every restart drops every circuit. This slice deliberately holds a passage plus its
candidates in the circuit until triage ends, which is why every number is bounded: the passage at
12,000 characters (~24 KB as UTF-16), the focus hint at 200, the candidate set at 12, and each
candidate at 500 + 1,000 characters — enforced by `CandidateBounds.WithinColumnLimits` (Phase 2 §5)
rather than assumed of the model. A full batch is therefore well under 100 KB of application
state, and it shrinks as triage proceeds because each triaged candidate is dropped from the set.

The generation call holds a circuit open for the duration of the timeout. At the PRD's stated scale
(`target_scale.qps: low`) that is a handful of concurrent requests at worst, comfortably inside the
tier's headroom — but it is the first thing to re-examine if concurrent triage sessions reach the low
tens, alongside the ARR session-affinity concern already recorded in `TenExCards/AGENTS.md`.

`EnableRetryOnFailure` is already configured on the SQL connection, which matters here because the
accept path writes to the database inside a user-visible interaction.

## Migration Notes

One forward-only migration, `AddCards`, creating one table and one index. It runs on the boot path
via `Database.MigrateAsync()`, so a throw means the container does not serve on a tier with no
deployment slots. It is applied to `sqldb-tenexcards-dev` first, by a local `dotnet run`, and the
previous archive is retained — but redeploying that archive does **not** reverse schema. No `Down()`
is relied on. The table is additive and nothing existing depends on it, which is the main reason
this phase is sequenced first.

## References

- Roadmap slice: `context/foundation/roadmap.md` → `### S-02`
- Product rule: `context/foundation/prd.md` → `## Business Logic`, `## Non-Functional Requirements`,
  US-01
- Repository rules: `TenExCards/AGENTS.md`, `TenExCards/TenExCards.Tests/AGENTS.md`
- Prior slice for structure and conventions:
  `context/archive/2026-09-12-accounts-and-sessions/plan.md`
- App-setting ordering trap: `context/deployment/deploy-plan.md`
- Recurring rules: `context/foundation/lessons.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not
> rename step titles. See `references/progress-format.md`.

### Phase 1: The card entity and the account boundary

#### Automated

- [x] 1.1 Solution builds — 9041291
- [x] 1.2 Tests pass — 9041291
- [x] 1.3 Migration creates exactly one table and one index, altering no Identity table — 9041291
- [x] 1.4 `CardOwnershipTests` observed failing with the owner filter removed — 9041291

#### Manual

- [x] 1.5 Migration applies cleanly to `sqldb-tenexcards-dev` with the expected columns and index — 9041291
- [x] 1.6 No entity, store method or column can hold a passage — 9041291

### Phase 2: The generation client and the card-quality specification

#### Automated

- [x] 2.1 Solution builds — fe56f6d
- [x] 2.2 Tests pass — fe56f6d
- [x] 2.3 No secret reaches a tracked file — fe56f6d
- [x] 2.4 App boots locally against `sqldb-tenexcards-dev` with `Gemini:ApiKey` in user-secrets — fe56f6d

#### Manual

- [x] 2.5 `gemini-api-key` in the vault and `Gemini__ApiKey` reporting `Resolved`, before the merge — fe56f6d
- [x] 2.6 Streaming + JSON-schema structured output established by a throwaway call and recorded — fe56f6d
- [x] 2.7 A hand-run generation returns cards that read as reformulated, not copied — fe56f6d

### Phase 3: Paste, generate, triage

#### Automated

- [x] 3.1 Solution builds
- [x] 3.2 Tests pass
- [x] 3.3 `AuthBoundaryTests` shows `/generate` answering `302` to the login path
- [x] 3.4 No passage-bearing field is persisted or logged

#### Manual

- [x] 3.5 Full local loop: paste, triage, summary counts match
- [x] 3.6 Over-length refused both by the disabled control and by the handler's re-check
- [x] 3.7 Passage cleared on success, retained on a forced failure
- [x] 3.8 Accept and reject equal in prominence, neither confirming
- [x] 3.9 Refresh mid-triage warns, then discards the untriaged remainder
- [x] 3.10 Locally accepted cards present in `sqldb-tenexcards-dev` with the correct `OwnerId`

### Phase 4: Deploy and verify on the live instance

#### Automated

- [ ] 4.1 `main` workflow run green across the full step list
- [ ] 4.2 `scripts/verify_deploy.py` passes in that run

#### Manual

- [ ] 4.3 Live assets answer `text/css` / a JavaScript type, not `text/html`
- [ ] 4.4 Anonymous `/generate` on the live site returns `302`, not `401`
- [ ] 4.5 Acknowledgement under 2 seconds on the deployed circuit at maximum length
- [ ] 4.6 Generation ceiling measured and recorded; timeout raised and flagged only if it was missed
- [ ] 4.7 A card accepted live survives a refresh, confirmed by query against `sqldb-tenexcards`
- [ ] 4.8 No passage text in the deployed schema or the App Service log stream
- [ ] 4.9 A live generation failure reports itself and leaves the passage recoverable

### Phase 5: Update the repository record

#### Automated

- [ ] 5.1 Solution builds and tests pass
- [ ] 5.2 `AGENTS.md` no longer says an LLM client is missing
- [ ] 5.3 `Home.razor` no longer says the flow arrives in later slices

#### Manual

- [ ] 5.4 `AGENTS.md` reads correctly end to end; secret count and app-setting list match reality
- [ ] 5.5 Roadmap `S-02`, Open Roadmap Question 2 and `deploy-plan.md` tell the same story
- [ ] 5.6 Every fact lives in exactly one of `AGENTS.md` or `deploy-plan.md`, never both
- [ ] 5.7 The roadmap moves the learner-perspective test to `S-03` and `S-02` no longer claims it
