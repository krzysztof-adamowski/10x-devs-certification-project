# First deployment — TenExCards scaffold → Azure App Service

## Context

`context/foundation/infrastructure.md` recorded Azure App Service (Linux, B1) as the
deployment target after a scored comparison and anti-bias cross-check, but nothing has ever
been deployed. The goal here is to **prove the deployment pipeline end to end with the
simplest possible artifact** — the unmodified `dotnet new webapi` scaffold currently in
`TenExCards/` — so that when Blazor Server and Identity land, a deployment failure can only
be the application, never the platform setup.

Scope confirmed with the user: deploy the scaffold as-is (a single `GET /weatherforecast`
route), West Europe, active subscription, app name based on initials.

## Corrections to infrastructure.md found while planning

Verification overturned two claims in the artifact and surfaced one gratuitous deviation from
the .NET default. All three are in `## Getting Started`; the first two would have caused wasted
debugging.

1. **"WebSockets default to Off and Blazor Server will not work without them" is false on
   Linux.** Per the App Service on Linux FAQ, *"the `webSocketsEnabled` ARM setting doesn't
   apply to Linux apps since WebSockets are always enabled for Linux."* The default-Off
   behaviour is Windows-only. The `--web-sockets-enabled true` flag succeeds but is a no-op.
2. **`UseHttpsRedirection()` will NOT cause a redirect loop here**, contrary to the general
   Microsoft warning about Linux App Service. The Linux .NET container supplies
   `X-Forwarded-Proto`, so `Request.IsHttps` is true for real traffic and the middleware returns
   before it ever looks for a port. A loop requires **both** `ASPNETCORE_HTTPS_PORT` and
   `ASPNETCORE_FORWARDEDHEADERS_ENABLED=false` — it comes from the missing forwarded scheme, not
   from the port, which is harmless on its own. Set neither; `--https-only true` at the platform
   is the correct posture, and makes `UseHttpsRedirection()` redundant.

3. **`-o ./publish` deviates from the .NET default for no benefit.** Step 3 says
   `dotnet publish -c Release -o ./publish`; run from `TenExCards/` that produces
   `TenExCards/publish/`, which is **not ignored** — verified with `git check-ignore`:

   ```
   NOT IGNORED   TenExCards/publish/TenExCards.dll
   NOT IGNORED   TenExCards/publish.zip
   IGNORED       TenExCards/bin/Release/net10.0/publish/...  <- .gitignore:12:[Bb]in/
   ```

   The `# Publish Web Output` heading at `.gitignore:136` looks like it covers this but does
   not — the entries beneath it are `*.pubxml` / `*.publishproj` / `*.publishsettings`, publish
   *settings* rather than publish *output*. A misleading heading in the stock Visual Studio
   template.

   The `-o` flag was invented when this artifact was written and buys nothing. Dropping it
   restores the .NET default, `bin/Release/net10.0/publish/`, which `[Bb]in/` already ignores —
   so **`.gitignore` needs no change**, and the command matches every Microsoft quickstart and
   every agent's prior. The zip goes to `bin/publish.zip`; `dotnet` produces no zip, so there
   is no default to deviate from and `bin/` is its natural home.

`infrastructure.md` gets all three corrections, plus the region it never named, in step 9.

## Manual gates (human-only — the agent cannot do these)

| Gate | Why |
|---|---|
| `winget install --exact --id Microsoft.AzureCLI` | Azure CLI is not installed; no `~/.azure` exists. Machine-scope MSI raises a UAC prompt. |
| **Close and reopen the terminal** | Documented as the #1 install issue — `az` is not on PATH until a new shell starts. |
| `az login` | Interactive browser authentication. |

Nothing below runs until `az account show` returns a subscription.

## Execution

Variables (PowerShell). `$PUB` is the stock `dotnet publish` output location; both paths sit
under `bin/`, which `[Bb]in/` already ignores.

> **SUPERSEDED — see Deviations.** The run landed in `rg-tenexcards-plc` / `polandcentral`.
> West Europe and North Europe both failed at create time on this subscription.

```powershell
$RG   = "rg-tenexcards-weu"
$LOC  = "westeurope"
$PLAN = "asp-tenexcards-linux"
$APP  = "tenexcards-ka"
$PROJ = (Resolve-Path "./TenExCards").Path   # run from the repo root
$PUB  = Join-Path $PROJ "bin\Release\net10.0\publish"
$ZIP  = Join-Path $PROJ "bin\publish.zip"
```

**1. Pre-flight — three checks before creating any billable resource.**

```powershell
az webapp list-runtimes --os linux --query "[?starts_with(@,'DOTNETCORE')]" -o tsv
az appservice list-locations --sku B1 --linux-workers-enabled -o table
az webapp check-name --name $APP --query "{available:nameAvailable, reason:message}"
```

- Use whatever runtime string the first command prints, verbatim. `DOTNETCORE:10.0` is
  expected but **unverified** — no Microsoft page prints the colon form for .NET 10 (the pipe
  form `DOTNETCORE|10.0` is confirmed for `--linux-fx-version`). This is a gate, not polish.
- If West Europe is absent from the second, switch `$LOC` to `northeurope`. Third-party
  reporting describes West Europe capacity pressure through 2026; no Microsoft confirmation
  found, so check rather than assume.
- If `tenexcards-ka` is taken, try `tenexcards-krzysztof-adamowski`; if that is also taken,
  append a random suffix to it. Re-run `az webapp check-name` at each step.

**2. Resource group and Linux B1 plan.**

```powershell
az group create --name $RG --location $LOC
az appservice plan create --name $PLAN --resource-group $RG --location $LOC `
  --is-linux true --sku B1 --number-of-workers 1 --enriched-errors true
az appservice plan show -g $RG -n $PLAN --query "{kind:kind, reserved:properties.reserved}" -o table
```

`reserved` must be `true` — that is the real signal the plan is Linux. `--is-linux` is passed
explicitly rather than trusting a recently-changed default.

> Corrected 2026-09-01: this query previously read `reserved:reserved`, which returns `null` —
> the field sits under `properties`, not at the root of the CLI's output object. The assertion
> this step calls load-bearing was silently returning nothing, which reads as *not* Linux.

**3. Create the web app, HTTPS enforced at the platform.**

```powershell
az webapp create --name $APP --resource-group $RG --plan $PLAN `
  --runtime "DOTNETCORE:10.0" --https-only true
az webapp config show -g $RG -n $APP --query linuxFxVersion -o tsv   # expect DOTNETCORE|10.0
```

**4. Site config and logging — before deploying, so the first startup is captured.**

