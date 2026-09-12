<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Interactive Blazor Server Shell

- **Plan**: `context/changes/blazor-server-shell/plan.md`
- **Scope**: Full plan — Phases 1, 2 and 3 (all complete)
- **Commits**: `8a6c8b8` (p1) · `85af805` (p2) · `49d4092` (p3) · `877e177` (epilogue)
- **Date**: 2026-09-08
- **Verdict**: NEEDS ATTENTION — all 10 findings triaged 2026-09-08: 9 fixed, 1 accepted as risk and handed to F-03
- **Findings**: 0 critical · 7 warnings · 3 observations

All 11 automated success criteria were re-run independently during this review and all pass,
including a live re-check of the deployed routes and a re-run of the archive shape gate against
the artifact actually deployed. The build is clean at 0 warnings. **No code defect was found and
no `## Never do these` rule is violated by the code.** Every finding below is in the change's
records rather than its behaviour — which matters here, because Phase 3's entire purpose was to
make those records true.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

**Architecture passes on evidence, not by default.** `Program.cs` matches the .NET 10 Blazor Web
App template order exactly: `UseAntiforgery()` sits after the implicit `UseRouting()` and before
`MapRazorComponents`, `MapStaticAssets()` correctly replaces `UseStaticFiles()` for .NET 9+, and
`UseHsts()` is inside the non-Development branch where `HstsOptions.ExcludedHosts` already
excludes localhost. No status-code re-execution loop is possible: `/not-found` is a real route and
`StatusCodePagesMiddleware` disables its own feature during re-execution.

## Findings

### F1 — infra/main.bicep still documents the middleware this change deleted

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: infra/main.bicep:84-95
- **Detail**: Phase 3 opens "Three documents make claims this change falsifies." There were four.
  The comment block above `httpsOnly: true` still reads "it makes `UseHttpsRedirection()` in
  `Program.cs` redundant", "the middleware returns before looking for a port", and "The
  `HttpsRedirectionMiddleware[3] Failed to determine the https port` line appears once at startup
  … It is not per-request and not a defect."
  All three describe a middleware that no longer exists. This is worse than ordinary staleness:
  `CLAUDE.md` declares `infra/main.bicep` the source of truth for infrastructure state, and
  `AGENTS.md` was edited by this change to install the opposite as a tripwire — "**It no longer
  appears** … if you see it again, the middleware is back." A future agent consulting the
  designated source of truth is told the warning is expected, and will dismiss precisely the
  signal Phase 3 installed to catch a regression.
