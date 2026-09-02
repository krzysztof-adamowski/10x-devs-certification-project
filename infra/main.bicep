// TenExCards — App Service (Linux, B1) + plan. This template is the SOURCE OF
// TRUTH for this app's infrastructure. The 2026-08-31 CLI run recorded in
// context/deployment/deploy-plan.md was the bootstrap; this file is
// authoritative from there on, and it declares more than that run typed by hand
// (clientAffinityEnabled, http20Enabled). Anything set imperatively that this
// template does not declare is drift — except appSettings, deliberately
// excluded for the reason given further down.
//
// Deploy:
//   az group create --name rg-tenexcards-plc --location polandcentral
//   az deployment group create -g rg-tenexcards-plc -f infra/main.bicep
//
// Preview changes before applying (the habit worth building):
//   az deployment group what-if -g rg-tenexcards-plc -f infra/main.bicep
//
// This template provisions infrastructure only. Pushing code is a data-plane
// operation and stays imperative:
//   az webapp deploy -g rg-tenexcards-plc -n tenexcards-ka \
//     --src-path TenExCards/bin/publish.zip --type zip --track-status true

targetScope = 'resourceGroup'

@description('Globally unique; becomes <name>.azurewebsites.net.')
param appName string = 'tenexcards-ka'

@description('App Service plan name.')
param planName string = 'asp-tenexcards-linux'

// Inherits the resource group's region. NOT every region can actually take a B1
// Linux plan: West Europe returns RequestDisallowedByAzure ("not accepting new
// customers") and North Europe returns "Current Limit (B1 VMs): 0" on this
// subscription, even though both appear in `az appservice list-locations`.
// Quota is region-specific. Poland Central and Sweden Central both work.
param location string = resourceGroup().location

@description('B1 is the floor. F1 caps WebSockets at 5/instance and has no Always On — see AGENTS.md.')
@allowed([ 'B1', 'B2', 'B3', 'S1', 'P0v3' ])
param skuName string = 'B1'

@description('Verify with `az webapp list-runtimes --os linux | grep -i dotnet` before changing.')
param linuxFxVersion string = 'DOTNETCORE|10.0'

// WARNING: deploying this template CLEARS `properties.freeOfferExpirationTime`
// on the plan. Verified 2026-08-31 by snapshot/deploy/diff: the value went from
// 2026-09-30T18:15:33 to null on a deployment that reported "Succeeded". The
// property is service-assigned and read-only — there is no `az` command to put
// it back. `what-if` predicts this correctly as `- Delete`; it is NOT part of
// the noise this resource type is prone to. Do not dismiss it.
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  kind: 'linux'
  // `tier` is deliberately not declared: ARM derives it from the SKU name, and
  // hardcoding 'Basic' would have been wrong for the S1/P0v3 values skuName
  // allows. `az deployment group what-if` reports it as `NoEffect`.
  sku: {
    name: skuName
    capacity: 1
  }
  properties: {
    // The real Linux switch — `kind: 'linux'` alone is cosmetic. This is the
    // property to assert on after any deployment.
    reserved: true
  }
}

// what-if reports two residual `+ Create` lines on this resource —
// siteConfig.localMySqlEnabled and siteConfig.netFrameworkVersion — neither of
// which is declared anywhere in this template. They are ARM defaults diffed
// against the site GET's partial siteConfig view. Verified twice on 2026-08-31
// by deploy-then-diff: both are noise, nothing changes.
//
// They look identical to the plan's freeOfferExpirationTime line above, which
// was real and destructive. There is no way to tell them apart by reading the
// output — only by snapshotting, deploying, and diffing.
resource site 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: plan.id

    // Platform-level HTTP->HTTPS (301). This is the correct way to enforce TLS,
    // and it makes UseHttpsRedirection() in Program.cs redundant.
    //
    // The container supplies X-Forwarded-Proto, so Request.IsHttps is already
    // true for real traffic and the middleware returns before looking for a
    // port. Verified 2026-08-31: ASPNETCORE_HTTPS_PORT=443 alone returns 200
    // with zero redirects; only when paired with
    // ASPNETCORE_FORWARDEDHEADERS_ENABLED=false does it return 307 to the
    // request's own URL (ERR_TOO_MANY_REDIRECTS). Add neither.
    //
    // The "HttpsRedirectionMiddleware[3] Failed to determine the https port"
    // line appears once at startup, from the platform's plain-HTTP warm-up
    // probe. It is not per-request and not a defect.
    httpsOnly: true

    // Sticky sessions (ARR affinity). Irrelevant to the current scaffold and
    // `true` by platform default anyway, but load-bearing the moment Blazor
    // Server circuits exist and the plan scales past 1 worker: a circuit's
    // state is in-memory on one instance.
    //
    // This belongs on `properties`, NOT inside `siteConfig`. ARM has no
    // `siteConfig.clientAffinityEnabled` and silently discards it; Bicep only
    // warns (BCP037), so a template that gets it wrong compiles and deploys
    // green with affinity unset.
    clientAffinityEnabled: true
  }
}