```powershell
az webapp config set -g $RG -n $APP --always-on true --min-tls-version 1.2 --ftps-state Disabled
az webapp log config -g $RG -n $APP --docker-container-logging filesystem --level information
az webapp log config -g $RG -n $APP --application-logging filesystem --level information
```

> Corrected 2026-09-01: this was a single call. `--level` only takes effect alongside
> `--application-logging`, so the first call alone left `applicationLogs` **Off** (deviation 3).
> Two calls are required.

Deliberately **not** set, each for a specific reason:
- `ASPNETCORE_HTTPS_PORT` — unnecessary; combined with disabled forwarded headers it produces a
  redirect loop (see Corrections)
- `WEBSITES_PORT` — custom containers only; Oryx exports `ASPNETCORE_URLS=http://*:$PORT` (8080) for built-in runtimes
- `SCM_DO_BUILD_DURING_DEPLOYMENT` — we ship compiled binaries
- `--web-sockets-enabled` — no-op on Linux
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED` — already active by platform default on the Linux .NET
  container; nothing needs adding, now or when Identity lands. Setting it to `false` is the hazard.

**5. First Release build — none has ever been produced in this repo.**

```powershell
Set-Location $PROJ
dotnet publish -c Release
```

Confirm `TenExCards.dll`, `TenExCards.runtimeconfig.json` and `appsettings.json` are present,
and that `runtimeconfig.json` requests `Microsoft.AspNetCore.App` 10.0.x — that is the contract
the platform image must satisfy. There must be no native `TenExCards` executable or `*.so`
pile; that would mean a self-contained publish, which the csproj rules out.

**6. Flat zip — contents at the archive root.**

> **Superseded 2026-09-08 — do not follow this step as written.** Both the command and the
> assertion below are wrong for any publish output containing subdirectories, which includes
> every Blazor build. See `## Compress-Archive cannot build a deployable archive from this
> project` in the 2026-09-08 record. Kept unedited because this section records what was run on
> 2026-08-31.

```powershell
Compress-Archive -Path (Join-Path $PUB '*') -DestinationPath $ZIP -Force
```

The trailing `*` is load-bearing. Verify before uploading — a nested zip deploys
*successfully* and then 503s, which is the nastiest failure mode available here:

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z = [IO.Compression.ZipFile]::OpenRead($ZIP)
$z.Entries.FullName | Select-Object -First 10; $z.Dispose()
```

Must start with `TenExCards.dll`, not `publish/TenExCards.dll`.

**7. Deploy.**

```powershell
az webapp deploy -g $RG -n $APP --src-path $ZIP --type zip --track-status true --enriched-errors true
```

If `--track-status` hangs on "Pending", Ctrl+C and verify directly — a known Linux CLI issue
where the site is already live.

**8. Record the deployment** at `context/deployment/deploy-plan.md` — what was provisioned,
the resulting URL, which settings were deliberately omitted and why, and the teardown command.
The lesson chain expects this path as ground truth for what is already deployed.

**9. Correct `infrastructure.md`** — the two factual errors, drop `-o ./publish` in favour of
the `dotnet publish -c Release` default (with `--src-path ./bin/publish.zip`), and record
`westeurope` as the chosen region (`## Getting Started` never named one). No `.gitignore`
change.

> **SUPERSEDED — see Deviations.** The region recorded was `polandcentral`, not `westeurope`.

## Verification

```powershell
$H = az webapp show -g $RG -n $APP --query defaultHostName -o tsv
Invoke-RestMethod "https://$H/weatherforecast"        # 5 JSON forecast objects = SUCCESS
Invoke-WebRequest "https://$H/" -SkipHttpErrorCheck   # 404 expected and CORRECT
az webapp log tail -g $RG -n $APP
```

Three results that look like failures and are not:

- **404 at `/`** — `Program.cs:22` maps only `GET /weatherforecast`. There is no root route.
  Always On also pings `/` every 5 minutes, so expect a steady drip of 404s in the logs.
- **404 for `/robots933456.txt`** — App Service's warm-up probe. Any status code satisfies it.
- **`warn: HttpsRedirectionMiddleware[3] Failed to determine the https port for redirect`** —
  emitted once at startup, from App Service's internal plain-HTTP warm-up probe. Real HTTPS
  traffic carries `X-Forwarded-Proto`, so the middleware returns before reaching the port lookup
  and logs nothing. Do not "fix" it.

`/openapi/v1.json` returns 404 in production by design — `MapOpenApi()` is inside the
`IsDevelopment()` branch. Do not set `ASPNETCORE_ENVIRONMENT=Development` to get it; that also
enables the developer exception page.

## Out of scope

The database (nothing persists yet), Blazor Server conversion, custom domain, and Application
Insights.

CI/CD left this section on 2026-09-10. It is no longer deferred — it is the `deploy-pipeline`
change (`context/changes/deploy-pipeline/plan.md`), which builds the pipeline
`ci_provider: github-actions` always implied. That change also settled the branch name this
section used to park: `master` was renamed to `main` on 2026-09-10, so the repository, the
roadmap, and `tech-stack.md`'s "merge to main" now agree.

## Teardown

```powershell
az group delete --name $RG --yes --no-wait
```

Everything provisioned lives in one resource group specifically so this is a single command.
Worth running if the deploy is only a pipeline proof and Blazor work is days away — B1 bills
~$13.15/mo whether or not anything uses it (Poland Central list price, verified
2026-09-01 via the Azure retail price API).

## Critical files

- `TenExCards/Program.cs` — pipeline and the single route (read-only here)
- `TenExCards/TenExCards.csproj` — `net10.0`, framework-dependent, no RID
- `context/foundation/infrastructure.md` — to correct (step 9)
- `context/deployment/deploy-plan.md` — to create (step 8)
- `TenExCards/.gitignore` — read only; no change needed once artifacts live under `bin/`

---

# Deployment record — executed 2026-08-31

Status: **live and verified.** The pipeline is proven end to end with the scaffold, which was
the whole point: a future Blazor Server deployment failure can now only be the application.

## What is deployed

| | |
|---|---|
| URL | `https://tenexcards-ka.azurewebsites.net` |
| Live route | `GET /weatherforecast` → `200`, 5 JSON forecast objects |
| Subscription | `<subscription-id>` ("Azure subscription 1") |
| Resource group | `rg-tenexcards-plc` |
| **Region** | **`polandcentral`** — *not* the planned `westeurope` (see Deviations) |
| App Service plan | `asp-tenexcards-linux`, B1 Linux, 1 worker, `reserved: true` |
| Web app | `tenexcards-ka`, `linuxFxVersion: DOTNETCORE\|10.0` |
| Deployment id | `c7539443-efa0-4ec2-acfc-6051c33bd180`, `RuntimeSuccessful`, 1/1 instances |
| Artifact | `TenExCards/bin/publish.zip`, flat, from `dotnet publish -c Release` |
| Toolchain | .NET SDK 10.0.400, Azure CLI 2.89.1 |