- **Fix**: Amend the bicep comment block to record that `UseHttpsRedirection()` was removed on
  2026-09-08 and that `httpsOnly` is now the sole enforcement point, and drop the "not a defect"
  line for the startup warning.
  - Strength: Restores agreement between the three records that describe HTTPS posture, and keeps
    the tripwire armed.
  - Tradeoff: Touches a file the plan explicitly said it would not change ("No changes to
    `infra/main.bicep`") — though that exclusion was about infrastructure *resources*, not comments.
  - Confidence: HIGH — the contradiction is verbatim and verified in both files.
  - Blind spot: None significant.
- **Decision**: FIXED — bicep comment block now records the 2026-09-08 removal, ties Request.IsHttps to UseHsts(), and states the startup line no longer appears (tripwire preserved). Comments only; no resource property changed.

### F2 — The plan still teaches the packaging rule it disproved, and a Progress row certifies it

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: context/changes/blazor-server-shell/plan.md:133-136 and Progress row 2.2
- **Detail**: `## Critical Implementation Details` — reached *before* Phase 2, with no supersession
  marker — still instructs: "**The trailing `*` in `Compress-Archive -Path <publish>/*` is
  load-bearing.** … Assert the archive's first entry is `TenExCards.dll` … before uploading".
  Both halves were disproven by this change. `AGENTS.md` and `deploy-plan.md` each received a
  correction for exactly this text, and `deploy-plan.md` step 6 even got a `> Superseded` marker;
  the plan's own body was missed.
  The Progress row is the sharper problem: `- [x] 2.2 Archive first entry is TenExCards.dll
  (nested-zip assertion passes) — 85af805` is **factually false** about the artifact that shipped.
  Entry 0 of the deployed `publish.zip` is `appsettings.Development.json`. The row is checked, and
  the plan's own success criterion two sections above now reads "all three shape assertions".
  Rows 1.6 and 1.2 have the same shape — each states a criterion the plan's body later widened.
  The `/10x-implement` convention "do not rename step titles" explains why they were not edited,
  but the result is a done-checklist asserting things the plan says are wrong.
- **Fix A ⭐ Recommended**: Add a `> Superseded` marker to `## Critical Implementation Details`
  pointing at the Phase 2 adaptation note, and append a one-line note under the Progress heading
  recording that rows 1.2, 1.6 and 2.2 are titled by their original criteria and were verified
  against the amended ones.
  - Strength: Respects the "do not rename step titles" convention that exists to keep Progress
    machine-parseable, while removing the contradiction. Matches how `deploy-plan.md` step 6 was
    handled in this same change.
  - Tradeoff: The rows still read wrong in isolation; only the note rescues them.
  - Confidence: HIGH — the marker pattern is already used twice in this change.
  - Blind spot: Whether `/10x-archive` parses anything under the Progress heading that a note
    would disturb.
- **Fix B**: Rename the three Progress rows to their amended criteria and accept the convention break.
  - Strength: The checklist becomes true read standalone.
  - Tradeoff: Breaks a stated convention of the workflow that produced it; SHA suffixes would
    need re-checking.
  - Confidence: MEDIUM — depends on how strictly the archive step parses row titles.
  - Blind spot: Not verified whether any tool matches rows by exact title.
- **Decision**: FIXED via Fix A — `> Superseded` marker added to `## Critical Implementation Details`; note under `## Progress` records that rows 1.2, 1.6 and 2.2 carry original titles and were verified against amended criteria.

### F3 — AGENTS.md warns about an outcome this change made impossible, and omits the one that remains

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: TenExCards/AGENTS.md:73-75
- **Detail**: The rule reads "**Never set `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, and never add
  `ASPNETCORE_HTTPS_PORT`.** Together they produce an infinite redirect."
  The repo's own measurement in `deploy-plan.md` `## Deliberately not set` says the loop needs the
  port *plus* `ASPNETCORE_FORWARDEDHEADERS_ENABLED=false`, and that the variable "is already active
  by platform default — which is why disabling it is what breaks things." The redirect loop also
  required `HttpsRedirectionMiddleware`, which this change removed, so the stated outcome can no
  longer occur at all.
  The live hazard is now unstated. `Request.IsHttps` is true only because forwarded-header
  processing is on by platform default, and `UseHsts()` — added by this change — emits its header
  only when `Request.IsHttps`. Setting the variable to `false`, which a literal reading of "never
  set" arguably invites and which bicep documents as a tested value, silently stops the HSTS
  header, and would later strip `Secure` from Identity cookies under the default
  `CookieSecurePolicy.SameAsRequest`. No warning, no log line, no failing request.
  Note the rule's *advice* ("never set it") remains safe; its *explanation* is now wrong and its
  scope is now under-stated.
- **Fix**: Rewrite as "Never set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=false` — it is on by platform
  default, and `UseHsts()` plus (later) Identity cookie security depend on it. Never add
  `ASPNETCORE_HTTPS_PORT` — harmless today only because `UseHttpsRedirection()` is gone."
  - Strength: Names the failure that can still happen, and ties it to `UseHsts()`, which this
    change introduced.
  - Tradeoff: None material; same bullet, corrected.
  - Confidence: HIGH — contradicts two measurements recorded in this repo.
  - Blind spot: The Identity-cookie consequence is reasoned from ASP.NET Core defaults, not
    measured — Identity does not exist yet.
- **Decision**: FIXED — bullet rewritten to forbid `ASPNETCORE_FORWARDEDHEADERS_ENABLED=false` specifically, name the silent HSTS/Identity-cookie consequence, and mark the 307 loop as historical (it needed the removed middleware).

### F4 — A new "never" rule with no executable implementation and a silent failure mode

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: TenExCards/AGENTS.md:58-65
- **Detail**: This change added "**Never upload an archive that has not passed all three shape
  assertions** … **Do not build the archive with `Compress-Archive`** … Build it entry-by-entry
  with names normalised to `/`". There is no script in the repo that does either — no `.ps1`,
  `.sh` or `Makefile` exists, and `deploy-plan.md` records only the prose "built entry-by-entry
  with `ZipArchive.CreateEntry`". The next deployer must reconstruct both the packer and the three
  assertions from description. By the rule's own statement, reconstructing them wrong "deploys
  **successfully** and then breaks at runtime" — the failure mode is that you cannot tell you got
  it wrong. The working packer and gate existed as throwaway scratch scripts during Phase 2 and
  were not committed.
- **Fix**: Commit the pack-and-verify script (build the archive, run all three assertions, exit
  non-zero on failure) and point both AGENTS.md and deploy-plan.md at its path instead of at prose.
  - Strength: Converts a rule that depends on careful reading into one that fails loudly. Directly
    unblocks `F-03`, whose whole outcome is deploying without hand-built archives.
  - Tradeoff: Adds a script to a repo that currently has none, and someone must decide where it
    lives and whether `F-03` would rewrite it in CI anyway.
  - Confidence: HIGH — absence of any script verified by search.
  - Blind spot: Whether `F-03` intends to do this in GitHub Actions, which would make a local
    script short-lived.
- **Decision**: ACCEPTED-AS-RISK, handed to F-03 — not fixed here. Recorded as a `Carried forward from F-01 (2026-09-08)` bullet on the roadmap's F-03 item, where `/10x-plan deploy-pipeline` will read it: the three assertions must fail the job non-zero, `Compress-Archive` is unusable on PS 5.1, and packing on Linux avoids the separator problem but not the other two assertions.

### F5 — roadmap.md contradicts itself about F-02 after an out-of-scope edit was committed

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: context/foundation/roadmap.md:62 vs its `### F-02` detail block
- **Detail**: The `## At a glance` table now reads `| F-02 | persistence-spine | … | proposed |`
  while F-02's own detail block still reads `- **Status:** ready`. Only the table row was flipped.
  The edit was made outside this change, surfaced during the Phase 3 commit ritual, and included
  at the user's explicit instruction with a naming line in the commit body — so the *inclusion*
  was a deliberate, informed call. The defect is that the flip was half-applied: the roadmap now
  disagrees with itself about whether the parallel foundation slice is startable, and that
  inconsistency is committed in `49d4092`.
- **Fix**: Set the `### F-02` detail block to `- **Status:** proposed` so both sites agree, or
  revert the table cell to `ready` — whichever matches the actual intent for F-02.
- **Decision**: FIXED — table cell reverted to `ready`, matching the untouched detail block. Root cause was a vocabulary misreading: in this roadmap `ready` means "prerequisites met, /10x-plan can run", not "already implemented" (that is `done`). F-02's prerequisites are `—` and its one unknown is explicitly non-blocking, so `ready` was correct; `ready -> proposed` was also a backward move in an otherwise forward-only lifecycle.

### F6 — "the two variables" lost its antecedent when the heading was renamed

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: TenExCards/AGENTS.md:118-120
- **Detail**: The section now opens "The Linux .NET container supplies `X-Forwarded-Proto`, so
  `Request.IsHttps` is already correct. Setting the two variables together produces a `307` …"
  Nothing in the section names two variables. The antecedent was supplied by the old heading,
  "why neither environment variable belongs here", which this change renamed to "what enforces it,
  and what must never be set", and by a clause removed in edit (b). The variables are named only
  45 lines earlier, under `## Never do these`. This lands directly against Phase 3's manual
  criterion 3.5, "read start to finish as a fresh agent would", which is checked off.
- **Fix**: Name them inline — "Setting `ASPNETCORE_FORWARDEDHEADERS_ENABLED` and
  `ASPNETCORE_HTTPS_PORT` together produces a `307` …".
- **Decision**: FIXED — both variables named inline, and the 307 loop marked as historical, consistent with the F3 rewrite.

### F7 — Program.cs comment states an Azure-only fact unconditionally and contradicts AGENTS.md

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: TenExCards/Program.cs:20-21
- **Detail**: The comment reads "so the middleware would find no port and no-op while logging a
  warning at every startup." Under the `https` launch profile the app binds
  `https://localhost:7209` (`launchSettings.json:17`), so locally the middleware *would* resolve a
  port from `IServerAddressesFeature` and *would* redirect — "would find no port" is true on Azure
  only. Separately, "logging a warning at every startup" contradicts `AGENTS.md`, which attributes
  that line to App Service's plain-HTTP warm-up probe rather than to the middleware's mere
  presence. AGENTS.md already carries the correct and stronger justification ("safe because
  `infra/main.bicep:96` declares `httpsOnly: true`"); the comment offers a weaker one that does not
  survive a move off Azure — which is exactly the scenario the removal decision hinges on.
- **Fix**: Replace the second line with the platform-guarantee reasoning and a pointer to
  `AGENTS.md ### HTTPS`.
- **Decision**: FIXED — comment now cites the `httpsOnly: true` guarantee and names the condition under which the middleware should be reinstated, pointing at `AGENTS.md "### HTTPS"`. Both inaccurate claims removed. Still passes the widened 1.2 gate, which matches `app.UseHttpsRedirection()` rather than the bare identifier.

### F8 — A sixth AGENTS.md edit was made but the plan records five

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: TenExCards/AGENTS.md — `## Conventions`
- **Detail**: "Do not add `Co-Authored-By` trailers." was added to `## Conventions`. The plan
  contracts four edits and records a fifth in an adaptation note; this sixth appears in no contract
  and no note. The content is correct and matches all four commits (zero trailers), and it is
  arguably implied by edit (c) settling the commit convention — but the change's own record
  undercounts its edits, in the file whose accuracy Phase 3 exists to guarantee.
- **Fix**: Extend the Phase 3 adaptation note to mention it, or fold it explicitly into edit (c)'s
  scope.
- **Decision**: FIXED — Phase 3 adaptation note extended to record the sixth edit; the plan's count now matches the file (six edits, not four).

### F9 — NotFound.razor cannot receive focus and has no page title

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: TenExCards/Components/Pages/NotFound.razor:4
- **Detail**: Alone among the four pages it uses `<h3>` where Home, CircuitCheck and Error use
  `<h1>`, and it declares no `<PageTitle>`, so the browser tab falls back to empty.
  `Routes.razor:4` sets `<FocusOnNavigate RouteData="routeData" Selector="h1" />`, so keyboard and
  screen-reader focus lands nowhere on the one page a lost user reaches. It is also the only page
  declaring `@layout MainLayout` explicitly rather than inheriting `DefaultLayout`. This is
  inherited template content copied unchanged — but Phase 1's stated purpose was to prune the
  template's incidental decisions rather than inherit them.
- **Fix**: Add `<PageTitle>Not found</PageTitle>` and promote the heading to `<h1>`.
- **Decision**: FIXED — `<PageTitle>Not found</PageTitle>` added and `<h3>` promoted to `<h1>` so `FocusOnNavigate Selector="h1"` can reach it. Explicit `@layout MainLayout` left in place by choice.

### F10 — Dangling Bootstrap sourcemap turns a devtools 404 into a full Blazor render

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: TenExCards/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css (last line)
- **Detail**: The kept file ends with `/*# sourceMappingURL=bootstrap.min.css.map */` and the
  `.map` was deliberately excluded by the 44→1 prune. The plan anticipated this and judged it
  harmless ("a sourcemap request from an open devtools panel 404s harmlessly"), which was correct
  at planning time. It is now slightly more than harmless: `UseStatusCodePagesWithReExecute`
  converts that 404 into a full re-executed render of `/not-found`, returning `text/html` under a
  `.map` URL — a wasted component render per devtools session and a confusing log line. No other
  dangling asset exists: NavMenu icons and the app.css icon are inline `data:` URIs, and
  `favicon.png` is present.
- **Fix**: Strip the trailing `sourceMappingURL` comment from the kept CSS file (one line).
- **Decision**: FIXED — trailing `sourceMappingURL` comment stripped from the vendored `bootstrap.min.css` (232,803 -> 232,758 bytes). Restoring Bootstrap from upstream later would reintroduce it.

## Forward-looking notes — not findings against this change

These are correctly out of scope here and are recorded so the receiving slice inherits them:

- **SignalR `MaximumReceiveMessageSize` defaults to 32 KB** and is not set. `prd.md` accepts a
  passage "approximately the length of an article or book section", which can exceed that.
  Exceeding it terminates the circuit rather than returning a validation message, presenting to
  the user as the "random disconnect" AGENTS.md teaches readers to blame on OOM — and violating
  "Never let the form freeze." **Unverified**: the exact default on this SDK, whether Blazor form
  binding sends a passage in one hub message, and whether a realistic passage crosses 32 KB.
  `S-02` should measure this rather than assume it, and derive the transport limit and the
  client-side length bound from the same number so they cannot disagree.
- **`UseStatusCodePagesWithReExecute("/not-found")` catches every 4xx**, so once Identity lands a
  **403** will render "the content you are looking for does not exist" instead of a denial. 401 is
  fine — the cookie handler converts it to a login redirect first. `S-01` should branch on status
  code in the same change that adds the auth boundary.
- **Retained-circuit memory** is at defaults (100 circuits, 3-minute retention). `baseline.md`
  already records the uBlock-blocked `disconnect` beacon that makes non-proactive release the
  common case rather than the edge case. Nothing in this change holds avoidable state —
  `CircuitCheck.razor` holds one `int` and is deleted by `S-01`.
- **`AGENTS.md` cites `infra/main.bicep:96` by line number.** Any edit above it silently
  invalidates the citation — and F1 recommends editing exactly that region. Prefer citing the
  property name.
