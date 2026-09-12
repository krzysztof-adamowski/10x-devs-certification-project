# Merges Deploy Themselves — Implementation Plan

> Roadmap item `F-03` (`deploy-pipeline`). Prerequisite `F-01` (`blazor-server-shell`) is done and
> live.

## Overview

Replace the hand-assembled deploy ritual with a GitHub Actions workflow that builds on Linux, packs
through a committed script that fails non-zero on any archive-shape violation, deploys via OIDC with
no stored credential, verifies the live page *and every asset it references*, and retains the
archive so a rollback candidate is a retained artifact rather than a git-ignored file on one laptop.

The change also closes finding `F4` from `../blazor-server-shell/reviews/impl-review.md`, which was
accepted-as-risk and handed here: the archive-shape rule currently exists only as prose in
`TenExCards/AGENTS.md`, and its failure mode is that a wrong archive **deploys successfully** and
then serves a page whose every asset 404s.

## Current State Analysis

**What exists.** `infra/main.bicep` provisions a B1 Linux App Service plan in `polandcentral`;
`https://tenexcards-ka.azurewebsites.net` is live and serving the Blazor Server shell from a
manual `az webapp deploy` run on 2026-09-08. `context/deployment/deploy-plan.md` is the
authoritative record of what was run.

**What is missing.**

- No `.github/` directory of any kind — zero CI, despite `context/foundation/tech-stack.md`
  recording `ci_provider: github-actions` and `ci_default_flow: auto-deploy-on-merge` as decisions
  taken. They are unwired, not unchosen.
- No script of any kind in the repo. No `.sln`, no `global.json`, one csproj at
  `TenExCards/TenExCards.csproj` (`net10.0`, framework-dependent, no RID).
- No Azure credential usable by CI. No service principal, app registration, or federated credential
  has ever been created; the subscription is Free Trial with `spendingLimit: On`.
- No rollback path that survives one laptop. Today's rollback depends on
  `TenExCards/bin/publish-scaffold-rollback.zip`, which is git-ignored.

**Constraints discovered.**

- **`zip` is not installed on the development machine.** Git Bash ships `unzip` but not `zip`. A
  shell packer would satisfy CI and silently fail the locally-runnable half of the requirement.
  **Python 3.10.11 with `zipfile` is present locally and preinstalled on `ubuntu-latest`.**
- **`gh` 2.98.0 is installed**, so the branch rename, default-branch change, and secret creation are
  scriptable rather than portal work.
- Branch is `master`; `tech-stack.md` and the roadmap both say `main`.
  `context/deployment/deploy-plan.md:228` explicitly parks the inconsistency "to settle when CI is
  actually built, not now."

## Desired End State

A commit pushed to `main` produces, without any human action: a Release build, an archive that has
passed every shape assertion, a retained artifact, a deploy to `tenexcards-ka`, and a verification
pass that fetches the rendered root page and asserts a `200` for every stylesheet and script the
page references. Any failure in that chain fails the workflow run — red, not silently green.

Every deployed archive is retained for 90 days, so a rollback candidate always exists off the
development machine and is known shape-valid — only a passing `pack.py` can produce one. Restoring a
retained artifact is a documented manual procedure, not a workflow path; automating it is deferred
(see `## What We're NOT Doing`).

Verified by: opening the run in GitHub Actions and seeing every step green, then confirming the live
site serves the commit that triggered it.

### Key Discoveries:

- **Python is the only packer language available on both machines.** `zip` is absent from Git Bash
  (`which zip` → not found; `unzip` present). Python's `zipfile` writes `/`-separated entry names
  when arcnames are constructed as POSIX paths — precisely the property Windows PowerShell 5.1's
  writer lacks, which is why `TenExCards/AGENTS.md` forbids `Compress-Archive`.
- **A latent false-positive in the existing record.** `context/deployment/deploy-plan.md` step 5
  asserts *"There must be no native `TenExCards` executable or `*.so` pile; that would mean a
  self-contained publish."* On Windows the apphost is `TenExCards.exe`, so the check passes. On a
  Linux runner a **framework-dependent** publish emits an extensionless ELF named `TenExCards`, and
  that assertion fires falsely on a correct archive. Confirmed against the current local publish
  output, which contains `TenExCards.exe` alongside `TenExCards.dll`.
- **`--track-status true` is a known hang.** `deploy-plan.md` records that it "hangs on Pending" as
  a Linux CLI issue where the site is already live. A hang in CI is a job that burns its timeout
  rather than reporting.
- **A green deploy command is not evidence.** The 2026-09-08 record deployed `RuntimeSuccessful` and
  was only trusted after checking that every `.css`/`.js` URL the rendered root references returned
  `200` on the live host.
- **B1 has no deployment slots**, so there is no platform rollback to lean on
  (`TenExCards/AGENTS.md`, `### Scaling past one worker`).
- The publish output has nested paths (`wwwroot/…`), which is why the separator hazard is live at
  all — the 2026-08-31 scaffold archive had eleven bare filenames and no subdirectories.

## What We're NOT Doing

- **Not deploying `infra/main.bicep` from CI**, and not running `what-if` from CI. Infrastructure
  stays a deliberate manual `az deployment group create` with the snapshot-deploy-diff ritual,
  because `TenExCards/AGENTS.md` requires human judgment on every predicted deletion — phantoms and
  real deletions are indistinguishable in the output.
- **Not touching application settings.** The workflow never runs `az webapp config appsettings set`,
  and never adds an `appSettings` block anywhere.
- **Not adding a `pull_request` trigger.** Commits land directly on the default branch; a PR job
  would ship untested. Add it the day PRs start.
- **Not adding a test job.** No test project exists; `TenExCards/AGENTS.md` states one is created
  with the first feature that touches generation, triage, or persistence — that is `S-01`, not here.
- **Not adding `-warnaserror`**, action SHA pinning, path filters, deployment slots, staging
  environments, container builds, or Application Insights.
- **Not changing `infra/main.bicep`'s resources, properties, or parameters.** Phase 5 §6 corrects a
  stale `az webapp deploy` command in its *header comment* — no template change, nothing deployed.
- **Not automating redeploy of a prior run's artifact**, and therefore not drilling a rollback
  against production in this change. `roadmap.md` states F-03's outcome as build, package, deploy,
  and *artifact retained* — redeploy-by-dispatch appears only under `Unlocks`, as rationale. Adding
  it would mean a `run_id` input, `actions: read`, a conditional download-artifact branch, and a
  drill that deliberately mutates production; retention alone delivers the stated outcome and the
  restore procedure is documented in Phase 5 §2. Add the automated path the day a rollback is needed
  under time pressure, not before.
- **Not deleting `TenExCards/bin/publish-scaffold-rollback.zip`.** It stays as a local
  belt-and-braces until the pipeline has proven itself.

## Implementation Approach

Build the pieces in dependency order, and make each one provable before the next depends on it.

The two scripts come before any CI, so they are debugged against a local publish and the
already-live site rather than through the slow feedback loop of a failing workflow run. The Azure
identity comes before the workflow, so the workflow's first run fails for application reasons if at
all, never for authentication. The branch rename comes first of all, because the workflow's trigger
names a branch and renaming afterwards means editing a file that has already run.

Both scripts are Python and both take arguments with defaults matching the documented paths, so the
same invocation works in CI and on the development machine. The workflow is a thin caller: every
assertion it makes lives in a script a human can run.

## Critical Implementation Details

**Ordering: the branch rename must precede the workflow file.** A workflow whose
`on: push: branches: [main]` lands while the default branch is still `master` never runs, and the
absence of a run is indistinguishable from a passing one in the branch UI.

**The OIDC subject is exact-match and includes the branch name.** A federated credential issued for
`refs/heads/main` will not authenticate a run on any other ref, including a re-run triggered before
the rename completes. This is a feature, not an obstacle — but it means Phase 1 genuinely blocks
Phase 3.