Settings applied: `--https-only true`, `--always-on true`, `--min-tls-version 1.2`,
`--ftps-state Disabled`, container + application filesystem logging at `information`.

## Deviations from the plan

1. **Region: `westeurope` → `northeurope` → `polandcentral`.** West Europe appears in
   `az appservice list-locations --sku B1 --linux-workers-enabled` but ARM rejects it at create
   time: `RequestDisallowedByAzure` — "the selected region is currently not accepting new
   customers". The plan anticipated this and named North Europe as the fallback; North Europe
   passed that check and then failed on a different one — `Current Limit (B1 VMs): 0`.
   A two-region probe established the quota block is **region-specific, not subscription-wide**:
   Sweden Central and Poland Central both accepted B1 on the same subscription. Poland Central
   was chosen for lowest latency from Poland and EU data residency. The probe plans were deleted
   immediately; total exposure was under two minutes and no other resource was created.

   **Re-tested ~2 hours later** at the user's request, to check whether either failure was transient:
   both regions failed byte-identically, so neither was. Nothing was created by the re-test. Note the
   framing, though — these are **Free Trial constraints, not Azure-wide facts**. Region access and
   quota increases are both free support tickets, generally granted only on Pay-As-You-Go; a paid
   subscription may hit neither block. West Europe is closed to *new* customers, not broken.
2. **Two plan commands do not exist / do not parse on Azure CLI 2.89.1.**
   - `az webapp list-runtimes --query "[?starts_with(@,'DOTNETCORE')]"` — the command now returns
     objects, not strings; JMESPath `starts_with()` throws. Used `grep -i dotnet`.
   - `az webapp check-name` — not a command. Used the ARM REST endpoint
     `POST /providers/Microsoft.Web/checknameavailability`.

   > Corrected 2026-09-01: a third bullet here claimed `az appservice plan create
   > --enriched-errors` was not a valid flag on that command. That was **wrong** —
   > `az appservice plan create --help` on 2.89.1 documents it, and it is precisely the
   > context-enriched diagnostic for Linux plan-create failures, i.e. the West Europe and North
   > Europe blocks in deviation 1 above. It should have been kept; step 2 already passes it.
3. **`az webapp log config --level information` alone left `applicationLogs` Off.** `--level`
   only takes effect alongside `--application-logging`; a second call was needed.
4. **`-SkipHttpErrorCheck` is PowerShell 7+**, unavailable in Windows PowerShell 5.1. Status-code
   checks were run with `curl -o /dev/null -w '%{http_code}'` instead.
5. **Two ARM template deployments ran after the zip push and were not recorded here until
   2026-09-01.** Recovered with `az deployment group list -g rg-tenexcards-plc`:

   | Name | Mode | Result | Timestamp |
   |---|---|---|---|
   | `drift-probe` | Incremental | Succeeded | 2026-08-31T20:49:45Z |
   | `restructure-probe` | Incremental | Succeeded | 2026-08-31T20:57:12Z |

   These applied `infra/main.bicep` to the live app twice, and they are the deployments that
   cleared `properties.freeOfferExpirationTime` on the plan. `infra/main.bicep` is the source of
   truth for this app's infrastructure, so its declared state — which includes
   `clientAffinityEnabled` and `http20Enabled`, never set by the CLI commands above — is
   authoritative over anything in this record. The CLI run was the bootstrap.
6. **`az` auto-registered the `Microsoft.Web` resource provider on the subscription** during the
   first `az appservice plan create`, without prompting. Normal CLI behaviour and permanent, but
   it is a subscription-level change nobody typed. Confirmed still `Registered` on 2026-09-01 via
   `az provider show --namespace Microsoft.Web --query registrationState`. Recorded here because
   it is the only mutation from this deploy that lives outside the resource group, so
   `az group delete` in `## Teardown` does **not** undo it.

## Plan claims confirmed by the run

- **`DOTNETCORE:10.0` is accepted** on `--runtime` input and normalizes to `DOTNETCORE|10.0` in
  `linuxFxVersion`. The plan flagged this as unverified; it is now verified. .NET 10 on App
  Service is **GA/LTS** (`support: Active`, EOL 2028-12-01), not Preview-tagged.
- **`UseHttpsRedirection()` no-ops rather than looping.** `--https-only true` performs the
  redirect at the platform (`http://` → `301` → `https://`), and the startup log carries the
  single `HttpsRedirectionMiddleware[3] Failed to determine the https port for redirect` line from
  the warm-up probe.
- **`.gitignore` needs no change.** `git check-ignore -v` confirms both
  `TenExCards/bin/publish.zip` and `TenExCards/bin/Release/net10.0/publish/TenExCards.dll` are
  caught by `TenExCards/.gitignore:12:[Bb]in/`.
- **`WEBSITES_PORT` correctly omitted** — logs show `Now listening on: http://[::]:8080`.
- All four "looks like a failure but is not" cases reproduced: `404` at `/`, `404` at
  `/robots933456.txt`, `404` at `/openapi/v1.json` (`Hosting environment: Production`), and the
  HttpsRedirection warning.

## Deliberately not set (unchanged from the plan)

`ASPNETCORE_HTTPS_PORT` · `WEBSITES_PORT` (custom containers only) ·
`SCM_DO_BUILD_DURING_DEPLOYMENT` (we ship compiled binaries) · `--web-sockets-enabled`
(no-op on Linux) · `ASPNETCORE_FORWARDEDHEADERS_ENABLED`.

The two HTTPS-related entries were tested against the live app on 2026-08-31:

- `ASPNETCORE_HTTPS_PORT=443` alone → `200`, zero redirects. The port is not a hazard on its own.
- That port **plus** `ASPNETCORE_FORWARDEDHEADERS_ENABLED=false` → `307` with `Location` equal to
  the request URL, i.e. `ERR_TOO_MANY_REDIRECTS`. The loop needs both, and originates in the
  missing forwarded scheme rather than the port.
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED` is already active by platform default on the Linux .NET
  container — which is why disabling it is what breaks things. It needs no addition when Identity
  lands.

Since `--https-only` performs the redirect at the platform, `UseHttpsRedirection()` in
`Program.cs` is redundant and should be removed rather than configured around — per Microsoft:
*"If the proxy also handles HTTPS redirection, there's no need to use HTTPS redirection
middleware."*

## Open item for the human

The subscription is **Free Trial** (`quotaId: FreeTrial_2014-09-01`) with `spendingLimit: On`,
re-verified 2026-09-01 with
`az rest --method get --url "https://management.azure.com/subscriptions/<id>?api-version=2022-12-01"`
— note `az account show` and `az account list` both return `null` for `subscriptionPolicies`, so
that is not the command to check this with. The same GET reports a `freetier` promotion running
to 2027-09-30. See `## Budget Posture` in `infrastructure.md` for what this permits.
This is a **deliberate choice** — the project runs on free course credits — so nothing below is a
defect to fix now. It matters only for whoever takes this past the course.

