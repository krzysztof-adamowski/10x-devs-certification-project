# scripts/

Two scripts stand between a build and a deploy that looks fine and is not. Both exit non-zero on
failure, and both are the **authority** on the rule they encode — the prose elsewhere describes
them, it does not duplicate them. If a rule here and a rule in a markdown file disagree, the script
is right and the markdown is stale.

CI calls these same two scripts with these same defaults. There is no CI-only assertion and no
locally-only assertion, so a failure you can see in GitHub Actions is a failure you can reproduce on
your own machine in one command.

| Script | Answers | Exits non-zero when |
| --- | --- | --- |
| `pack.py` | Is this archive safe to upload? | any of four shape assertions fails |
| `verify_deploy.py` | Did the deploy actually work? | the page or any same-origin asset is not `200` |

## Why these exist

Every failure they catch **deploys successfully** and then breaks at runtime. `az webapp deploy`
reports `RuntimeSuccessful` either way, so there is no signal to notice — a nested archive 503s, and
an archive with backslash entry names serves a page whose every asset 404s. Both were measured; see
the 2026-08-31 and 2026-09-08 records in `../context/deployment/deploy-plan.md`.

Before this directory existed the rules lived only as prose in `../TenExCards/AGENTS.md`, which made
every deploy depend on someone reading carefully. That was recorded as finding `F4` of the
`blazor-server-shell` review and handed to this change to make executable.

## `pack.py`

Builds the deployable archive from a publish directory, then **reads the archive back** and checks
its shape — it does not trust its own write, because the banned `Compress-Archive` fails precisely
by writing something other than what it was asked to.

```powershell
dotnet publish TenExCards/TenExCards/TenExCards.csproj -c Release
python scripts/pack.py
```

| Argument | Default |
| --- | --- |
| `--publish-dir` | `TenExCards/TenExCards/bin/Release/net10.0/publish` |
| `--out` | `TenExCards/TenExCards/bin/publish.zip` |

The four assertions, each named in the output with `PASS` or `FAIL`:

1. `TenExCards.dll` present at the archive root
2. no entry prefixed `publish/`
3. no entry containing a backslash
4. at least one entry under `wwwroot/`

All four are **always evaluated** — the script does not stop at the first failure. A
`publish/`-nested tree violates 1, 2 and 4 at once, and a fail-fast script would name only
assertion 1, hiding the diagnosis.

## `verify_deploy.py`

Fetches the root page, extracts the stylesheet `href`s and script `src`s the page actually
references, and asserts `200` for each. A green deploy command is not evidence; this is.

```powershell
python scripts/verify_deploy.py
python scripts/verify_deploy.py --base-url https://tenexcards-ka.azurewebsites.net
```

| Argument | Default |
| --- | --- |
| `--base-url` | `https://tenexcards-ka.azurewebsites.net` |
| `--warmup-seconds` | `120` |
| `--timeout` | `30` |

Two behaviours that look like complexity and are not:

- **It retries.** The container restarts after a deploy, so a cold first request is expected on a
  *correct* deploy. Without the warm-up loop the pipeline emits false reds, and a pipeline that
  cries wolf gets ignored. Transport failures and `5xx` are retried; a `4xx` is definitive — the app
  answered, so waiting changes nothing — and fails immediately rather than burning the budget.
- **It skips off-origin URLs.** A CDN outage is not this deploy's fault and must not fail the run.

## Why Python

`zip` is not installed in Git Bash on the development machine, so a shell packer would work in CI
and fail the "runs locally" half of the requirement. `zipfile` and `urllib` are standard library on
both machines, so CI needs no dependency install step, and neither script needs `zip`, WSL, or
PowerShell 7.

Python's `zipfile` writes `/`-separated entry names on every platform when arcnames are built as
POSIX paths — exactly the property Windows PowerShell 5.1's `Compress-Archive` lacks, and the reason
that cmdlet is banned in `../TenExCards/AGENTS.md`.

## Adding a script here

Keep the pattern: arguments default to the documented paths so the same invocation works in CI and
on a laptop, the failure message names what to fix rather than only that something failed, and
anything CI depends on is runnable by hand.
