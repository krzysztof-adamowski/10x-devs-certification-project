<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Manage Saved Cards

- **Plan**: `context/changes/manage-saved-cards/plan.md`
- **Scope**: Phases 1–3 of 4 (Phase 4 is the production deploy, not yet run)
- **Date**: 2026-09-13
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 5 warnings, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

Automated criteria re-run at review time: solution builds with 0 errors; `TenExCards.Tests`
116/116; `TenExCards.E2E` 12/12; `CardOptions` carries no length mirror; `AllowAnonymous` on
exactly the four known page surfaces.

The account boundary was verified clean and needs no remediation: every `ICardStore` member takes
`ownerId` and predicates on it, update and delete filter by `Id && OwnerId` in one round trip so
there is no read-then-write gap, "not yours" and "does not exist" are indistinguishable at both the
store and the page, and `CardOwnershipTests` asserts all four members across the boundary — each
observed failing with the filter removed.

## Findings

### F1 — `SearchAsync` sets state on the wrong side of its first `await`

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `TenExCards/TenExCards/Components/Pages/Cards.razor.cs:58-99`
- **Detail**: `_loading = true; _loadError = null;` are assigned *after* `await previous.CancelAsync()`.
  `CancelAsync` genuinely yields, so a newer keystroke can run to completion during the suspension:
  the older search then resumes, sets `_loading = true`, and its `finally` refuses to clear the flag
  because `_searchCts` now belongs to the newer search. `_loading` stays true for the life of the
  circuit and an empty result set renders "Looking…" forever, never reaching "Nothing matches" or
  the no-cards state. Two related defects in the same block: the late resumer also clears a
  `_loadError` a newer search set, and the `catch (Exception)` writes `_loadError` and `_results`
  **without** the currency check the success path uses, so a superseded failure blanks results a
  newer search already delivered.
- **Fix**: Hoist `_loading`/`_loadError` above the cancel await, and gate every post-await write
  behind the same `ReferenceEquals(_searchCts, cts)` test the `finally` already uses.
  - Strength: One edit closes the stuck flag, the erased error and the clobbered results.
  - Tradeoff: None material.
  - Confidence: HIGH — the currency guard already exists on the success path.
  - Blind spot: None significant.
- **Decision**: FIXED

### F2 — The "Looking…" indicator never reaches the browser

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `Cards.razor.cs:67`, `Cards.razor:32-35`
- **Detail**: `ComponentBase` renders at the handler's first yield and again at completion. For a
  keystroke search the first yield is the cancel await — before `_loading = true` — and by
  completion `_loading` is false again, so the state is unreachable while typing. `Generate.razor.cs`
  does the opposite deliberately and says why. `AGENTS.md`'s "never let the form freeze" is the
  rule this sits under.
- **Fix**: Same hoist as F1.
- **Decision**: FIXED

### F3 — The delete confirmation is enforced only by the render tree

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `Cards.razor.cs:179-204`
- **Detail**: `ConfirmDeleteAsync(id)` checks `_busy` but never `_confirmingDeleteId == id`. The
  two-step gate exists purely because the button is rendered under `@if`. A replayed circuit event
  for any card id in the learner's current list deletes it with no confirmation — irreversibly, and
  the page's own copy says so. The same file argues forty lines earlier why a render-tree guard is
  not a guard ("a disabled attribute can be removed in dev tools") and does not apply that reasoning
  to the more destructive action. Scope is the learner's own cards, so this is not cross-account.
- **Fix**: `if (_busy || _confirmingDeleteId != id) return;`
- **Decision**: FIXED

### F4 — `Dispose()` disposes the search CTS without cancelling it

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `Cards.razor.cs:206-210`
- **Detail**: Navigating away disposes `_searchCts` while its token may still be registered in the
  EF/SqlClient pipeline: the in-flight query keeps running after the circuit is gone, and disposing
  a CTS with live registrations can surface `ObjectDisposedException` from inside the provider —
  which lands in the blanket catch and triggers F1's clobbering path. `IDisposable` rather than
  `IAsyncDisposable` is correct here: cancelling a CTS is synchronous, and unlike `Generate` this
  page has no awaitable teardown.
- **Fix**: `Cancel()` before `Dispose()`.
- **Decision**: FIXED

### F5 — The candidate-edit gate and its message now come from different types

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Pattern Consistency
- **Location**: `TenExCards/TenExCards/Components/Pages/Generate.razor.cs:221-225`
- **Detail**: Introduced by this change. `AcceptAsync` still *decides* with
  `CandidateEdit.IsCommittable` but now takes its *message* from `CardEdit.Validate(...).Message`.
  They agree only because the inputs are pre-trimmed: `IsCommittable` measures untrimmed length
  while `Validate` trims first. A future caller passing untrimmed text gets `IsCommittable == false`
  with `Validate` valid, assigning `_editValidationMessage = null` — a silent refusal with no
  visible reason.