**The deploy step must not use `--track-status true`.** The record documents it hanging on "Pending"
while the site is already live. Pass `--track-status false` and let the verification script be the
signal; put a `timeout-minutes` on the step as a backstop.

**The verification script needs a warm-up loop.** The container restarts after a deploy, so the first
request can fail or time out on a correct deploy. Retry with backoff before failing, or the pipeline
produces false reds that train everyone to ignore it.

**STOP-AND-ASK: the Azure CLI session.** The last recorded `az` work in this repo was 2026-09-01;
the session may well have expired. Before the first `az` command in Phase 3, run the session gate
below. **If it fails, do not attempt `az login` yourself and do not retry the failing command** —
`az login` opens a browser for interactive authentication, which an agent cannot complete. Stop,
tell the user the session has expired, and ask them to run `az login` in their own terminal, then
resume. This is a manual gate in the same sense as the ones `deploy-plan.md` already records.

```powershell
# Session gate — run before any other az command in Phase 3.
az account show --query "{name:name, id:id, user:user.name, state:state}" -o table
```

A non-zero exit, or any output containing `Please run 'az login'`, `AADSTS`, or
`refresh token has expired`, means the session is gone. Ask; do not improvise.

**Command conventions used below.** Blocks are Windows PowerShell 5.1, which is the user's shell.
Two consequences appear repeatedly and are not stylistic:

- `@{u}` must be quoted as `'@{u}'` — unquoted, PowerShell parses `@{` as a hashtable literal.
- JSON is never passed inline to `az`. PowerShell 5.1 mangles nested quotes on the way to the
  process. Write it to a file with `ConvertTo-Json` and pass `--parameters "@<file>"`, using
  `-Encoding ascii` — PowerShell 5.1's `utf8` writes a BOM that `az`'s JSON parser rejects.

---

## Phase 1: Rename `master` to `main`

### Overview

Settle the parked branch-name inconsistency before anything triggers on a branch name. Small, but it
gates Phases 3 and 4.

### Changes Required:

#### 1. The remote and local branch

**File**: git refs — no file in the working tree

**Intent**: Bring the repository in line with the two foundation documents that already say `main`,
rather than introducing a third spelling by pointing CI at `master`.

**Contract**: `main` exists locally and on `origin`, is the GitHub default branch, tracks
`origin/main`, and `origin/master` is deleted. `gh` 2.98.0 is installed, so the default-branch
change is a command rather than a portal visit. Order matters: create and push `main`, change the
default on GitHub, *then* delete `master` — deleting first orphans the default branch.

**Commands** (PowerShell):

```powershell
# Pre-flight: working tree must be clean apart from this change's own folder.
git status --short

git branch -m master main
git push -u origin main
gh repo edit --default-branch main
git push origin --delete master
git fetch --prune
```

If `gh repo edit` reports insufficient scope, run `gh auth refresh -h github.com -s repo` — that is
interactive but stays in the terminal, so it is not a stop-and-ask gate.

#### 2. The one document that records the inconsistency

**File**: `context/deployment/deploy-plan.md` (line 228, in `## Out of scope`)

**Intent**: That sentence parks the question "to settle when CI is actually built, not now." It is
being settled now, so the sentence must stop describing an open item.

**Contract**: The `## Out of scope` paragraph no longer names an unresolved branch inconsistency. CI
itself also leaves that section, since it is no longer out of scope — it is this change. Leave the
rest of the historical record untouched; it describes what was run on a given date.

### Success Criteria:

#### Automated Verification:

```powershell
git rev-parse --abbrev-ref HEAD                                    # expect: main
git rev-parse --abbrev-ref --symbolic-full-name '@{u}'             # expect: origin/main
gh repo view --json defaultBranchRef -q .defaultBranchRef.name     # expect: main
git ls-remote --heads origin master                                # expect: no output
git log --oneline -1                                               # expect: c9afde3 or later, unchanged
```

Note the quoting on `'@{u}'` — unquoted, PowerShell parses `@{` as a hashtable literal and the
command fails with a parse error rather than a git error.

- `git rev-parse --abbrev-ref HEAD` prints `main`
- Upstream tracking resolves to `origin/main`
- `gh repo view` reports `main` as the default branch
- `git ls-remote --heads origin master` returns no output
- Searching the markdown for `master` returns no line describing an unsettled branch name. This
  change's own folder is excluded, because the plan text necessarily discusses the rename; outside
  it there is exactly one hit today, in `deploy-plan.md`:

  ```powershell
  Get-ChildItem -Path context,TenExCards -Recurse -Filter *.md |
    Select-String -Pattern 'master' |
    Where-Object { $_.Path -notlike '*\context\changes\deploy-pipeline\*' }
  ```

#### Manual Verification:

- The GitHub repository page shows `main` as the default branch and no stale `master` branch
- No open PR or external link depended on `master`

**Implementation Note**: After completing this phase and all automated verification passes, pause
for manual confirmation before proceeding.

---

## Phase 2: Committed pack-and-verify scripts

### Overview

Turn two prose rules into two executable scripts that exit non-zero on failure. This is the phase
that closes finding `F4`. Both are proven locally — against the current publish output and against
the already-live site — before any CI exists to hide their behaviour.

### Changes Required:

#### 1. The packer

**File**: `scripts/pack.py`

**Intent**: Build the deployable archive from a publish directory and refuse to produce one that
violates the recorded shape rules. Replaces the prose in `TenExCards/AGENTS.md` as the authority on
how the archive is built.

**Contract**: Takes `--publish-dir` (default `TenExCards/bin/Release/net10.0/publish`, the stock
`dotnet publish -c Release` location, already covered by `TenExCards/.gitignore:12:[Bb]in/`) and
`--out` (default `TenExCards/bin/publish.zip`, the documented artifact path). Writes every entry
with a POSIX-separated arcname relative to the publish directory root, so contents sit at the
archive root with no wrapper directory.

Then asserts, against the archive it just wrote — reading it back rather than trusting its own
write:

1. `TenExCards.dll` is present at the archive root
2. no entry name begins with `publish/`
3. no entry name contains a backslash
4. at least one entry exists under `wwwroot/`

Evaluates **all four** assertions and prints each with its result before exiting — it does not stop
at the first failure. This matters: a `publish/`-nested tree violates assertions 1 and 2 at once, so
a fail-fast script would name only assertion 1 and hide the more diagnostic one. Also prints the
archive's entry count and byte size. Exits `0` only if all four pass; exits non-zero naming **every**
failing assertion otherwise. The fourth assertion is new
— a cheap guard against a publish that lost its static assets, which is the same class of silent
failure as the other three.

Python rather than shell: `zip` is not present in Git Bash on the development machine, so a shell
packer would work in CI and fail the locally-runnable requirement. `zipfile` is in the standard
library on both.

Do **not** carry over `deploy-plan.md` step 5's "no native `TenExCards` executable" check. On Linux a
framework-dependent publish legitimately emits an extensionless apphost by that name; the check is a
Windows-only artifact and would fail every CI build. Phase 5 corrects the record.

#### 2. The deploy verifier

**File**: `scripts/verify_deploy.py`

**Intent**: Decide whether a deploy actually worked, rather than whether the deploy *command*
succeeded. Encodes the check that made the 2026-09-08 deploy trustworthy.

**Contract**: Takes a base URL (default `https://tenexcards-ka.azurewebsites.net`) and an optional
warm-up budget. Fetches `/` with retry and backoff until it returns `200` or the budget expires.
Parses the returned HTML for `href` on stylesheet links and `src` on scripts, resolves each against
the base URL, discards any that resolve off-origin, and requests each one asserting `200`. Prints
every URL checked with its status. Exits non-zero naming the first failure, or on warm-up timeout.