// siteConfig lives here as the `web` child resource rather than inline on the
// site above. Inline, what-if diffs it against the site GET, which returns only
// a subset of siteConfig — ftpsState and minTlsVersion are absent from it, so
// they showed as permanent phantom `+ Create` lines. Verified 2026-08-31:
// deploying with them inline changed nothing, confirming the diff was noise.
// A child resource is diffed against config/web, which returns the full object
// — the same reason the `logs` child below reports NoChange correctly.
//
// Tradeoff: on a greenfield deploy the site is created before this applies, so
// it exists briefly without a runtime. Accepted, because the alternative is a
// what-if output that trains an operator to ignore it — which is exactly how
// the real freeOfferExpirationTime delete got waved through.
resource siteWebConfig 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: site
  name: 'web'
  properties: {
    linuxFxVersion: linuxFxVersion

    // Required on B1: without it the app is unloaded when idle and the first
    // request pays a cold start. Not available on F1.
    alwaysOn: true

    minTlsVersion: '1.2'
    ftpsState: 'Disabled'
    http20Enabled: true

    // Deliberately NOT declared here, each for a reason:
    //   webSocketsEnabled  - no-op on Linux; WebSockets are always on
    //   WEBSITES_PORT      - custom containers only; Oryx exports
    //                        ASPNETCORE_URLS=http://*:8080 for built-in runtimes
    //   appSettings        - see the comment on the block below
  }
}

// App settings are intentionally NOT declared in this template.
//
// Declaring `appSettings: []` above would make Bicep authoritative over them,
// and every future deployment would silently delete anything set out of band —
// including the LLM API key and the database connection string, which
// infrastructure.md specifies as Key Vault references applied via
// `az webapp config appsettings set`. Losing those on a routine infra
// deployment is a worse failure than the drift this omission allows.
//
// Do NOT add ASPNETCORE_FORWARDEDHEADERS_ENABLED here. Verified 2026-08-31:
// the Linux .NET container already supplies X-Forwarded-Proto by default, so
// Request.IsHttps is correct without it. Setting it to false is what breaks
// things — combined with ASPNETCORE_HTTPS_PORT it produces a 307 to the
// request's own URL, an infinite redirect. Neither setting belongs here.
//
// When Identity lands, move secrets to Key Vault references and add them
// above — that is the change this block is waiting for.

// Ordered after the `web` config on purpose: a config/web write can reset
// httpLoggingEnabled, and this resource is what turns it back on.
resource logs 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: site
  name: 'logs'
  dependsOn: [ siteWebConfig ]
  properties: {
    // On Linux this is what `az webapp log tail` streams — the container's
    // stdout, i.e. the ASP.NET Core startup trace.
    httpLogs: {
      fileSystem: {
        enabled: true
        retentionInDays: 3
        retentionInMb: 100
      }
    }
    // Note: via CLI this needs BOTH --application-logging and --level;
    // --level alone silently leaves it Off.
    applicationLogs: {
      fileSystem: {
        level: 'Information'
      }
    }
    detailedErrorMessages: {
      enabled: false
    }
    failedRequestsTracing: {
      enabled: false
    }
  }
}

output appUrl string = 'https://${site.properties.defaultHostName}'
output planIsLinux bool = plan.properties.reserved