B1 provisions against trial credit today, but when the credit is exhausted Azure **disables the
resources rather than billing** — the site stops rather than costing money. Upgrade to
Pay-As-You-Go before anything depends on this staying up; B1 bills ~$13.15/mo once on PAYG (Poland Central, verified 2026-09-01).
Upgrading is also the precondition for requesting West Europe access or a North Europe quota
increase, if either region is ever wanted.

## Teardown (supersedes the `rg-tenexcards-weu` command above)

```powershell
az group delete --name rg-tenexcards-plc --yes --no-wait
```

---

# Deployment record — executed 2026-09-08

Status: **live and verified.** Second deployment to `tenexcards-ka`, replacing the scaffold that
had served since 2026-08-31. Change: `blazor-server-shell` (roadmap `F-01`), Phase 2.

## What is deployed

| | |
|---|---|
| URL | `https://tenexcards-ka.azurewebsites.net` |
| Live routes | `GET /` → `200` (Blazor Server shell) · `GET /circuit-check` → `200` · `GET /weatherforecast` → **`404`** |
| Deployment id | `354fe65d-52e2-4627-87b8-b0c18d5caf85`, `RuntimeSuccessful`, 1/1 instances |
| Artifact | `TenExCards/bin/publish.zip`, flat, 497,672 bytes, 28 entries |
| Runtime reported by the container | `ASP .NETCore Version: 10.0.11` |
| Instance | `lw1sdlwk0000ZO` |
| Infrastructure | unchanged — no `infra/main.bicep` deployment ran |

The app is a Blazor Web App with per-page interactivity. `UseHttpsRedirection()` is gone; the
minimal-API scaffold, the `WeatherForecast` record and the OpenAPI package are gone with it.

**Measured timings are in `../changes/blazor-server-shell/baseline.md`** — TTFB, total page load,
circuit establishment and click round-trip, with the conditions they were taken under. They are
not copied here; that file is their single home.

## Compress-Archive cannot build a deployable archive from this project

This is the one thing that would have broken the deploy, and it was caught before uploading.

Windows PowerShell 5.1 runs on .NET Framework 4.8, whose zip writer stores Windows path separators
verbatim. Every nested entry came out as `wwwroot\_framework\blazor.web.js` rather than
`wwwroot/_framework/blazor.web.js`. `[IO.Compression.ZipFile]::CreateFromDirectory` under the same
runtime does the same thing — it is the runtime, not the cmdlet.

A backslash is a legal filename character on Linux, so such an archive can extract as flat files
with literal backslashes in their names instead of a `wwwroot/` tree. The deploy reports success
and the app then serves an HTML shell whose every stylesheet and script 404s.

**This could not have bitten the 2026-08-31 deploy.** That archive contained eleven bare filenames
and no subdirectories at all, so it had no separators to get wrong. The Blazor publish output is
the first to carry nested paths.

The archive is now built entry-by-entry with `ZipArchive.CreateEntry`, names normalised to `/`.

### The old first-entry assertion was also wrong

`## Execution` step 6 above says the entry list "must start with `TenExCards.dll`". That check
**fails on a correct archive** as soon as the publish output has subdirectories: `Compress-Archive`
emits directory entries ahead of file entries, so entry 0 is `wwwroot/Components/`. It only ever
passed because the scaffold publish had no directories.

Three assertions replace it, all run before upload:

1. `TenExCards.dll` is present at the archive root
2. no entry is prefixed `publish/`
3. no entry contains a backslash

Confirmation that the shape was right came from the container's own startup log:
`Found the startup D name: TenExCards.dll`, followed by `dotnet "TenExCards.dll"`.

## Verified after the deploy

- `GET /` → `200`; `GET /circuit-check` → `200`; `GET /weatherforecast` → `404`.
- **Every `.css`/`.js` URL the rendered root references returns `200`** on the live host —
  `blazor.web.*.js`, `app.*.css`, `ReconnectModal.*.razor.js`, `bootstrap.min.*.css`,
  `TenExCards.*.styles.css`. Checking the shell alone is not enough: a Blazor page's HTML is
  mostly *references* to the scripts that make it work, so a `200` on the document proves little.
- The `_blazor` WebSocket reaches open state and `/circuit-check` increments without a reload —
  a circuit works on B1 Linux.
- **No `HttpsRedirectionMiddleware[3]` warning at startup**, now that the middleware is removed.

## Corrections to the 2026-08-31 record

- **"`404` at `/`" is no longer one of the "looks like a failure but is not" cases.** Always On
  pings `/` every five minutes and now gets a real page. A `404` at `/` is a genuine fault from
  here on. The other three cases (`/robots933456.txt`, `/openapi/v1.json`, and the
  HttpsRedirection warning) still stand — though the third can now only appear if someone has
  re-added the middleware.
- **`UseHttpsRedirection()` "no-ops rather than looping"** remains true as a statement about the
  platform, but is no longer a statement about this app: the middleware is not in the pipeline.
  The `## Deliberately not set` recommendation to remove it has been acted on.
- `UseHsts()` is now in the pipeline in the non-Development branch. It is new production
  behaviour relative to the scaffold, recorded so it is not later mistaken for drift. Reasoning
  is in `TenExCards/AGENTS.md` under `### HTTPS`.

## Rollback (superseded — history)

> **Do not follow this procedure.** The live one is `## Rollback` in the 2026-09-10/11 pipeline
> record at the end of this file, which restores a retained CI artifact instead of a git-ignored
> zip on one machine. This section is kept only as the record of what served production at the
> time.

B1 has no deployment slots, so rollback is manual. `TenExCards/bin/publish-scaffold-rollback.zip`
(git-ignored, 372,646 bytes) is the exact archive that served production from 2026-08-31 until
this deploy; redeploy it with the same `az webapp deploy` command. If that file is lost, rebuild
from commit `035e064`: `git checkout 035e064 -- TenExCards/`, publish, zip, redeploy.

---

# Deployment record — executed 2026-09-10

Change `persistence-spine` (`F-02`), phases 1 and 3. Phase 1 provisioned the persistence
infrastructure; phase 3 deployed the application code that uses it. This record covers both,
because the ordering between them is the whole design: **the Key Vault reference was proven to
resolve while the deployed app still contained no EF Core code at all**, so that when the code
landed the only new variables were the archive and the platform.