Same-origin filtering matters because an off-origin CDN failure is not this deploy's fault and must
not fail the run. The warm-up loop matters because the container restarts after a deploy, so a cold
first request is expected on a correct deploy — without it the pipeline produces false reds.

Standard library only (`urllib`, `re`, `html.parser`); no dependency install step in CI.

#### 3. Script directory conventions

**File**: `scripts/README.md`

**Intent**: Say what these two scripts are for and that they are the authority the documentation
points at, so the next person does not reconstruct them from prose — the exact failure this phase
exists to prevent.

**Contract**: Names each script, its default arguments, how to run it locally on Windows, and states
that CI calls these same scripts with the same defaults.

### Success Criteria:

#### Automated Verification:

```powershell
# Happy path — run from the repo root.
dotnet publish TenExCards/TenExCards.csproj -c Release
python scripts/pack.py
if ($LASTEXITCODE -ne 0) { throw "pack.py failed" }

# Read the archive back independently of the script's own report.
python -c "import zipfile; n=zipfile.ZipFile('TenExCards/bin/publish.zip').namelist(); print('dll at root:', 'TenExCards.dll' in n); print('backslashes:', any('\\' in e for e in n)); print('publish/ prefix:', any(e.startswith('publish/') for e in n)); print('wwwroot entries:', sum(1 for e in n if e.startswith('wwwroot/')))"

# Negative test 1 — contents nested under publish/ must fail assertion 2.
$tmp = Join-Path $env:TEMP "pack-neg1\publish"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
Copy-Item -Recurse -Force "TenExCards/bin/Release/net10.0/publish/*" $tmp
python scripts/pack.py --publish-dir (Split-Path $tmp) --out "$env:TEMP\neg1.zip"
if ($LASTEXITCODE -eq 0) { throw "negative test 1 passed when it must fail" }

# Negative test 2 — missing static assets must fail assertion 4.
$tmp2 = Join-Path $env:TEMP "pack-neg2"
New-Item -ItemType Directory -Force -Path $tmp2 | Out-Null
Copy-Item -Recurse -Force "TenExCards/bin/Release/net10.0/publish/*" $tmp2
Remove-Item -Recurse -Force (Join-Path $tmp2 "wwwroot")
python scripts/pack.py --publish-dir $tmp2 --out "$env:TEMP\neg2.zip"
if ($LASTEXITCODE -eq 0) { throw "negative test 2 passed when it must fail" }

Remove-Item -Recurse -Force "$env:TEMP\pack-neg1","$env:TEMP\pack-neg2","$env:TEMP\neg1.zip","$env:TEMP\neg2.zip" -ErrorAction SilentlyContinue

# Verifier against the currently-live site, then against a host that 404s.
python scripts/verify_deploy.py
if ($LASTEXITCODE -ne 0) { throw "verify_deploy.py failed against the live site" }
python scripts/verify_deploy.py --base-url "https://tenexcards-ka.azurewebsites.net/no-such-path"
if ($LASTEXITCODE -eq 0) { throw "verifier passed against a 404 host" }

# Commit the scripts under this phase. Phase 4 commits only `.github`, so the `(p<N>)`
# suffix keeps tracing each commit to the phase that authorised it.
git add scripts
git commit -m "feat(deploy-pipeline): pack and verify scripts enforce the archive shape (p2)"
git push
```

- `dotnet publish` succeeds
- `python scripts/pack.py` exits `0` and reports all four assertions passing
- Reading the zip back independently confirms `TenExCards.dll` at the root, no backslash, no
  `publish/` prefix, and a non-zero `wwwroot/` entry count
- Negative test 1: a `publish/`-nested tree exits non-zero naming assertions 1 and 2
- Negative test 2: an emptied `wwwroot/` exits non-zero naming assertion 4
- `python scripts/verify_deploy.py` against the live site exits `0` listing more than one asset URL
- Negative test 3: verification against a path that 404s exits non-zero
- `scripts/` is committed under a `(p2)` message, not folded into Phase 4's commit

#### Manual Verification:

- `scripts/pack.py` output reads clearly enough that a failure tells you what to fix, not just that
  something failed
- The asset list `verify_deploy.py` prints matches what the browser's network tab loads for `/`
- Both scripts run from the repo root on Windows without needing `zip`, WSL, or PowerShell 7

**Implementation Note**: After completing this phase and all automated verification passes, pause
for manual confirmation before proceeding.

---

## Phase 3: Azure OIDC identity for CI

### Overview

Create the workload identity the workflow will authenticate with, so that no long-lived credential
is ever stored in GitHub. Several steps are human-only, matching the manual-gate pattern
`deploy-plan.md` already uses.

### Manual gates

| Gate | Who | Why |
| --- | --- | --- |
| `az login` | **Human only — STOP AND ASK** | Interactive browser authentication. The agent cannot complete it and must not attempt it. |
| Entra app registration | Agent, may be refused | Creates a directory object. Some tenants restrict this to admins — see the contingency. |
| Role assignment | Agent, may be refused | Requires Owner or User Access Administrator on the subscription. |
| Publish-profile fallback | **Human only — STOP AND ASK** | `az webapp deployment list-publishing-profiles` is `deny`-listed in `.claude/settings.json`. Read-only against Azure, but it prints a live credential, and an agent's stdout is logged. Only reached if the app registration is refused — see the contingency. |

**Run the session gate first, before anything else in this phase:**

```powershell
az account show --query "{name:name, id:id, user:user.name, state:state}" -o table
```

If that command exits non-zero, or its output mentions `Please run 'az login'`, `AADSTS`, or an
expired refresh token, then **stop and ask the user to run `az login` in their own terminal.** Do
not run `az login` yourself, do not run `az login --use-device-code` as a workaround, and do not
retry the failing command hoping the session recovers. Say plainly that the session has expired,
give them the command, and wait. The last recorded `az` work in this repo was 2026-09-01, so an
expired session is the expected case rather than an anomaly.

Once the gate passes, set the shared variables the rest of this phase uses:

```powershell
$RG     = "rg-tenexcards-plc"
$WEBAPP = "tenexcards-ka"
$APPNAME= "gh-tenexcards-deploy"
$REPO   = "krzysztof-adamowski/10x-devs-certification-project"
$SUB    = az account show --query id -o tsv
$TENANT = az account show --query tenantId -o tsv
$SCOPE  = "/subscriptions/$SUB/resourceGroups/$RG"

# Sanity: the resource group this scope names must actually exist.
az group show -n $RG --query "{name:name, location:location}" -o table
```

### Changes Required:

#### 1. App registration, service principal, and role assignment

**File**: Azure — no file in the working tree

**Intent**: Give GitHub Actions an identity that can deploy to one web app and nothing else.

**Contract**: An Entra application (suggested display name `gh-tenexcards-deploy`) with a service
principal, holding the `Contributor` role scoped to the **resource group**
`/subscriptions/<sub-id>/resourceGroups/rg-tenexcards-plc` — not the subscription. Records the
`appId` (client id), `tenantId`, and `subscriptionId`.

Resource-group scope is the smallest scope that permits `az webapp deploy`; subscription scope would
let a compromised workflow reach resources this change has no business touching.

**Commands:**

```powershell
$APPID = az ad app create --display-name $APPNAME --query appId -o tsv
if (-not $APPID) { throw "app registration refused - see the contingency below" }
az ad sp create --id $APPID | Out-Null

# Directory replication lags; a role assignment issued immediately can fail with
# "PrincipalNotFound". Retry a few times before treating it as a real refusal.
$ok = $false
foreach ($i in 1..6) {
  az role assignment create --assignee $APPID --role Contributor --scope $SCOPE 2>$null
  if ($LASTEXITCODE -eq 0) { $ok = $true; break }
  Start-Sleep -Seconds 10
}
if (-not $ok) { throw "role assignment failed after retries" }
```

