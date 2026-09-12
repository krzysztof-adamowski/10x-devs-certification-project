<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Merges Deploy Themselves

- **Plan**: `context/changes/deploy-pipeline/plan.md`
- **Scope**: Phases 1–2 of 5 (`4c8e589` p1, `c0102d9` p2)
- **Date**: 2026-09-10
- **Verdict**: REJECTED (at review) → APPROVED (after triage)
- **Findings**: 1 critical, 4 warnings, 5 observations

One narrow fail-open (F1) against the single rule this change exists to enforce. Everything
else is sound: 19 of 20 checkable contract points match the plan literally, and no scope creep
exists in the committed work. F1 is a ~6-line fix, not a rewrite.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS (after triage; WARNING at review) |
| Scope Discipline | WARNING |
| Safety & Quality | PASS (after triage; FAIL at review) |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — A rejected archive is left at the exact path the deploy command reads

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `scripts/pack.py:161-183` (rejection branch), `scripts/pack.py:41-52` (write)
- **Detail**: `build_archive` writes `--out` before `assert_shape` ever runs, and no path removes
  it when assertions fail. Verified empirically: pack a good tree to `--out` (28 entries, 19 under
  `wwwroot/`, 497,970 bytes), then pack a `wwwroot`-less tree to the same `--out` — the script
  exits 1 and prints `ARCHIVE REJECTED`, and `--out` now holds **the rejected archive** (9 entries,
  0 under `wwwroot/`, 109,944 bytes). Two consequences: (a) the very next command in the documented
  flow, `az webapp deploy --src-path TenExCards/bin/publish.zip --type zip`, uploads the rejected
  archive, deploys `RuntimeSuccessful` and then breaks at runtime — the precise fail-open that
  `TenExCards/AGENTS.md` forbids with "Never upload an archive that has not passed all three shape
  assertions"; (b) `zipfile.ZipFile(out, "w")` truncates at open, so a failed pack destroys the
  previous good archive. CI is protected — a non-zero exit fails the job before deploy — so this
  bites the local path, which is exactly the path the prose rule already failed to protect.
- **Fix**: Write to `<out>.tmp`, run `assert_shape` against that, and `os.replace()` onto `--out`
  only when `failures` is empty; leave the rejected temp file in place and name it in the stderr
  text so it can still be inspected.
  - Strength: Makes the guarantee atomic rather than advisory — no window exists in which a
    rejected archive occupies the deploy path, and a failed pack cannot destroy a good archive.
  - Tradeoff: Six lines; the inspect-the-failure workflow moves to a `.tmp` path.
  - Confidence: HIGH — failure reproduced and measured; `os.replace` is atomic on both platforms.
  - Blind spot: None significant.
- **Decision**: FIXED — atomic staging write; `<out>.tmp` promoted via `os.replace()` only when all four assertions pass. Fail-open reproduced before and proven closed after.