Measurements are **not** copied here. `context/changes/persistence-spine/baseline.md` is their
single home.

## What was provisioned

Declared in `infra/main.bicep`, deployed as `persistence-spine-p1`, `Succeeded`, **Incremental**
(never `--mode Complete`):

| Resource | Name | Notes |
| --- | --- | --- |
| SQL logical server | `sql-tenexcards-plc` | `polandcentral`, `minimalTlsVersion` 1.2 |
| App database | `sqldb-tenexcards` | S0 / Standard, provisioned — not auto-pausing |
| Development database | `sqldb-tenexcards-dev` | Basic |
| Firewall rule | `AllowAllWindowsAzureIps` | `0.0.0.0`–`0.0.0.0` |
| Key Vault | `kv-tenexcards-plc` | RBAC, purge protection unset, 7-day retention |
| Role assignment | `4d35348c-…` | Key Vault Secrets User → the site's identity |
| Site identity | `a3558bdf-861d-464a-a071-e279dfe12ba9` | system-assigned; added to the existing `site` |

## Two subscription-level mutations that `az group delete` does NOT undo

`Microsoft.Sql` and `Microsoft.KeyVault` were both `NotRegistered` and were registered with
`az provider register`. Registration is asynchronous — poll, do not assume; both took ~45 s.

This is the same class of silent, irreversible change the 2026-08-31 record notes for
`Microsoft.Web`. Deleting the resource group leaves both registered. Note also that
`az sql db list-editions -l polandcentral` returns `SubscriptionNotFound` *before* registration —
which reads like a region problem and is not.

## The application deploy

Deployment `10dd25fc-9cac-4c10-9f90-4f5c7b818786` — `RuntimeSuccessful`, 1/1 instances,
0 failed, `Site started successfully` after 93 s.

The archive was **not** hand-built. `F-03` landed `scripts/pack.py` and `scripts/verify_deploy.py`
in parallel with this change, and this deploy is the first to use them:

> **The last line below is SUPERSEDED — do not run it.** `/db-check` was deleted on 2026-09-12 by
> `S-01` phase 5, along with `/circuit-check`, once the auth-cookie restart-survival check replaced
> what it was kept for. The route now `404`s, and `404` is **not** in `verify_deploy.py`'s transient
> set — so it fails immediately, with wording saying the app answered and this is not a warm-up
> problem. That is a red which reads like a broken deploy. The **bare** `python
> scripts/verify_deploy.py` on the line above it is still correct and is what CI runs. The block is
> kept unedited because it records what was run on 2026-09-10; note that its `dotnet publish` path
> also predates the `S-01` solution-folder restructure.

```powershell
dotnet publish TenExCards/TenExCards.csproj -c Release
python scripts/pack.py            # 77 entries, 27,676,619 bytes — all four assertions PASS
az webapp deploy -g rg-tenexcards-plc -n tenexcards-ka --src-path TenExCards/bin/publish.zip --type zip --track-status true
python scripts/verify_deploy.py   # / and all 5 same-origin assets 200
python scripts/verify_deploy.py --base-url https://tenexcards-ka.azurewebsites.net/db-check
```

`scripts/` is now the authority on the archive shape rules; the prose in `TenExCards/AGENTS.md`
describes them and does not duplicate them.

## The migration ran on the boot path, against an empty database

This was the plan's most-guarded risk — a failed startup migration means the container does not
serve, on a tier with no slot rollback — so it was arranged to fire while the database was empty
and disposable rather than first in `S-01` against a database holding accounts.

Phase 2 verified everything against `sqldb-tenexcards-dev`, leaving `sqldb-tenexcards` with **zero
tables**, confirmed immediately before the deploy. The startup log then shows:

```
20:36:11  info: Program[0]  Applying 1 pending migration(s): 20260910190508_InitialSpine
20:36:11  CREATE TABLE [__EFMigrationsHistory] ( …
20:36:12  CREATE TABLE [DataProtectionKeys] ( …
20:36:12  CREATE TABLE [SpineProbes] ( …
20:36:12  info: Program[0]  Migrations applied successfully.
20:36:14  Application started.
```

The migration itself took ~0.8 s of the 93 s; the rest is container start.

## Looks like a failure but is not — a new case

**A `BadImageFormatException` plus a bogus routing error during the file-swap window.** At
`20:35:03`, **68 seconds before the new container started**, the log carries:

```
System.BadImageFormatException: Index not found. (0x80131124)
System.InvalidOperationException: The type TenExCards.Components.Pages.NotFound
  does not have a Microsoft.AspNetCore.Components.RouteAttribute applied to it
```

This is the **outgoing** container reading type metadata from files being replaced under it
mid-extraction. The second message is a lie produced by the first: reflection over torn metadata
returned no attributes, so the router concluded there were none. `NotFound.razor` does carry
`@page "/not-found"`.

Verified against the running app rather than argued: three nonexistent URLs all returned `404`
with the not-found page rendering and no exception page. Nothing failed after `20:36:14`.

Do not go editing `NotFound.razor`. The test is whether 404s work *after* the new container
starts.

**Also expected, and not the warning it resembles:** `No XML encryptor configured. Key {…} may be
persisted to storage in unencrypted form.` This appears only on the boot that *mints* a key, so it
is easy to miss and easy to confuse with the keys-*not-persisted* warning, which is now gone. See
`TenExCards/AGENTS.md` under `## Never do these`.

## Secrets and the reference

Four secrets, all written with `az keyvault secret set --file` — never `--value`, which the CLI
warns about, and never as a command argument, because Windows PowerShell 5.1 keeps
`ConsoleHost_history.txt` indefinitely:

| Secret | Purpose |
| --- | --- |
| `sql-admin-password` | the `@secure()` template parameter, for every future deployment |
| `sql-connection-string` | what the deployed app consumes |
| `sql-dev-user-password` | the contained `tenexdev` user — **only copy** |
| `sql-dev-connection-string` | what local `dotnet user-secrets` holds |

The plan specified two; the `-dev` pair was added during phase 1 because the contained user is
created in phase 1 and consumed in phase 2 and had nowhere durable to live in between.

One app setting, set with `az webapp config appsettings set`:

```
ConnectionStrings__DefaultConnection=@Microsoft.KeyVault(SecretUri=https://kv-tenexcards-plc.vault.azure.net/secrets/sql-connection-string/)
```

The **trailing slash** makes it versionless, so a rotated secret is picked up. Omitting it changes
the meaning rather than erroring. Under Windows PowerShell 5.1 a leading `@` with parentheses is
array-subexpression syntax — quote the value. The reference itself is a pointer, not a secret.