`PrincipalNotFound` in the first seconds after `az ad sp create` is replication lag, not a
permission problem — which is why the loop exists. A persistent authorization error is a different
thing and triggers the contingency.

**Contingency, with a defined trigger and a defined fallback.** If the tenant blocks application
registration for non-administrators — `az ad app create` fails with an authorization error rather
than a transient one — do not escalate and do not retry: fall back to a publish profile secret.

```powershell
# Fallback path only. Skip entirely if the OIDC path above succeeded.
$prof = Join-Path $env:TEMP "tenexcards-publish-profile.xml"
az webapp deployment list-publishing-profiles -g $RG -n $WEBAPP --xml | Set-Content -Path $prof -Encoding ascii
gh secret set AZURE_WEBAPP_PUBLISH_PROFILE --body (Get-Content $prof -Raw)
Remove-Item $prof -Force
```

That profile is a live credential — delete the temp file immediately, as above, and never echo it.
Record which path was taken and why in Phase 5's deployment record.

**STOP-AND-ASK: the fallback block above is human-only.**
`az webapp deployment list-publishing-profiles` is on the `deny` list in `.claude/settings.json`,
alongside `az account get-access-token`. An agent cannot run it, **must not attempt it, and must not
route around it** — not via `--query`, not by redirecting to a file, not through a wrapper script.
Stop, tell the user the fallback is agent-blocked, and hand them the block to run in their own
terminal. This is a manual gate in exactly the sense `az login` is, and for a stronger reason.

The verb says `list`; the payload is a working credential. The profile carries `userName`,
`userPWD`, and the SCM/Kudu `publishUrl`, and those grant push of arbitrary code, read access to
app settings, filesystem browsing, and command execution on the instance. It is read-only against
Azure — nothing rotates, nothing breaks — so the rule is not about mutation. It is about
**exfiltration**: anything an agent prints lands in conversation history and tool logs, and a
credential shown once cannot be unshown. That is also why the two commands sit together under
`deny` — both are read verbs that emit credentials.

Consequence for this phase: on the fallback branch the agent completes every step it can, then
stops before the profile is read. The user runs the four lines above themselves and confirms
`AZURE_WEBAPP_PUBLISH_PROFILE` is set; the agent resumes at the fallback success criteria, which
are already written to be checkable without ever reading the profile
(`gh secret list`, `length(@)`, `Test-Path`). Nothing downstream needs the profile's contents.

Record in Phase 5 (criterion 5.11) not just which auth path was taken, but that the documented
alternative was policy-blocked for the agent — otherwise the contingency reads as available when it
is not.

**What the fallback changes downstream.** This branch has to be executable in advance, not written
up afterwards the way the 2026-08-31 region fallback was, so both places that assume OIDC state
their alternative here rather than leaving the implementer to improvise:

*Phase 3 success criteria, fallback variant.* Criteria 3.1, 3.2, 3.3, 3.5 and 3.6 all assert OIDC
artifacts that will not exist on this path. **Do not tick them and do not fail them — mark each `n/a
(publish-profile path)`** and answer these instead:

```powershell
gh secret list                                    # AZURE_WEBAPP_PUBLISH_PROFILE present
                                                  # and the three AZURE_* OIDC secrets absent
az webapp deployment list-publishing-profiles -g $RG -n $WEBAPP --query "length(@)"   # non-zero
Test-Path (Join-Path $env:TEMP "tenexcards-publish-profile.xml")                      # expect False
```

- `AZURE_WEBAPP_PUBLISH_PROFILE` exists and `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/
  `AZURE_SUBSCRIPTION_ID` do not — mixing the two paths is the failure mode to catch
- The temp profile file no longer exists on disk
- No app registration was created: `az ad app list --display-name $APPNAME --query "length(@)"`
  returns `0`

*Phase 4 step list, fallback variant.* Replace the `azure/login` → `az webapp deploy` pair with a
single `azure/webapps-deploy` step taking `app-name: tenexcards-ka` and
`publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE }}`, with the packed zip as its
`package`. Everything else in the workflow is unchanged — checkout, `setup-dotnet`, publish,
`pack.py`, `upload-artifact`, and `verify_deploy.py` all still apply, and `verify_deploy.py` remains
the signal that the deploy actually worked.

Two consequences worth stating so they are not discovered mid-run: **`permissions` drops
`id-token: write`** (nothing requests an OIDC token on this path — leaving it is a needless grant),
and **`--track-status false` has no equivalent**, because `azure/webapps-deploy` exposes no such
flag. The hang that flag avoids was an `az` CLI behaviour, so it does not transfer; the
`timeout-minutes` backstop on the step matters more here, not less. Phase 4 §2's header comment
should then explain the publish-profile choice and the missing `--track-status` note rather than the
OIDC ones.

#### 2. Federated credential

**File**: Azure — no file in the working tree

**Intent**: Let GitHub's OIDC token stand in for a secret, scoped to this repository and this branch.

**Contract**: A federated identity credential on the app registration with issuer
`https://token.actions.githubusercontent.com`, audience `api://AzureADTokenExchange`, and a subject
**derived from GitHub's own OIDC customization endpoint** — never transcribed from documentation.

> **Corrected 2026-09-10, after the first CI run failed.** This section originally specified the
> subject as `repo:<owner>/<repo>:ref:refs/heads/main`. **This repository does not emit that form.**
> It has `use_immutable_subject: true`, so GitHub presents numeric owner and repository IDs inside
> the subject:
> `repo:krzysztof-adamowski@322424024/10x-devs-certification-project@1350427864:ref:refs/heads/main`.
> A credential built to the original text can never match any run, on any ref. Phase 3's success
> criteria all passed against it anyway, because every one of them checked that the credential
> matched *this plan* — none exchanged a token. The break surfaced only in Phase 4, as
> `AADSTS700213`. Read `sub_claim_prefix` and build the subject from it; do not hand-type it.

The subject is exact-match. A `workflow_dispatch` run on `main` produces this same subject, so the
manual re-run path needs no second credential — but a run on any other ref will fail to
authenticate, which is why Phase 1 blocks this one. The immutable form is the stronger of the two:
it prevents a renamed or re-registered repository *name* from inheriting this Azure access.

**Commands:**

```powershell
# Derive the subject from GitHub; do not type it. `sub_claim_prefix` always carries the
# owner/repo form this repository actually emits -- plain `repo:owner/name` on most
# repositories, ID-augmented where `use_immutable_subject` is on. Hand-typing it is exactly
# what produced AADSTS700213 on the first CI run.
$PREFIX = gh api "repos/$REPO/actions/oidc/customization/sub" --jq ".sub_claim_prefix"
if (-not $PREFIX) { $PREFIX = "repo:$REPO" }   # endpoint absent on older GitHub versions
$SUBJECT = "${PREFIX}:ref:refs/heads/main"
Write-Host "subject: $SUBJECT"

$credFile = Join-Path $env:TEMP "gh-main-cred.json"
@{
  name      = "gh-main"
  issuer    = "https://token.actions.githubusercontent.com"
  subject   = $SUBJECT
  audiences = @("api://AzureADTokenExchange")
} | ConvertTo-Json -Compress | Set-Content -Path $credFile -Encoding ascii

az ad app federated-credential create --id $APPID --parameters "@$credFile"
Remove-Item $credFile -Force
```

Two things here are load-bearing rather than stylistic. The JSON goes through a **file**, not an
inline argument — PowerShell 5.1 mangles nested quotes on the way to `az`, and the error it produces
points at JSON rather than at quoting. And `-Encoding ascii` avoids PowerShell 5.1's BOM, which
`az`'s JSON parser rejects with an equally unhelpful message. `${REPO}` needs the braces because a
bare `$REPO:` would make PowerShell read `REPO:` as a drive-qualified variable.

