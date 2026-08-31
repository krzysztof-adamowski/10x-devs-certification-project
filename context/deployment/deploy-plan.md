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

```powershell
$RG   = "rg-tenexcards-weu"
$LOC  = "westeurope"
$PLAN = "asp-tenexcards-linux"
$APP  = "tenexcards-ka"
$PROJ = "C:\Users\Krzychu\Documents\10xDevs\TenExCards"
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
az appservice plan show -g $RG -n $PLAN --query "{kind:kind, reserved:reserved}" -o table
```

`reserved` must be `true` — that is the real signal the plan is Linux. `--is-linux` is passed
explicitly rather than trusting a recently-changed default.

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
```

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

CI/CD (`ci_provider: github-actions` is recorded but pipeline setup is explicitly out of scope
in `infrastructure.md`), the database (nothing persists yet), Blazor Server conversion, custom
domain, and Application Insights. The git branch is `master` while `tech-stack.md` says
"merge to main" — an inconsistency to settle when CI is actually built, not now.

## Teardown

```powershell
az group delete --name $RG --yes --no-wait
```

Everything provisioned lives in one resource group specifically so this is a single command.
Worth running if the deploy is only a pipeline proof and Blazor work is days away — B1 bills
~$12.41/mo whether or not anything uses it.

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
2. **Three plan commands do not exist / do not parse on Azure CLI 2.89.1.**
   - `az webapp list-runtimes --query "[?starts_with(@,'DOTNETCORE')]"` — the command now returns
     objects, not strings; JMESPath `starts_with()` throws. Used `grep -i dotnet`.
   - `az webapp check-name` — not a command. Used the ARM REST endpoint
     `POST /providers/Microsoft.Web/checknameavailability`.
   - `az appservice plan create --enriched-errors` — not a valid flag on that command (it is a
     `az webapp deploy` flag). Dropped.
3. **`az webapp log config --level information` alone left `applicationLogs` Off.** `--level`
   only takes effect alongside `--application-logging`; a second call was needed.
4. **`-SkipHttpErrorCheck` is PowerShell 7+**, unavailable in Windows PowerShell 5.1. Status-code
   checks were run with `curl -o /dev/null -w '%{http_code}'` instead.

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

The subscription is **Free Trial** (`quotaId: FreeTrial_2014-09-01`) with `spendingLimit: On`.
This is a **deliberate choice** — the project runs on free course credits — so nothing below is a
defect to fix now. It matters only for whoever takes this past the course.

B1 provisions against trial credit today, but when the credit is exhausted Azure **disables the
resources rather than billing** — the site stops rather than costing money. Upgrade to
Pay-As-You-Go before anything depends on this staying up; B1 bills ~$12.41/mo once on PAYG.
Upgrading is also the precondition for requesting West Europe access or a North Europe quota
increase, if either region is ever wanted.

## Teardown (supersedes the `rg-tenexcards-weu` command above)

```powershell
az group delete --name rg-tenexcards-plc --yes --no-wait
```