Checking that it resolved is **not** `az resource show` — that endpoint returns a collection, so
that command answers `Not Found`, which is indistinguishable from a real failure. The working form
is an `az rest` GET against `config/configreferences/appsettings`; see `TenExCards/AGENTS.md`.

**`az role assignment` is unusable on this subscription** — every command in the group, including
`az role definition list`, returns `(MissingSubscription)`. ARM is fine via `az rest`.

## what-if on the new resource types

Characterised against an empty database and written into `infra/main.bicep` above each resource.
No `- Delete` was predicted on anything, in either the pre- or post-deploy run. Permanent phantoms
found:

- `Microsoft.Sql/servers/databases` — `Modify sku.name: 'Standard' -> 'S0'`. The GET returns the
  *tier* in `sku.name`; the template declares the *service objective*. The most alarming-looking
  line the template produces, and pure noise.
- `Microsoft.Authorization/roleAssignments` — `Modify properties.principalId`, diffing the resolved
  GUID against the literal unevaluated `[reference(...)]` expression.
- `Microsoft.KeyVault/vaults` and `Microsoft.Sql/servers/firewallRules` — clean, no phantoms.

And the inverse: what-if **did not predict** the `identity: SystemAssigned` addition, which was the
deployment's one intended change and did land. `Microsoft.Web/*` is unreliable in both directions.

## Teardown — additions to the command above

`az group delete` removes the server, both databases and the vault, but:

- **The vault's name is reserved for 7 days** by soft-delete. Recreating `kv-tenexcards-plc` needs
  `az keyvault purge --name kv-tenexcards-plc` first. Purge protection is deliberately **off** so
  that the single-command teardown keeps working.
- **Both provider registrations survive.** Nothing undoes them.
- **Three data-plane objects are declared in no template** and must be recreated by hand: the four
  vault secrets, the contained `tenexdev` user (T-SQL, in the dev database), and the
  development-machine firewall rule `dev-machine-krzychu`. The `-dev` secrets are the only copy of
  the contained user's credential.

## Rollback (superseded — history)

> **Do not follow this procedure.** The live one is `## Rollback` in the 2026-09-10/11 pipeline
> record at the end of this file, which restores a retained CI artifact instead of a git-ignored
> zip on one machine. This section is kept only as the record of what served production at the
> time, and for the forward-only migration note below, which still holds.

`TenExCards/bin/publish-shell-rollback.zip` (git-ignored, **497,970 bytes**, 28 entries, no EF
assemblies) is the archive that served production before this deploy; redeploy it with the same
`az webapp deploy` command. If lost, rebuild from commit `8b0bdbf` — the last commit before
`e6d3949` added EF Core.

**It does not reverse the migration.** `InitialSpine` has been applied to `sqldb-tenexcards`, and
redeploying the shell leaves those three tables in place. That is harmless here — the old code
simply ignores them — but it is the general shape of the forward-only rule: the archive is the
rollback path for *code*, and there is no rollback path for *schema*.
---

# Deployment record — executed 2026-09-10/11 — the pipeline

`F-03` (`deploy-pipeline`). **A push to `main` now deploys itself.** This record supersedes the
`## Rollback` sections of both records above; they stay as history.

## What is deployed, and by what

`.github/workflows/deploy.yml` on every push to `main`, plus a bare `workflow_dispatch` (no inputs).
The workflow is a thin caller — `scripts/pack.py` builds and shape-checks the archive,
`scripts/verify_deploy.py` proves the live site serves. Both run identically on the development
machine, so a CI failure is reproducible off CI.

Five runs on 2026-09-10 (UTC) tell the whole story and are worth keeping:

| Run | Commit | Outcome |
| --- | --- | --- |
| `34530622990` | `e82e5a3` | **failed at `azure/login`** — `AADSTS700213`, the subject collision below |
| `34536825268` | `d8beee9` | first successful CI deploy, green in **70s** |
| `34537581808` | `3cd479a` | negative test — **failed at `Publish`**; Pack, Login, Deploy and Verify all skipped |
| `34537642474` | `40ccddb` | revert restored green in **1m1s** |
| `34538642319` | `be36194` | build marker deploy, green |

The negative test is the one worth reading twice: a compile error stopped the run at `Publish`, so
**no credential was ever requested and no artifact was produced**. The pipeline cannot ship a
non-compiling app, and that is now measured rather than assumed.

## Authentication: OIDC, and the subject collision that broke the first run

**The OIDC path was taken. There is no stored Azure credential** — the publish-profile contingency
was never needed and is not in use. App registration `gh-tenexcards-deploy`, `Contributor` at
resource-group scope only, no client secret.

**The app registration carries two federated credentials. Only one works, and that is deliberate:**

| Credential | Subject | Status |
| --- | --- | --- |
| `gh-main-immutable` | `repo:krzysztof-adamowski@322424024/10x-devs-certification-project@1350427864:ref:refs/heads/main` | **live** |
| `gh-main` | `repo:krzysztof-adamowski/10x-devs-certification-project:ref:refs/heads/main` | matches nothing |

Phase 3 provisioned `gh-main` against the name-based subject that GitHub's own documentation shows.
**This repository does not emit that form.** It has `use_immutable_subject: true`, so GitHub inserts
numeric owner and repository IDs into the subject. The first CI run therefore presented a subject
Azure had never been told to trust, and was refused with **`AADSTS700213`**
(*No matching federated identity record found for presented assertion subject*).

That error string is the only symptom, and it arrives late: a credential built from the documented
form is syntactically valid, provisions without complaint, and shows nothing wrong in
`az ad app federated-credential list`. The mismatch surfaces at the first workflow run and nowhere
earlier. Grep this file for `AADSTS700213` when it happens — the fix is to rebuild the subject from
`sub_claim_prefix` as below, never to re-type it.

`gh-main` is **retained on purpose**: it becomes the working credential if `use_immutable_subject` is
ever switched off. Undocumented it would be a trap — an auditor sees two credentials, cannot tell
which is load-bearing, and has even odds of deleting the working one while "removing the duplicate."
That is why it is written down here rather than tidied away.

**Never hand-type this subject.** Read it from GitHub:

```powershell
$PREFIX = gh api "repos/<owner>/<repo>/actions/oidc/customization/sub" --jq ".sub_claim_prefix"
$SUBJECT = "${PREFIX}:ref:refs/heads/main"
```

**What OIDC did not close.** It removes the stored credential; it does not narrow who can deploy.
Anyone who can push to `main` can cause Azure to mint a `Contributor` token for `rg-tenexcards-plc`
— not by forging an identity (the subject is signed by GitHub and validated against its published
keys) but by pushing code to the branch that legitimately holds one. Branch protection on `main`
and a role narrower than `Contributor` are the controls that would narrow it. Neither is in place.
Do not read "we use OIDC" as meaning this question is settled.