#### 3. GitHub repository secrets

**File**: GitHub repository settings — no file in the working tree

**Intent**: Make the three identifiers available to the workflow.

**Contract**: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID` set as repository
secrets via `gh secret set`. None of the three is confidential on its own — they are stored as
secrets because that is the convention `azure/login` documents, and because it keeps subscription
identifiers out of logs and out of the public repository's YAML.

**Commands:**

```powershell
gh secret set AZURE_CLIENT_ID       --body $APPID
gh secret set AZURE_TENANT_ID       --body $TENANT
gh secret set AZURE_SUBSCRIPTION_ID --body $SUB
```

### Success Criteria:

#### Automated Verification:

```powershell
az ad app list --display-name $APPNAME --query "[].appId" -o tsv                    # one id, == $APPID
az role assignment list --assignee $APPID --scope $SCOPE `
  --query "[].roleDefinitionName" -o tsv                                            # includes Contributor
az ad app federated-credential list --id $APPID --query "[].subject" -o tsv         # the exact subject
gh secret list                                                                      # the three names

# The role must NOT exist at subscription scope. This must print nothing.
az role assignment list --assignee $APPID --scope "/subscriptions/$SUB" `
  --query "[?scope=='/subscriptions/$SUB'].roleDefinitionName" -o tsv

# No client secret may exist on the app registration. This must print nothing.
az ad app credential list --id $APPID --query "[].keyId" -o tsv
```

- The app registration exists and returns exactly one `appId`
- `Contributor` is assigned at resource-group scope
- The federated credential lists the exact `repo:…:ref:refs/heads/main` subject
- `gh secret list` shows all three names
- The subscription-scope query returns nothing — the role was not granted too broadly
- The credential list returns nothing — no client secret was created

#### Manual Verification:

- The subject string's repository owner and name match the actual remote exactly, character for
  character — compare against `gh repo view --json nameWithOwner -q .nameWithOwner`. A typo here
  fails only at the first workflow run, with an opaque error.
  **This criterion passed on 2026-09-10 and the credential still could not authenticate.** Owner
  and name were correct; what was absent was the `@<owner_id>` / `@<repo_id>` suffix this
  repository emits, which `nameWithOwner` cannot reveal. The criterion even named the failure mode
  it would produce — "fails only at the first workflow run, with an opaque error" — and that is
  exactly what happened. Compare against
  `gh api repos/<owner>/<repo>/actions/oidc/customization/sub --jq .sub_claim_prefix` instead: it is
  the only source that reflects what GitHub will actually present
- If the contingency fired, the reason is written down for Phase 5
- If the session gate stopped the phase, the user ran `az login` themselves — the agent did not
  attempt it

**Implementation Note**: After completing this phase and all automated verification passes, pause
for manual confirmation before proceeding.

---

## Phase 4: The workflow

### Overview

Wire the proven scripts and the proven identity into a single workflow that deploys on push to
`main` and retains the archive it deployed.

### Changes Required:

#### 1. The deploy workflow

**File**: `.github/workflows/deploy.yml`

**Intent**: Make a merge to `main` deploy itself, and make any failure in build, packaging,
deployment, or live verification fail the run.

**Contract**:

- Triggers: `push` on `main`, plus a bare `workflow_dispatch` (no inputs) so a build can be
  re-run by hand from the current `main`. **Redeploying a *prior* run's artifact from the workflow
  is deliberately out of scope** — see `## What We're NOT Doing`.
- `concurrency: { group: deploy-production, cancel-in-progress: false }` — two merges must not deploy
  over each other, and cancelling a deploy mid-flight is worse than queueing behind it.
- `permissions: { id-token: write, contents: read }`. `id-token` is required for OIDC; `contents:
  read` for checkout. Nothing else is granted — with no cross-run artifact download, `actions: read`
  is not needed, and the narrower set is the point.
- Steps in order: checkout → `actions/setup-dotnet` pinned to `10.0.x` →
  `dotnet publish TenExCards/TenExCards.csproj -c Release` (no `-o`; the stock path is what
  `pack.py` defaults to) → `python scripts/pack.py` → `actions/upload-artifact` with the zip and an
  explicit `retention-days: 90` → `azure/login` with the three secrets → `az webapp deploy` →
  `python scripts/verify_deploy.py`.
- The deploy command mirrors the recorded one but passes `--track-status false`:
  `az webapp deploy -g rg-tenexcards-plc -n tenexcards-ka --src-path TenExCards/bin/publish.zip
  --type zip --track-status false --enriched-errors true`. Give the step a `timeout-minutes`
  backstop.
- The artifact name includes the short commit SHA so a run is identifiable in the artifact list.
  Retention is what delivers the roadmap's stated outcome: every deployed archive is recoverable for
  90 days, and because only a passing `pack.py` ever produces one, anything in the store is known to
  be shape-valid. Restoring one is a documented manual procedure (Phase 5 §2), not a workflow path.

`--track-status false` is deliberate: `deploy-plan.md` records the tracking flag hanging on "Pending"
while the site is already live, and a hang in CI burns the job timeout instead of reporting. The
verification step is a stronger signal than the flag it replaces.

#### 2. Workflow-level documentation

**File**: `.github/workflows/deploy.yml` (header comment)

**Intent**: State why the two non-obvious settings are what they are, so the next person does not
"fix" them.

**Contract**: A short header naming: why `--track-status false`, why `permissions` is exactly
`id-token: write` + `contents: read` and no more, and that infrastructure, application settings, and
prior-artifact redeploy are deliberately outside this workflow's scope, with a pointer to
`TenExCards/AGENTS.md`.

### Success Criteria:

#### Automated Verification:

```powershell
# No local YAML pre-parse. PyYAML is not installed on this machine, and this repo
# deliberately stays standard-library-only (see Phase 2). GitHub is the parser of
# record: a workflow that does not parse never registers, and `gh workflow view`
# below returns nothing — a stronger check than `yaml.safe_load`, which would accept
# valid YAML that is not a valid workflow.

# Only `.github` here — `scripts/` was committed under `(p2)`, so each commit traces
# back to the phase that authorised it as TenExCards/AGENTS.md requires.
git add .github
git commit -m "feat(deploy-pipeline): deploy on push to main (p4)"
git push

# `gh run watch` takes a REQUIRED <run-id>. Called bare it errors non-interactively, and
# interactively it can attach to the previous run and report that run's result instead —
# a green report for a run that is not the one just pushed. `git push` also returns before
# the run is queryable. Resolve the run by the commit that triggered it, which removes the
# race rather than sleeping through it.
function Wait-RunForCommit([string]$Sha) {
  foreach ($i in 1..30) {
    $id = gh run list --workflow=deploy.yml --commit $Sha --limit 1 --json databaseId -q '.[0].databaseId'
    if ($id) { return $id }
    Start-Sleep -Seconds 5
  }
  throw "no run appeared for commit $Sha"
}

# The parse check: a workflow that failed to parse never registers, so this returns nothing.
gh workflow view deploy.yml

# Watch the run to completion, then assert its conclusion.
$RUNID = Wait-RunForCommit (git rev-parse HEAD)
gh run watch $RUNID --exit-status
gh run list --workflow=deploy.yml --limit 1 --json conclusion,headSha -q '.[0]'   # conclusion: success

# Pull the retained artifact back and re-assert its shape locally.
gh run download $RUNID --dir "$env:TEMP\artifact-check"
$zip = (Get-ChildItem "$env:TEMP\artifact-check" -Recurse -Filter *.zip | Select-Object -First 1).FullName
python -c "import sys,zipfile; n=zipfile.ZipFile(sys.argv[1]).namelist(); assert 'TenExCards.dll' in n; assert not any('\\' in e for e in n); assert not any(e.startswith('publish/') for e in n); assert any(e.startswith('wwwroot/') for e in n); print('artifact shape ok,', len(n), 'entries')" $zip
Remove-Item -Recurse -Force "$env:TEMP\artifact-check"

# Confirm the deployed commit is the one that triggered the run.
gh run list --workflow=deploy.yml --limit 1 --json headSha -q '.[0].headSha'
```