- **Fix**: Make `CandidateEdit.IsCommittable` delegate to `CardEdit.Validate(...).IsValid` so one
  rule backs both the decision and the message.
  - Strength: Removes the divergence class rather than the current instance.
  - Tradeoff: `Generation.CandidateEdit` takes a dependency on `Cards.CardEdit`.
  - Confidence: HIGH — both already bottom out on `CardBounds`.
  - Blind spot: None significant.
- **Decision**: FIXED

### F6 — The capped-list notice over-claims at exactly the cap

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `Cards.razor:121-126`
- **Detail**: The notice fires on `_results.Count == Options.MaxResults`, so a learner owning
  exactly 20 cards is told "Search to reach the rest" when there is no rest. `SavedCardsPageTests`
  seeds `MaxResults + 1`, so the suite never sees it.
- **Fix**: Reword to a claim that is true either way rather than querying `limit + 1` to detect
  overflow — the honesty requirement is that twenty rows must not read as the whole collection.
- **Decision**: FIXED

### F7 — Two new page tests can pass for the wrong reason

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `TenExCards/TenExCards.Tests/SavedCardsPageTests.cs:73, 98`
- **Detail**: `Home_WhenSignedIn_LinksToTheSavedCards` asserts `href="cards"` on `/`, but `NavMenu`
  renders that exact href on every page — the assertion survives deleting the Home button it claims
  to test. The `Contain("generate")` assertion has the same hole. The E2E counterpart gets this
  right by scoping to `AriaRole.Article`.
- **Fix**: Scope both assertions to the content area rather than the whole document.
- **Decision**: FIXED

### F8 — The new E2E port guard treats its own timeout as "port free"

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `TenExCards/TenExCards.E2E/AppUnderTest.cs:88-100`
- **Detail**: `RefuseIfAlreadyServingAsync` returns "free" on `TaskCanceledException` — its own
  two-second timeout. A leftover process that is alive but slow (cold start, mid-migration: exactly
  the state a just-killed run leaves) is therefore not detected, and the suite then drives the old
  build, which is the precise failure the guard was written for.
- **Fix**: Treat a timeout as occupied; only a connection refusal means free.
- **Decision**: FIXED

### F9 — One database round-trip per keystroke, with no debounce

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `Cards.razor:17`, `Cards.razor.cs:54`
- **Detail**: Every character issues a non-sargable `LOWER(...) LIKE '%…%'` query against S0 Azure
  SQL (10 DTU). Supersede-and-cancel bounds *stale results*, not *load* — cancellation reaches SQL
  Server as an attention request after the query has already started. `_busy` is deliberately not
  applied here, which is right; nothing throttles either.
- **Fix**: Debounce ~250 ms before issuing the query, keeping supersede-and-cancel as the
  correctness backstop.
  - Strength: Cuts round-trips by roughly an order of magnitude on a 10 DTU tier.
  - Tradeoff: Adds a timer interacting with the CTS logic and with E2E timing, in a component whose
    only automated coverage is a non-gating browser suite.
  - Confidence: MEDIUM — the win is clear, the destabilisation risk is real.
  - Blind spot: Not measured against the live instance; Phase 4 is where latency becomes observable.
- **Decision**: SKIPPED — deliberate. Recorded as a Phase 4 question rather than fixed blind; see
  plan item 2.9, which is the live check this would change the answer to.

### F10 — Every failure path is swallowed without logging

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `Cards.razor.cs:86, 169, 196`
- **Detail**: All three `catch (Exception)` blocks discard the exception. No `ILogger` is injected
  in the page. A learner reporting "my card wouldn't delete" leaves nothing in App Service logs —
  including the case where the delete committed and only the acknowledgement timed out, leaving a
  deleted card still rendered. `Generate.razor.cs` has the same posture, so this is a project-wide
  gap rather than drift introduced here.
- **Decision**: SKIPPED — project-wide posture, not this slice's to change unilaterally. Candidate
  for `/10x-lesson` and a dedicated observability slice.

## Observations carried forward, not fixed

- **`GetForOwnerAsync` has no production caller** — interface surface exercised only by tests. The
  page reads through `FindForOwnerAsync`. Harmless; `S-05`/`S-06` are the likely consumers.
- **Prerendering doubles the initial query** — `OnInitializedAsync` runs on the HTTP request and
  again on the circuit, so two identical queries per page load. New with this page, since
  `Generate.OnInitializedAsync` does no I/O.
- **The search's stated justification is the weak part, not its shape** — production pays a
  sargability cost so that a *test double* behaves like production. Defensible at this data volume
  and recorded in `AGENTS.md`, but the honest alternative is `EF.Functions.Like` plus a
  case-insensitivity test that runs against real SQL.
- **Scope**: roughly 330 lines of unplanned E2E work across four files plus a doc. Judged justified
  rather than creep — the plan predates the E2E project, and every test maps onto a plan manual
  item — but it is scope the plan never authorised.