## The build marker

`/` renders `<footer class="build-marker">build <short-sha></footer>`. CI publishes with
`-p:SourceRevisionId=<commit sha>`, which the SDK appends to `AssemblyInformationalVersion` after a
`+`; the footer reads it back. A local build sets no revision id and renders `local` rather than a
stale or invented SHA.

This exists because without it **"which build is live" was unanswerable**. Two deploys of identical
source are byte-identical, so a successful deploy and a total no-op looked the same. Confirmed live
on 2026-09-11: `build be36194`, matching the commit that triggered run `34538642319`.

## Artifact retention and the cold-restore rehearsal

Every deployed archive is retained **90 days** as `publish-<short-sha>`. Because only a passing
`pack.py` ever produces one, anything in that store is known shape-valid.

Rehearsed 2026-09-11 against run **`34537642474`** (`publish-40ccddb`), the previous successful run:
downloaded off the development machine and all four shape assertions re-asserted locally — 77
entries, 27,638,294 bytes, `TenExCards.dll` at the root, no `publish/` prefix, no backslash entries,
`wwwroot/` populated. **Production was not mutated to prove this.**

`--status success` is load-bearing when picking a rollback candidate: the negative test leaves a
*failed* run directly behind a good one, and a failed run uploaded no artifact at all.

**On archive size.** These archives are ~27.5 MB, not the ~500 KB the 2026-09-08 record shows. That
figure predates `F-02`. `Microsoft.Data.SqlClient` ships MSAL native broker binaries for every RID —
`linux-x64` alone is 36 MB uncompressed, with osx and win variants adding ~15 MB more. Legitimate,
not a packaging fault. Publishing with a `linux-x64` RID would cut it sharply; that is a future
change, deliberately not this one.

## Corrections to the earlier records

- The 2026-09-08 record's step 5 asserts **no native `TenExCards` executable** in the archive. That
  is **Windows-only**. A framework-dependent publish on Linux emits an extensionless apphost by that
  name, so the check is a false positive on every CI build. It is not one of the four shape
  assertions and is deliberately not carried into `pack.py`.
- `--track-status true` in step 7 is **not used by the pipeline**, for the hang that record itself
  documents. The deploy step passes `--track-status false` and lets `verify_deploy.py` be the
  signal, with `timeout-minutes` as the backstop. Measured: the deploy step completes in ~31s.

## Rollback

**This supersedes the `## Rollback` sections of the two records above.** Rollback is still a manual
`az webapp deploy` — B1 has no deployment slots, and redeploying a prior artifact from the workflow
is deliberately out of scope. What changed is the *source*: a retained CI artifact, not a
git-ignored zip on one laptop.

**First choice — restore a retained artifact (90-day window):**

```powershell
gh run list --workflow=deploy.yml --status success --limit 10 --json databaseId,headSha,createdAt
gh run download <run-id> --dir "$env:TEMP\restore"
$zip = (Get-ChildItem "$env:TEMP\restore" -Recurse -Filter *.zip | Select-Object -First 1).FullName
az webapp deploy -g rg-tenexcards-plc -n tenexcards-ka --src-path $zip --type zip `
  --track-status false --enriched-errors true
python scripts/verify_deploy.py
```

**Second choice — past the window, or the artifact store is unavailable:** rebuild from the commit.
`git checkout <sha> -- TenExCards/`, `dotnet publish TenExCards/TenExCards/TenExCards.csproj -c Release`,
`python scripts/pack.py`, then the same `az webapp deploy` above. This is why `pack.py` had to stay
runnable on the development machine rather than living inside the workflow.

> **Path corrected 2026-09-12.** That `dotnet publish` argument read
> `TenExCards/TenExCards.csproj` until `S-01` phase 4 made `TenExCards/` a solution folder and moved
> the project one level deeper. Corrected rather than left as history because this is a **runnable
> rollback procedure**, not a dated measurement — it would fail on the day it is needed most. A
> restore from a **retained artifact** (first choice, above) is unaffected: it never rebuilds.
> Everything else in these records keeps its original paths on purpose.

**Third choice — the local zips**, now demoted to last resort:
`TenExCards/bin/publish-shell-rollback.zip` (497,970 bytes, pre-EF) and
`publish-scaffold-rollback.zip` (372,646 bytes). Both are git-ignored and exist on one machine.

**None of these reverse a migration.** `InitialSpine` is applied to `sqldb-tenexcards`; redeploying
older code leaves those tables in place. The archive is the rollback path for *code*. There is no
rollback path for *schema*, by design — migrations here are forward-only.

---

# Deployment record — executed 2026-09-12 — accounts and sessions

`S-01` (`accounts-and-sessions`). Five deploys, one per phase, **all through the pipeline** — this
is the first change deployed entirely by pushing to `main`, with no hand-built archive at any point.
The infrastructure deployment and the app setting were the only human work, exactly as
`### A push to main deploys` in `TenExCards/AGENTS.md` says they must be.

The ordering across those five is the design, and it is worth reading before reusing any of it:
the key-ring work went **first, while no account existed**, because closing the exposure means
discarding a key and that invalidates every outstanding token. After phase 3 it would have signed
every learner out.

| Phase | Commit | What deployed |
| --- | --- | --- |
| 1 | `874cd61` | Key ring encrypted at rest |
| 2 | `175097f` | Identity tables only — no UI, no DI, no authorization |
| 3 | `606e084` | Register, sign in, sign out; authorization defaults to protected |
| 4 | `dbd121e` | `TenExCards.Tests` and the CI test gate |
| 5 | `9fccf42` | `/circuit-check` and `/db-check` retired, `SpineProbes` dropped |

## What was provisioned

Declared in `infra/main.bicep`, deployed as `s01-keyring-20260912-111827`, `Succeeded`,
**Incremental** (never `--mode Complete`):

| Resource | Name / id | Notes |
| --- | --- | --- |
| Vault key | `dataprotection-key` | RSA 2048, `keyOps: [wrapKey, unwrapKey]`, enabled |
| Role assignment | `22ceb7ae-9e57-5d50-9bc6-96b673955633` | **Key Vault Crypto User** → the site's identity, vault scope |

The second assignment was necessary because the site already held only `Key Vault Secrets User`.
**Secrets and keys are separate data-plane surfaces with separate roles** — a secrets role confers
nothing on a key. The same split bit the *operator* account during verification; see the false
positive below.

One app setting, set with `az webapp config appsettings set`, **before** the build that reads it was
merged:

```
DataProtection__KeyIdentifier=<the deployment's dataProtectionKeyUri output>
```