Negative test — a broken build must fail before the deploy step ever runs:

```powershell
# Introduce a deliberate compile error, push, and confirm the run fails at publish.
Add-Content TenExCards/Program.cs "this is not valid csharp"
git add TenExCards/Program.cs; git commit -m "chore: temporary broken build (revert me)"; git push
$BAD = Wait-RunForCommit (git rev-parse HEAD)
gh run watch $BAD     # expect failure
gh run view $BAD --log-failed | Select-String -Pattern "publish|deploy" | Select-Object -First 20

# Revert immediately — main must not be left broken.
git revert --no-edit HEAD
git push
$FIXED = Wait-RunForCommit (git rev-parse HEAD)
gh run watch $FIXED --exit-status
```

- `gh workflow view deploy.yml` returns the workflow — GitHub parsed and registered it
- A push to `main` produces a run with conclusion `success`
- The downloaded artifact passes all four assertions locally
- The `verify_deploy.py` step's log lists more than one asset URL, each `200`
- The negative test fails at the publish step and the deploy step never runs — confirm by reading
  the failed run's step list, not just its colour
- The revert restores a green run before the phase is considered done

#### Manual Verification:

- The workflow log's deploy step completes rather than hanging, confirming the `--track-status`
  decision
- The live site serves the commit that triggered the run — check something visibly changed by it.
  **Deferred to Phase 5 on 2026-09-10.** The commit that introduced this workflow changes only
  `.github/`, so CI deployed bits byte-identical to what was already live; a pass and a total no-op
  are indistinguishable. Criterion **5.8** is this same check done properly, against the build
  marker. Do not tick 4.8 by inspecting the site — there is nothing there to see
- The artifact appears in the run's artifact list with the expected name and a plausible size
  (~500 KB, per the 2026-09-08 record)
- No secret value appears anywhere in the run log

**Implementation Note**: After completing this phase and all automated verification passes, pause
for manual confirmation before proceeding.

---

## Phase 5: Prove the artifact restores, and correct the record

### Overview

A workflow that has deployed once still has an unproven rollback story. Verify that a retained
artifact is genuinely restorable *without* mutating production, then bring the repository's own
documentation in line — because leaving the docs pointing at a superseded manual ritual re-creates
finding `F4` one layer up.

### Changes Required:

#### 1. Build marker and cold-restore rehearsal

**File**: a build marker rendered on `/` (footer of `TenExCards/Components/Layout/MainLayout.razor`
or equivalent)

**Intent**: Make "which build is live" observable at all, and confirm a retained artifact is a real
rollback candidate rather than an assumed one.

**Contract, part 1 — the marker.** Land a commit that renders a build marker on `/`; the short
commit SHA in the footer is enough. Without it nothing distinguishes one deployed build from
another: at the end of Phase 4 the negative test's revert restores the exact source the `(p4)`
commit had, so both builds are byte-identical. The marker is what makes Phase 4's criterion 4.8
("the live site serves the triggering commit") and the Manual Testing step "push a commit that
changes something visible on `/`" answerable rather than taken on trust. Expect asset fingerprints
to change on every build; that is harmless.

**Contract, part 2 — the rehearsal.** Download the *previous* successful run's artifact and assert
its shape locally. This proves the rollback input exists, is retrievable off the development
machine, and is shape-valid — which is the part that could silently not be true. It stops short of
deploying it, because automating and drilling that path is deferred (`## What We're NOT Doing`), and
because a rollback that mutates production to prove itself is a cost this change no longer needs to
pay. The emergency restore command is written down in §2 rather than executed here.

**Commands:**

```powershell
# Land the build marker. (Re-declare Wait-RunForCommit from Phase 4 if this is a fresh shell.)
git add TenExCards
git commit -m "feat(deploy-pipeline): render build marker on / (p5)"
git push
$MARKER = Wait-RunForCommit (git rev-parse HEAD)
gh run watch $MARKER --exit-status
python scripts/verify_deploy.py
if ($LASTEXITCODE -ne 0) { throw "verification failed after the marker deploy" }

# Confirm the marker is what the live root now shows, then record it.
(Invoke-WebRequest https://tenexcards-ka.azurewebsites.net/ -UseBasicParsing).Content |
  Select-String -Pattern (git rev-parse --short HEAD)

# Cold-restore rehearsal: the PREVIOUS successful run is the rollback candidate.
# `--status success` is load-bearing — Phase 4's negative test leaves a FAILED run
# directly behind the current one, and a failed run uploaded no artifact at all.
# `gh run list` has no `-o` flag; that is an `az` idiom. `--json` alone emits JSON.
gh run list --workflow=deploy.yml --status success --limit 5 --json databaseId,headSha,createdAt,conclusion
$PREVIOUS = gh run list --workflow=deploy.yml --status success --limit 2 --json databaseId -q '.[1].databaseId'
"rollback candidate = $PREVIOUS"   # record in the deployment record

gh run download $PREVIOUS --dir "$env:TEMP\rollback-check"
$zip = (Get-ChildItem "$env:TEMP\rollback-check" -Recurse -Filter *.zip | Select-Object -First 1).FullName
python -c "import sys,zipfile; n=zipfile.ZipFile(sys.argv[1]).namelist(); assert 'TenExCards.dll' in n; assert not any('\\' in e for e in n); assert not any(e.startswith('publish/') for e in n); assert any(e.startswith('wwwroot/') for e in n); print('rollback artifact shape ok,', len(n), 'entries')" $zip
Remove-Item -Recurse -Force "$env:TEMP\rollback-check"
```

Deliberately no `az webapp deploy` here. The rehearsal proves the artifact is present, retrievable
and shape-valid; deploying it would mutate production to demonstrate something the four assertions
already establish, and the automated redeploy path it would exercise is out of scope for this change.

#### 2. Deployment record

**File**: `context/deployment/deploy-plan.md`

**Intent**: Add the third deployment record, and correct the two things the earlier records now get
wrong.

**Contract**: A new dated record covering: the pipeline is live; which authentication path was taken
(OIDC, or the publish-profile contingency and why); the workflow and script paths; the cold-restore
rehearsal result with the rollback-candidate run id; and the artifact retention window.

It must also record **the OIDC subject collision of 2026-09-10** and its consequences, because the
app registration now carries two federated credentials and nothing in Azure explains why:

- `gh-main-immutable` is the live one — subject built from `sub_claim_prefix`, carrying the
  `@<owner_id>` / `@<repo_id>` fragments this repository emits.
- `gh-main` matches nothing and grants nothing. It is retained deliberately, as the credential that
  would become live if `use_immutable_subject` were ever turned off. **Undocumented it is a trap**:
  an auditor sees two credentials, cannot tell which is load-bearing, and has even odds of deleting
  the working one while "removing the duplicate."
- The real trust boundary is unchanged by any of this: anyone who can push to `main` can obtain the
  token. Branch protection and a role narrower than `Contributor` are the controls that matter;
  OIDC does not close that, and the record should not imply it does.

Note also that criterion 4.9's "~500 KB" expectation is stale — it predates `F-02`. The archive is
now ~27.5 MB, of which ~51 MB uncompressed is MSAL native broker binaries shipped for every RID by
`Microsoft.Data.SqlClient`. Legitimate, not a packaging fault; a `linux-x64` RID would cut it, and
that is a future change, not this one.

