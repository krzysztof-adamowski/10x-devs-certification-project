# End-to-end testing rules

Browser-driven tests for 10xCards, landed by `S-03` (`edit-before-accepting`) on 2026-09-13. They
assert US-01 from the learner's side: register, paste, reject one candidate, edit and accept
another, accept a third untouched, read the summary.

Paths here are written from the **repo root**, as in `TenExCards/AGENTS.md`.

## This suite does not gate the deploy, and that is a decision

`.github/workflows/deploy.yml` runs it with `continue-on-error: true` — the only non-gating
assertion in that file. `TenExCards.Tests` is the gate; it is named there by path, so it cannot
pick this project up.

**A red run here does not show up as a red step.** `continue-on-error: true` sets the step's
*conclusion* to `success` even when the command exits non-zero, so `gh run view --json jobs` and the
web step list both report green. Verified 2026-09-13 by deliberately breaking the summary assertion:
the run, the step list and every step read `success`, while the step's own log carried
`Failed: 1, Passed: 4` and `##[error]Process completed with exit code 1`. To find out whether this
suite actually passed, read the step's **log** or its `##[error]` annotation — never its conclusion.
This is the one place in the repository where `TenExCards/AGENTS.md`'s "read the step list, not the
colour" is not enough.

The reason is trade, not doubt: a browser suite fails for reasons that have nothing to do with the
change under deploy, and blocking production on that is not worth it until this suite has a track
record. **What would have to be true to change it**: a run of stable green across many merges, with
every red traced to a real defect rather than to the browser, the runner or a timing race. Until
then, do not remove `continue-on-error` — and if you do remove it, record the evidence here.

## It drives the application over HTTP, and never references it

`TenExCards.E2E.csproj` has **no `ProjectReference` to `TenExCards.csproj`**, deliberately. A
reference would let a test reach past the browser into application types and quietly stop being an
end-to-end test. The scripted candidate prompts are therefore duplicated as constants in
`LearnerJourneyTests`; that duplication is the price of the boundary, not an oversight.

## Starting it locally

Browsers are not installed by a restore. Before a first local run:

```bash
dotnet build TenExCards/TenExCards.E2E/TenExCards.E2E.csproj
pwsh TenExCards/TenExCards.E2E/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test TenExCards/TenExCards.E2E/TenExCards.E2E.csproj
```

Set `E2E_HEADED=1` to watch it run rather than guess what it did, and `E2E_VIDEO_DIR=<path>` to
record it — a `.webm` per test — when nobody can sit and watch at the moment it runs.

`AppUnderTest` spawns the application itself on `http://127.0.0.1:5199` and kills the process tree
afterwards. It sets four environment variables and **`ASPNETCORE_ENVIRONMENT=Development` is not
optional**: `--no-launch-profile` alone defaults to Production, where the Key Vault guard runs and
every framework asset 500s — the page then renders unstyled rather than failing outright, which is
much harder to diagnose than a clean failure. `Testing__E2E=true` selects the harness;
`DOTNET_ENVIRONMENT` and `ASPNETCORE_URLS` are the other two.

## The harness must never reach a Release build

`Testing:E2E` swaps in `ScriptedCardCandidateGenerator` and an in-memory store, so the suite needs
no model, no database and no Key Vault. **Two mechanisms keep it out of production and both are
required**: `#if DEBUG` around `Testing/E2EHarness.cs` and
`Generation/ScriptedCardCandidateGenerator.cs`, and `Condition="'$(Configuration)' == 'Debug'"` on
the `Microsoft.EntityFrameworkCore.InMemory` package reference. CI publishes with `-c Release`.

The harness also checks `IsDevelopment()`, so the flag alone does nothing outside Development —
verified 2026-09-13 by booting with `ASPNETCORE_ENVIRONMENT=Production Testing__E2E=true`, which
still failed on the missing connection string.

Verify absence by byte-searching the published assembly, not with `strings`: `strings` returned
zero for a type that *is* present, which is indistinguishable from a real absence. Search the
publish output for `ScriptedCardCandidateGenerator`, `E2EHarness` and an `InMemory` assembly, and
**include a control name that must be found** (`CandidateEdit` works) so a broken check cannot read
as a pass.

## Select by accessible name, never by CSS class

Assertions use `GetByRole`, `GetByLabel` and `GetByText`. That keeps restyling from breaking the
suite and makes every assertion double as an accessibility check. `AssertEquallyProminentAsync`
is the one exception in spirit — it reads bounding boxes, because "equal prominence" in US-01 is a
visual claim and literal sameness of rendered width is the machine-checkable form of it.

## The circuit race, and why `FillOverCircuitAsync` exists

`/generate` is `@rendermode InteractiveServer`. **An `oninput` dispatched before the circuit's
WebSocket connects is simply lost** — the DOM holds the typed text while the component still
believes the field is empty, so the submit button never enables and the test times out somewhere
misleading. A single `FillAsync` races the connection. `FillOverCircuitAsync` fills and then
confirms the server saw it, re-filling until it did. Use it for any bound field on an interactive
page; a bare `FillAsync` there is a flake waiting to happen.

## Do not assume a culture

The application formats counters with `ToString("N0")`, which follows the **server's** culture — on
this development machine that is a non-breaking space, not a comma. Assert with a separator-tolerant
regex rather than pinning `12,000`.

## Assertions use AwesomeAssertions, never FluentAssertions

A licensing rule, identical to `TenExCards.Tests`. See `TenExCards/AGENTS.md`.