Unlike the connection string this is a **plain pointer, not a Key Vault reference** — safe to pass
inline. Its value was read from `az deployment group show -g rg-tenexcards-plc -n
s01-keyring-20260912-111827 --query properties.outputs` and compared byte-for-byte against the app
setting, rather than eyeballed. That output (`dataProtectionKeyUri`, the sixth the template declares)
exists precisely so the pointer cannot drift from the key it points at.

**The ordering here is the whole point.** App settings are deliberately outside the pipeline while a
push to `main` *is* a production deploy, so merging the encrypting build first would mean
`new Uri(null)` during service configuration — the container does not serve, on a tier with no
deployment slots, and the only way out is another push. The setting is inert to a build that does
not read it, so setting it early costs nothing and removes the window entirely.

## Discarding the plaintext key — the irreversible step

| | Before | After |
| --- | --- | --- |
| `Id` | `1` | `2` |
| `FriendlyName` | `key-f3bfba0e-…` | `key-73a4c584-…` |
| `LEN(Xml)` | 887 | 1,942 |
| Key material | plaintext | wrapped with `dataprotection-key` |

`DELETE FROM DataProtectionKeys` reported 1 row affected and 0 remaining. The growth to 1,942 bytes
is the Key Vault envelope — `kid`, wrapped key, IV, ciphertext.

**The restart between the delete and the next form is load-bearing and must not be dropped as
redundant.** Data Protection resolves the ring once and caches it (`KeyRingRefreshPeriod` defaults to
24 hours), so without a restart the app keeps using a key that no longer exists in the database: the
form succeeds, nothing is minted, and three separate checks then fail for a reason none of them
names.

## Migrations — two, in two phases, two deploys

Both forward-only, both on the boot path, both applied to `sqldb-tenexcards-dev` first:

```
20260912101411_AddIdentity      (phase 2) — AspNetUsers, AspNetUserClaims,
                                            AspNetUserLogins, AspNetUserTokens
20260912135900_DropSpineProbes  (phase 5) — drops SpineProbes, nothing else
```

`AddIdentity` creates **no role tables**: the context derives from `IdentityUserContext<TUser>`
rather than `IdentityDbContext<TUser>`, which makes "never add roles" structural under forward-only
migrations rather than conventional. It touched neither `DataProtectionKeys` — which would have
discarded the ring phase 1 had just encrypted — nor `SpineProbes`.

Each deploy's startup log reported applying exactly one pending migration by name, then `Migrations
applied successfully`.

## what-if reconciliation

Snapshotted before and after and diffed regardless of how the prediction looked, per the standing
rule. **Nothing was predicted as `- Delete`, and nothing was deleted**; the `appSettings` snapshot is
byte-identical across the deploy, so `ConnectionStrings__DefaultConnection` survived — the hazard the
template's `appSettings` omission exists to prevent. Both intended creates *were* predicted, which
is the first clean prediction on this template; recorded because it is a first, not because it makes
the next one trustworthy. The line-by-line table lives in
`../changes/accounts-and-sessions/change.md` and is not copied here.

Two notes for whoever reads that table next. `properties.freeOfferExpirationTime` was **already
`null`** before this deployment — cleared on 2026-08-31 — so this run is not evidence that the
destructive behaviour recorded against it has stopped. And `siteConfig.localMySqlEnabled` and
`siteConfig.netFrameworkVersion` were phantoms for the **fourth** time.

## Looks like a failure but is not — three new cases

**1. A `500` on `/db-check` in the app-setting restart window.** Applying
`DataProtection__KeyIdentifier` restarts the app, and the first request into that window logged a
genuine SQL error: *"A connection was successfully established with the server, but then an error
occurred during the login process."*

The timing is a perfect match for the hazard `infra/main.bicep` warns about — supplying a
`sqlAdminPassword` differing from the vault's silently rotates the server admin password, breaking
the app at its *next restart*. Three things rule it out: the documented symptom is
`Login failed for user 'tenexadmin'` (a credential rejection, not a failure *during* the login
process); the password came from the vault, so the deployment re-asserted the same value; and
**it did not persist** — eleven seconds later the new container connected cleanly. It was the old
container being torn down mid-request. **Retry before diagnosing; a rotated password does not heal
on the following boot.**

**2. `az keyvault key show` returns `(Forbidden)` for the operator.** Not a missing key and not a
broken deployment. That command is a *data-plane* read, and the operator holds `Key Vault Secrets
Officer` — a secrets role, which confers nothing on a key. Its output is indistinguishable from the
failure it appears to report, the same shape as the `az role assignment` breakage recorded on
2026-09-10. Use the ARM control-plane read instead, which Contributor already covers:

```bash
az rest --method get --url "https://management.azure.com/subscriptions/<sub>/resourceGroups/rg-tenexcards-plc/providers/Microsoft.KeyVault/vaults/kv-tenexcards-plc/keys/dataprotection-key?api-version=2024-11-01"
```

Proven before it was trusted: run against the keys *collection* before the deployment it returned
`{"value": []}` — a `200` with an empty list — establishing that the command works and the vault held
zero keys, so an empty result was readable as a verdict rather than as a broken command. Granting the
operator `Key Vault Crypto Officer` was declined: an imperative operator role assignment outside the
template, buying nothing the ARM read does not.

**3. `<value>` is present in a correctly ENCRYPTED key row.** The obvious ciphertext check —
`Xml LIKE '%<value>%'` — **reports a false positive on a correct row**. `<value>` appears in both
forms: the master key itself when plaintext, the ciphertext payload nested inside `<encryptedKey>`
when encrypted. Reading it as a failure would have led to re-running an irreversible deletion that
had already worked. Assert instead on the absence of `<masterKey>` and of the literal comment
`Warning: the key below is in an unencrypted form.`, which the plaintext writer always emits.

**Still reproducing from earlier records:** the `BadImageFormatException` during lazy route-table
warm-up appeared again in the phase 5 startup log. Checked against the archive before being treated
as a regression — the identical exception sits in the 2026-09-10 log, two days earlier, and both
boots are recorded by App Service's own health check as `success`. Pre-existing platform artifact.

## What this change did NOT close

- **Branch protection on `main` and the CI principal's `Contributor` scope.** Unchanged and still
  open. `S-01` closed a security row in the risk register, which is exactly the kind of thing that
  invites reading the deployment trust boundary as settled. It is not.
- **Database read access.** Encrypting the key ring removed *session forgery* from what that access
  buys. From phase 2 onward the same access returns every `AspNetUsers.PasswordHash`. What carries
  that residual risk is the sixteen-character password minimum over PBKDF2-HMAC-SHA512, not the key
  ring — see the risk register in `../foundation/infrastructure.md`.