It must also carry **the emergency restore procedure as literal commands**, because automating it is
deferred and an undocumented manual path is the same failure this change exists to remove — a rule
that lives only in someone's memory. Written out, it is:

```powershell
# Restore a previously deployed build from its retained artifact (90-day window).
gh run list --workflow=deploy.yml --status success --limit 10 --json databaseId,headSha,createdAt
gh run download <run-id> --dir "$env:TEMP\restore"
$zip = (Get-ChildItem "$env:TEMP\restore" -Recurse -Filter *.zip | Select-Object -First 1).FullName
az webapp deploy -g rg-tenexcards-plc -n tenexcards-ka --src-path $zip --type zip `
  --track-status false --enriched-errors true
python scripts/verify_deploy.py
```

Past the retention window, or if the artifact store is unavailable, fall back to rebuilding from the
commit: `git checkout <sha> -- TenExCards/`, `dotnet publish -c Release`, `python scripts/pack.py`,
then the same `az webapp deploy` — which is why `pack.py` had to stay runnable on the development
machine.

Two corrections to the existing text, written as corrections rather than edits to history, matching
how this file already handles superseded steps:

- Step 5's "no native `TenExCards` executable" assertion is **Windows-only**. A framework-dependent
  publish on Linux emits an extensionless apphost by that name, so the check produces a false
  positive on every CI build. It is not one of the archive-shape assertions and is not carried into
  `pack.py`.
- `--track-status true` in step 7 is not used by the pipeline, for the hang the file itself
  documents.

#### 3. Agent instructions

**File**: `TenExCards/AGENTS.md`

**Intent**: Point the archive-shape rule at the script that enforces it. This is the sentence that
closes finding `F4`.

**Contract**: The `Never upload an archive that has not passed all three shape assertions` bullet in
`## Never do these` names `scripts/pack.py` as the enforcement, and the `## Deployment` section gains
a short subsection stating that deploys happen through `.github/workflows/deploy.yml` on push to
`main`, that infrastructure and app settings are deliberately outside it, and that manual
`az webapp deploy` remains the documented emergency path using the same script.

Keep the *reasoning* — why `Compress-Archive` is unusable, what each assertion prevents, that a
passing deploy command is not evidence. Only the "how you comply" half becomes a pointer. Also note
the fourth assertion (`wwwroot/` non-empty) so the file and the script agree on the count.

#### 4. Roadmap baseline

**File**: `context/foundation/roadmap.md`

**Intent**: The `## Baseline` section states "there is no `.github/` directory at all, so zero CI
workflows exist despite `tech-stack.md` declaring auto-deploy-on-merge as a decision." That stops
being true in Phase 4.

**Contract**: The `Deploy / infra` baseline bullet reflects that CI now exists. Do not touch the
`F-03` item's `Status` — `/10x-archive` owns the move to `done`.

#### 5. The rollback section the pipeline supersedes

**File**: `context/deployment/deploy-plan.md`, `## Rollback`

**Intent**: That section still names `TenExCards/bin/publish-scaffold-rollback.zip` as *the*
rollback path — "B1 has no deployment slots, so rollback is manual." The retained artifact is now
the first-choice source, and leaving this uncorrected points the next reader at a git-ignored file
on one laptop during exactly the incident this change exists to remove.

**Contract**: `## Rollback` leads with the retained-artifact restore procedure from §2 (naming the
90-day window and that rollback is still a manual `az webapp deploy`, not a workflow dispatch), and
demotes the local zip to the fallback it now is — consistent with Migration Notes, which keeps the
file. Keep the `git checkout 035e064` reconstruction note; it is still the last resort if both
fail.

#### 6. The stale deploy command inside the infrastructure source of truth

**File**: `infra/main.bicep` (header comment block, lines 17-19 — **comment only**)

**Intent**: The comment carries `az webapp deploy … --track-status true`, the exact flag Phase 5
documents as a hang. CLAUDE.md names this file the source of truth for infrastructure, so a stale
command here is read with more authority than the same text anywhere else.

**Contract**: The comment points at `.github/workflows/deploy.yml` as the normal path and, if it
keeps a literal command for the emergency route, drops `--track-status true`. **No resource,
property, or parameter changes** — `## What We're NOT Doing` forbids changing the template, and
this edit stays inside the comment block so that rule holds intact.

### Success Criteria:

#### Automated Verification:

```powershell
Select-String -Path TenExCards/AGENTS.md -Pattern 'scripts/pack\.py'
Select-String -Path context/deployment/deploy-plan.md,TenExCards/AGENTS.md -Pattern 'deploy\.yml'
Select-String -Path context/foundation/roadmap.md -Pattern 'no .github. directory'   # expect: no output
Select-String -Path infra/main.bicep -Pattern 'track-status true'                    # expect: no output
Select-String -Path context/deployment/deploy-plan.md -Pattern 'deploy.yml' | Select-Object -First 3
gh run list --workflow=deploy.yml --limit 3 --json databaseId,conclusion
python scripts/verify_deploy.py
```

- `TenExCards/AGENTS.md` references `scripts/pack.py`
- Both `deploy-plan.md` and `AGENTS.md` reference `deploy.yml`
- The roadmap Baseline no longer claims `.github/` is absent
- `infra/main.bicep`'s comment no longer carries `--track-status true`, and `git diff` on it shows
  comment lines only
- `deploy-plan.md`'s `## Rollback` leads with the retained-artifact restore, with the local zip
  demoted, and `## Deployment` carries the literal restore commands
- The previous successful run's artifact downloads and passes all four shape assertions locally
- `python scripts/verify_deploy.py` exits `0` after the marker deploy

#### Manual Verification:

- The build marker is visible on `/` and matches the short SHA of the commit that triggered the run
  — this is what makes "which build is live" answerable rather than assumed
- The cold-restore rehearsal downloaded a real artifact from a *previous* run and asserted its shape;
  production was not mutated to prove it
- `TenExCards/AGENTS.md` reads start to finish as a fresh agent would, with no dangling reference to
  a manual ritual that no longer applies and no lost reasoning
- The new `deploy-plan.md` record states which authentication path was taken and why, records the
  2026-09-10 OIDC subject collision, and says plainly why the app registration carries two federated
  credentials — which one is live, and that the other is retained on purpose rather than left behind
- The record does not imply OIDC closed the deployment trust boundary: push access to `main` still
  yields the token, and branch protection plus a role narrower than `Contributor` are what would
  narrow it
- Someone could reconstruct the emergency manual deploy from the docs alone, using the same script CI
  uses

**Implementation Note**: This is the final phase. After it, the change is ready for
`/10x-impl-review`.

---

## Testing Strategy

There is no test project and this change does not create one — `TenExCards/AGENTS.md` places that
with the first feature touching generation, triage, or persistence (`S-01`). Verification here is
behavioural, run against real artifacts and the real site.

### Script-level checks:

- `pack.py` on a correct publish tree: all four assertions pass, exit `0`
- `pack.py` on a tree nested under `publish/`: exits non-zero naming assertions 1 and 2
- `pack.py` on a tree with an emptied `wwwroot/`: exits non-zero naming assertion 4
- `verify_deploy.py` against the live host: exits `0`, lists every referenced asset
- `verify_deploy.py` against a host that 404s: exits non-zero
- Both scripts run on the Windows development machine without `zip`, WSL, or PowerShell 7

### Pipeline-level checks:

- Push to `main` → full green run, live site serves that commit
- Deliberately broken build → run fails at publish, deploy step never runs
- Retained artifact of a prior successful run → downloads and passes all four shape assertions
  locally, proving a rollback candidate exists off the development machine

### Manual Testing Steps:

1. Push the build-marker commit, confirm the live site shows that short SHA within one run
2. Open the run log and read the `verify_deploy.py` output; confirm the asset list matches the
   browser network tab
3. Download the run's artifact, unzip it, confirm `TenExCards.dll` at the root and a `wwwroot/` tree
   with forward slashes
4. Download a *previous* successful run's artifact and assert its shape — the rollback candidate is
   real and retrievable without touching production
5. Follow the restore procedure in `deploy-plan.md` on paper and confirm every command is present and
   runnable as written; do not execute the deploy
6. Confirm no secret value appears in any run log

## Performance Considerations

Not a concern for this change — CI runtime is expected in single-digit minutes and nothing here
touches the request path. One deployment-adjacent note: the deploy causes a container restart, so the
site is briefly unavailable and every Blazor circuit is dropped. That is unchanged from manual
deploys and is out of scope at one worker, but it is why `verify_deploy.py` needs its warm-up loop.

## Migration Notes

- **`TenExCards/bin/publish-scaffold-rollback.zip` stays** until the pipeline has proven itself over
  several deploys. It is git-ignored and costs nothing.
- **Anyone with a local clone** must update their remote tracking after the Phase 1 rename
  (`git fetch --prune && git branch -m master main && git branch -u origin/main`). At present the
  only clone is the development machine.
- **The manual deploy path is not removed**, only demoted. It stays documented as the emergency
  route, and now runs the same `pack.py` CI does.
- **Rollback stays manual in this change.** Retention makes a rollback candidate always available
  off the development machine; restoring one is the documented `gh run download` + `az webapp deploy`
  procedure in `deploy-plan.md`. Automating it as a workflow dispatch is deliberately deferred —
  revisit when a rollback is actually needed under time pressure, which is also the point at which
  its cost is justified.

## References

- Roadmap item: `context/foundation/roadmap.md`, `### F-03: Merges deploy themselves`
- Carried-forward finding: `context/changes/blazor-server-shell/reviews/impl-review.md`, `### F4`
- Deployment ground truth: `context/deployment/deploy-plan.md` (both dated records)
- Archive-shape rules and their reasoning: `TenExCards/AGENTS.md`, `## Never do these`
- Infrastructure source of truth: `infra/main.bicep`
- CI decision on record: `context/foundation/tech-stack.md` (`ci_provider`, `ci_default_flow`)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not
> rename step titles. See `references/progress-format.md`.

### Phase 1: Rename `master` to `main`

#### Automated

- [x] 1.1 `git rev-parse --abbrev-ref HEAD` prints `main` — 4c8e589
- [x] 1.2 Upstream tracking resolves to `origin/main` — 4c8e589
- [x] 1.3 `gh repo view` reports `main` as the default branch — 4c8e589
- [x] 1.4 `origin/master` no longer exists — 4c8e589
- [x] 1.5 No markdown file describes an unsettled branch name — 4c8e589

#### Manual

- [x] 1.6 GitHub shows `main` as default with no stale `master` — 4c8e589
- [x] 1.7 Nothing external depended on `master` — 4c8e589

### Phase 2: Committed pack-and-verify scripts

#### Automated

- [x] 2.1 `dotnet publish -c Release` succeeds — c0102d9
- [x] 2.2 `python scripts/pack.py` exits `0` with all four assertions passing — c0102d9
- [x] 2.3 Reading the zip back confirms `TenExCards.dll` at the archive root — c0102d9
- [x] 2.4 Negative test: a `publish/`-nested tree exits non-zero naming assertions 1 and 2 — c0102d9
- [x] 2.5 Negative test: an emptied `wwwroot/` exits non-zero naming assertion 4 — c0102d9
- [x] 2.6 `python scripts/verify_deploy.py` against the live site exits `0` listing multiple assets — c0102d9
- [x] 2.7 Negative test: verification against a 404 host exits non-zero — c0102d9
- [x] 2.8 `scripts/` committed under a `(p2)` message — c0102d9

#### Manual

- [x] 2.9 `pack.py` failure output names what to fix — c0102d9
- [x] 2.10 Asset list matches the browser network tab for `/` — c0102d9
- [x] 2.11 Both scripts run on Windows without `zip`, WSL, or PowerShell 7 — c0102d9

### Phase 3: Azure OIDC identity for CI

#### Automated

- [x] 3.1 App registration `gh-tenexcards-deploy` exists and returns one `appId` — 765e2bc
- [x] 3.2 `Contributor` role assignment present at resource-group scope — 765e2bc
- [x] 3.3 Federated credential lists the exact `refs/heads/main` subject — 765e2bc
- [x] 3.4 `gh secret list` shows all three secret names — 765e2bc
- [x] 3.5 No role assignment exists at subscription scope — 765e2bc
- [x] 3.6 `az ad app credential list` returns nothing — no client secret was created — 765e2bc

#### Manual

- [x] 3.7 Subject string matches `gh repo view` character for character — 765e2bc
- [x] 3.8 If the contingency fired, the reason is written down for Phase 5 — 765e2bc
- [x] 3.9 If the session gate stopped the phase, the user ran `az login` — the agent did not — 765e2bc

### Phase 4: The workflow

#### Automated

- [x] 4.1 `gh workflow view deploy.yml` returns the workflow — GitHub parsed and registered it — 66d5abd
- [x] 4.2 A push to `main` produces a run with conclusion `success` — 66d5abd
- [x] 4.3 The downloaded artifact passes all four assertions locally — 66d5abd
- [x] 4.4 The verification step logs multiple asset URLs, each `200` — 66d5abd
- [x] 4.5 Negative test: a broken build fails at publish and never reaches deploy — 66d5abd
- [x] 4.6 The revert restores a green run before the phase closes — 66d5abd

#### Manual

- [x] 4.7 The deploy step completes rather than hanging — 66d5abd
- [x] 4.8 The live site serves the triggering commit — deferred to 5.8 and confirmed there: `/` renders `build be36194`, the commit that triggered run `34538642319`
- [x] 4.9 The artifact is listed with the expected name and plausible size — 66d5abd
- [x] 4.10 No secret value appears in the run log — 66d5abd

### Phase 5: Prove the artifact restores, and correct the record

#### Automated

- [x] 5.1 `TenExCards/AGENTS.md` references `scripts/pack.py`
- [x] 5.2 Both `deploy-plan.md` and `AGENTS.md` reference `deploy.yml`
- [x] 5.3 The roadmap Baseline no longer claims `.github/` is absent
- [x] 5.4 `infra/main.bicep`'s comment drops `--track-status true`; diff is comment-only
- [x] 5.5 `deploy-plan.md`'s `## Rollback` leads with the retained-artifact restore and carries the
      literal restore commands
- [x] 5.6 The previous successful run's artifact downloads and passes all four shape assertions
- [x] 5.7 `verify_deploy.py` exits `0` after the marker deploy

#### Manual

- [X] 5.8 The build marker is visible on `/` and matches the triggering commit's short SHA
- [x] 5.9 The cold-restore rehearsal used a real prior artifact; production was not mutated — re-performed 2026-09-12 rather than audited: run `34537642474` (`publish-40ccddb`, 77 entries, 27,638,294 bytes) passes all four assertions; the deployments feed's newest entry is still the marker deploy 32.6h earlier and `/` still renders `build be36194`
- [X] 5.10 `AGENTS.md` reads coherently start to finish with no lost reasoning
- [x] 5.11 The new deployment record states which auth path was taken and why, records the OIDC subject collision, and explains why two federated credentials exist — verified 2026-09-12 against live state, not just presence: `sub_claim_prefix` matches the recorded live subject exactly, both federated credentials exist with the tabulated subjects, 0 client secrets, and exactly one role assignment (Contributor, scoped to `rg-tenexcards-plc`)
- [X] 5.12 The emergency manual restore is reconstructable from the docs alone