### F2 — `4xx`-is-definitive re-introduces the false red it was meant to avoid, for `429`/`403`

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: `scripts/verify_deploy.py:104-110`
- **Detail**: The plan's contract says "Fetches `/` with retry and backoff until it returns `200`
  or the budget expires." The implementation instead fails immediately on any `4xx`, retrying only
  transport failures and `5xx`. The reasoning holds for the failure the plan actually names — a
  restarting container emits connection resets and `503`, never `404` — and it makes negative test
  3 return in under a second instead of burning 120s. But the justifying comment ("the app
  answered, so waiting changes nothing") overstates: App Service returns **`429`** under
  front-end throttling, which is transient by definition, and **`403`** (`This web app is stopped`)
  during a platform stop or tier operation. For those two, waiting *does* change the outcome.
  Failure direction is safe — false red, never false green.
- **Fix A ⭐ Recommended**: Retry `403` and `429` like `5xx`; keep the immediate fail for every
  other `4xx`.
  - Strength: Closes the only two statuses where the comment is factually wrong, keeps the fast
    fail on `404` that makes the negative test instant, and is a two-line change.
  - Tradeoff: One more branch; a genuinely stopped site now costs the full warm-up budget.
  - Confidence: HIGH — both statuses are documented, transient App Service front-end responses.
  - Blind spot: Haven't measured how often `429` actually occurs on a B1 single-worker plan.
- **Fix B**: Accept as risk; correct the overstated comment only.
  - Strength: Zero behaviour change; the failure direction is already safe and a spurious red is
    one `gh run rerun` away.
  - Tradeoff: Leaves a known-wrong justification in the code that a future reader will trust.
  - Confidence: MEDIUM — depends on throttling never landing on the verification step.
  - Blind spot: Phase 4 runs this on every merge, so exposure scales with merge frequency.
- **Decision**: FIXED via Fix A — `TRANSIENT_4XX = {403, 429}` now retried like 5xx; every other 4xx still fails immediately. 404 negative test still returns in 0s.

### F3 — Redirects are followed silently, so the script verifies whatever page it lands on

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `scripts/verify_deploy.py:65-73`, `scripts/verify_deploy.py:100-103`
- **Detail**: `urllib.request.urlopen` follows 301/302/303/307 through the default opener, and
  `fetch()` returns only `(status, body)` — `response.url` is discarded, so a redirect is invisible
  in the log and in the verdict. This is latent today and becomes live at `S-01`: once Identity
  lands, `/` redirects to `/Account/Login`, and the script will print
  `[200] https://tenexcards-ka.azurewebsites.net/`, verify the **login page's** assets, and report
  `DEPLOY VERIFIED -- root page 200`. Nobody will have checked the app root. That is a false green
  in a script whose whole purpose is that a green result means something.
- **Fix**: Return `response.url` from `fetch()`, print it whenever it differs from the requested
  URL, resolve assets against the landed URL rather than `base`, and fail if the root redirects
  off-origin.
  - Strength: Removes the false-green class entirely and makes the log self-describing; costs
    nothing today because nothing currently redirects.
  - Tradeoff: A few lines in `fetch()` and both call sites.
  - Confidence: HIGH — `urllib`'s redirect handling is default-on and well documented.
  - Blind spot: Whether Identity will redirect the root at all is an `S-01` design decision.
- **Decision**: FIXED — `fetch()` returns the landed URL, redirects are logged, assets resolve against the landed page, and a redirect to a different host fails. Narrowed from the proposed fix: an http→https upgrade on the same host is `httpsOnly` working and is logged, not failed. Verified live.

### F4 — Assets returning 200 proves files were deployed, not that a Blazor circuit works

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `scripts/verify_deploy.py:42-62`, whole script
- **Detail**: The collected asset set is *complete for today's markup* — all five live assets are
  reached via `link[rel~=stylesheet][href]` + `script[src]`, including
  `ReconnectModal.*.razor.js`, which is `<script type="module">` and is picked up because the
  collector ignores `type`. The gap is what the check *proves*. `_framework/blazor.web.js` is a
  static file; it returns 200 whether or not interactive server rendering is wired and whether or
  not the WebSocket transport works. The root page is static-rendered by definition, so a deploy in
  which every circuit is dead passes this verification fully green — and Blazor Server is the
  entire product. Measured: `POST /_blazor/negotiate?negotiateVersion=1` is mapped and reachable
  (a GET returns `405`, confirming the route exists).
- **Fix**: Add one `POST /_blazor/negotiate?negotiateVersion=1` asserting `200` and a body
  containing `connectionToken`.
  - Strength: Converts "the files are there" into "the circuit endpoint is live" for a few lines
    and one request; durable, unlike `CircuitCheck.razor`, which `S-01` deletes.
  - Tradeoff: Slightly beyond the plan's literal contract for this script; couples the verifier to
    a Blazor internal endpoint whose negotiate shape could change across major versions.
  - Confidence: MEDIUM — endpoint reachability measured, but the 200-plus-`connectionToken`
    response shape was not asserted end to end.
  - Blind spot: Whether a negotiate succeeding actually implies a circuit can be *held* under B1's
    WebSocket limits — it does not prove that.
- **Decision**: SKIPPED — the plan's contract for this script is the root page and its assets; circuits carry no product behaviour until `S-01`. Recorded in follow-ups/review-fixes.md with the measured negotiate-endpoint detail if revisited.

### F5 — The working tree carries another change's infrastructure edits into phases that stage by directory

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Scope Discipline
- **Location**: `infra/main.bicep` (uncommitted, ~150 lines), `context/foundation/roadmap.md`,
  `context/changes/persistence-spine/` (untracked)
- **Detail**: Not deploy-pipeline creep — the committed work is exactly the three planned files —
  but a live hazard. `infra/main.bicep` holds an uncommitted SQL server, two databases, a Key
  Vault, a system-assigned identity and a role assignment belonging to `persistence-spine` (F-02),
  being implemented concurrently. The plan permits only a *header-comment* edit to that file in
  Phase 5 §6 and states the diff must be comment-only; a careless `git add infra` during Phase 5
  would fold an entire other change's infrastructure into a `(p5)` commit and silently violate that
  rule. Phase 1's own pre-flight asked for a clean tree apart from this change's folder, and it was
  not clean.
- **Fix**: Before Phase 5, either land the F-02 work under its own change or confirm the file is
  clean; in Phase 5 §6, stage `infra/main.bicep` only after `git diff --cached -- infra/main.bicep`
  shows a comment-only diff.
  - Strength: Turns the plan's "diff is comment-only" success criterion into a pre-commit check
    rather than a post-hoc hope.
  - Tradeoff: One extra verification step at Phase 5.
  - Confidence: HIGH — the two changes provably touch the same file.
  - Blind spot: The concurrent session's timing is not under this change's control.
- **Decision**: FIXED + ACCEPTED-AS-RULE: "Two changes in flight against one file: verify the staged diff, not the file" — appended to context/foundation/lessons.md, plus an explicit Phase 5 §6 pre-commit guard in follow-ups/review-fixes.md.

### F6 — `pack.py`'s docstring and plan criterion 2.4 understate what a nested tree fails

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `scripts/pack.py:64-66`, vs `scripts/README.md:51-53`
- **Detail**: The docstring says a `publish/`-nested tree "violates assertions 1 and 2 at once". It
  violates **1, 2 and 4** — a nested entry is `publish/wwwroot/app.css`, which fails
  `n.startswith("wwwroot/")`. The README is correct; the docstring is stale, inherited verbatim
  from the plan, whose criterion 2.4 carries the same error. Behaviour is right and 2.4 is
  satisfied (it names 1 and 2, plus one more) — but it is prose contradicting executable output in
  a change whose thesis is that executable output replaces unreliable prose.
- **Fix**: Correct the docstring to "1, 2 and 4"; note the correction in the Phase 5 record rather
  than editing the plan's read-only phase block.
- **Decision**: FIXED — `assert_shape` docstring now says "1, 2 and 4" and explains why. Plan text and criterion 2.4 carry the same understatement; queued for the Phase 5 record rather than editing read-only phase blocks.

### F7 — The README states CI parity that does not exist yet

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `scripts/README.md:8-10`
- **Detail**: "CI calls these same two scripts with these same defaults" — there is no `.github/`
  directory; Phase 4 adds it. This is plan-authorised (Phase 2 §3's contract explicitly requires
  the statement), but it sits directly under the README's own thesis that "the script is right and
  the markdown is stale", and right now the markdown is ahead of the code. Every other README claim
  was checked and is true.
- **Fix**: Reword to "CI (from Phase 4) calls…", or move the paragraph into Phase 4's commit.
- **Decision**: DEFERRED — plan-authorised and self-resolving at Phase 4; queued in follow-ups/review-fixes.md to confirm once deploy.yml exists.

### F8 — Asset requests get zero retries while the root page gets 120 seconds

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `scripts/verify_deploy.py:204-216`
- **Detail**: The docstring argues that false reds "train everyone to ignore" the pipeline, then
  the asset loop hard-fails on the first transport hiccup. On a B1 container that just restarted,
  the root can legitimately 200 on attempt 3 while a following asset request still hits a reset or
  the 30s timeout. Low likelihood — once the root responds the process is warm and assets are
  static — but it is the same false-red class the warm-up loop exists to prevent.
- **Fix**: Allow one or two retries per asset against the remaining warm-up budget.
- **Decision**: DEFERRED — recorded in follow-ups/review-fixes.md. Low likelihood: once the root responds the process is warm and assets are static.

### F9 — I/O failures surface as tracebacks instead of the script's own error style

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `scripts/pack.py:45-52`, `scripts/pack.py:69`
- **Detail**: `zf.write()` can raise `PermissionError`/`OSError` — on Windows, a locked
  `TenExCards.dll` from a running `dotnet run`, or an AV scanner — and `zipfile.ZipFile(out_path)`
  can raise `BadZipFile`. Both produce a raw traceback rather than the clean `error: …` message
  and exit code 2 the script uses elsewhere, which is the difference between a CI log that names
  the fix and one that names a stack. Related and lower value: `fetch()` in `verify_deploy.py`
  always reads the full body even where the asset loop discards it.
- **Fix**: Wrap both boundaries and route them through the existing message format and exit code 2.
- **Decision**: DEFERRED — recorded in follow-ups/review-fixes.md. Not a live defect; affects error presentation, not correctness.

### F10 — `.replace("\\", "/")` is the one input that could make assertion 3 lie

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `scripts/pack.py:50`
- **Detail**: On Windows this is redundant with the `os.sep` replace. On Linux CI it silently
  rewrites a legal filename containing a literal backslash into a path separator, producing an
  archive entry at a different path than the file on disk — while assertion 3 reports PASS. Cannot
  occur with `dotnet publish` output, so it is not a live defect. It matters only because
  assertion 3 is otherwise a tautology given this writer, and this is the single input that would
  make it report a falsehood.
- **Fix**: On POSIX, raise on a backslash in a filename rather than normalising it.
- **Decision**: DEFERRED — recorded in follow-ups/review-fixes.md. Cannot occur with dotnet publish output.

## Verified and explicitly NOT findings

Recorded so they are not re-raised in a later review:

- The zipfile in `assert_shape` is closed (`with` block; `namelist()` materialised inside).
- The backoff loop cannot spin hot — `wait = min(delay, remaining)` is guarded by the
  `remaining <= 0` check; `--warmup-seconds 0` gives one attempt then a clean fail.
- `except Exception` at `verify_deploy.py:112` does not swallow the definitive-4xx verdict:
  `fail()` raises `SystemExit`, a `BaseException`.
- No SSRF surface — only absolute same-origin URLs are fetched; `data:`, `javascript:` and
  protocol-relative `//evil.com/x.js` all differ in netloc and are skipped.
- No secrets and no injection surface — neither script invokes a shell; the only hardcoded host is
  the documented public one and is overridable.
- Case sensitivity of `"TenExCards.dll"` is correct on both platforms — the csproj sets no
  `<AssemblyName>` override.
- Directory entries, symlinks and ZIP64 are non-issues: `os.walk` yields files only, so no `dir/`
  entries are ever written.
- `pack.py` correctly omits the `deploy-plan.md` step-5 "no native `TenExCards` executable" check,
  as the plan explicitly required — carrying it over would have failed every Linux CI build.
- Blazor's not-found path returns a real `404`, not a 200 error page, so negative test 3 is
  measuring what it claims to measure.

## Success criteria re-verification

All Phase 1 and Phase 2 automated criteria were re-run independently from the committed state and
pass: branch state (`main`, `origin/main`, default `main`, no `origin/master`), the single
surviving `master` reference describing a settled rename, `dotnet publish -c Release`, `pack.py`
exit 0 with four PASS, `verify_deploy.py` exit 0 with five same-origin assets at 200, and the 404
negative test exiting 1.

All five manual rows (1.6, 1.7, 2.9, 2.10, 2.11) carry explicit user confirmation in the
implementation session — no rubber-stamping.
