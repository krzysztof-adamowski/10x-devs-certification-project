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

// ---------------------------------------------------------------------------
// Persistence (F-02, change persistence-spine). Two databases on ONE logical
// server: the app's, and a separate one for local development. That split is
// not tidiness — Program.cs calls Database.Migrate() on the boot path, so a
// local `dotnet run` applies whatever migrations sit in the working tree to
// whatever database the connection string names, forward-only, with no down
// migration. Pointing local development at the app's database would also put
// the production Data Protection key ring on a development machine, since the
// EF key repository stores every key row in one table with no per-application
// partition. Same server keeps the SQL dialect identical, so a migration that
// applies to one applies to the other.
//
// The boundary between them is NOT the name. It is the contained database user
// created by hand in the development database (T-SQL, data-plane, declared in
// no template — see context/deployment/deploy-plan.md). Two databases that
// differ only by Initial Catalog, both reached as the server admin, are one
// edited token apart.
// ---------------------------------------------------------------------------

@description('Globally unique; becomes <name>.database.windows.net.')
param sqlServerName string = 'sql-tenexcards-plc'

@description('SQL administrator login. The password is a @secure() parameter with no default.')
param sqlAdminLogin string = 'tenexadmin'

// No default, and never written to a .parameters.json file inside this repo.
// Every future deployment of this template must supply it again; the value is
// retrievable from the vault as the `sql-admin-password` secret. This is the
// one place where deploying this template needs a secret as input, and it is
// why the password is stored in the vault on its own in addition to being
// embedded in the connection string.
//
// WARNING: this password is RE-ASSERTED on every deployment. Supplying a value
// that differs from the vault's `sql-admin-password` does not fail — the
// deployment reports "Succeeded" and silently ROTATES the server admin
// password out from under the app. The running app keeps working, because App
// Service resolved and cached its connection string at start; it breaks at the
// next restart with `Login failed for user 'tenexadmin'`, by which point the
// deployment that caused it is hours or days back. This is the same shape as
// the appSettings hazard documented further down: a successful-looking
// deployment invalidating a value held out of band.
//
// Always take the value FROM the vault. On a deliberate rotation, update
// `sql-admin-password` AND `sql-connection-string` in the same operation, then
// restart the app.
@secure()
@description('SQL administrator password. Supply at prompt or from a parameters file kept OUTSIDE this repository.')
param sqlAdminPassword string

@description('Database the deployed app reads and writes. S0 — provisioned, not auto-pausing.')
param appDbName string = 'sqldb-tenexcards'

@description('Database local development runs against. Never on a user path, so its throughput does not matter.')
param devDbName string = 'sqldb-tenexcards-dev'

@description('Globally unique; becomes https://<name>.vault.azure.net/.')
param vaultName string = 'kv-tenexcards-plc'

// Key Vault Secrets User — read secret *values*, nothing else. Built-in role
// GUIDs are stable across clouds and tenants, which is why this is a literal
// rather than a lookup.
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

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
// output — only by snapshotting, deploying, and diffing. Confirmed a third time
// on 2026-09-10: both lines appeared again, both changed nothing.
//
// 2026-09-10 also found this resource type unreliable in the OTHER direction.
// The deployment that added `identity` below was predicted by what-if as a
// Modify carrying ONLY those two phantom lines — it never mentioned identity at
// all, yet the principal was created and is what resolves the Key Vault
// reference. So what-if on Microsoft.Web/sites both invents changes that do not
// happen AND omits changes that do. A clean what-if here is not evidence that
// nothing will change; only the post-deploy diff is.
resource site 'Microsoft.Web/sites@2023-12-01' = {
  name: appName
  location: location
  kind: 'app,linux'

  // System-assigned, so its lifetime is the site's and there is no separate
  // identity resource to leak on teardown. This principal is what resolves the
  // Key Vault reference in ConnectionStrings__DefaultConnection — App Service
  // resolves references at app START and caches the outcome, so a reference set
  // before the role assignment below has propagated reports unresolved and does
  // NOT re-heal on its own. Restart the app after granting.
  identity: {
    type: 'SystemAssigned'
  }

  properties: {
    serverFarmId: plan.id

    // Platform-level HTTP->HTTPS (301). This is the correct way to enforce TLS,
    // and it is the ONLY thing enforcing TLS: app.UseHttpsRedirection() was
    // removed from Program.cs on 2026-09-08 (change blazor-server-shell, F-01).
    // Turning httpsOnly off therefore leaves the app with no redirect at all.
    //
    // The container supplies X-Forwarded-Proto, so Request.IsHttps is already
    // true for real traffic, which is also what makes UseHsts() emit its
    // header. Verified 2026-08-31: ASPNETCORE_HTTPS_PORT=443 alone returns 200
    // with zero redirects; only when paired with
    // ASPNETCORE_FORWARDEDHEADERS_ENABLED=false does it return 307 to the
    // request's own URL (ERR_TOO_MANY_REDIRECTS). Add neither.
    //
    // The "HttpsRedirectionMiddleware[3] Failed to determine the https port"
    // line NO LONGER APPEARS, because the middleware is gone. If it comes
    // back in a startup log, the middleware has been re-added. See
    // TenExCards/AGENTS.md "### HTTPS".
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
// This block is no longer "waiting" for anything. F-02 landed the database
// connection string as a Key Vault reference on 2026-09-10, and it lives
// OUTSIDE this template by design:
//
//   az webapp config appsettings set -g rg-tenexcards-plc -n tenexcards-ka \
//     --settings "ConnectionStrings__DefaultConnection=@Microsoft.KeyVault(SecretUri=https://kv-tenexcards-plc.vault.azure.net/secrets/sql-connection-string/)"
//
// A Key Vault *reference* is a pointer, not a secret — but declaring it here
// would still make Bicep authoritative over appSettings and delete it on the
// next routine deploy, exactly as the paragraph above describes. The LLM API
// key arrives the same way in S-02. Adding either one here is the mistake,
// not the milestone.

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

// SQL logical server. Public networking stays Enabled and there is no private
// endpoint — see the firewall rule below for what that actually costs.
//
// `version: '12.0'` is not a choice; it is the only value Azure SQL Database
// accepts and it does not correspond to a SQL Server release.
// what-if characterisation, 2026-09-10, taken against an EMPTY database (no
// tables, no migration history). Verified by running what-if BEFORE the first
// deployment and again AFTER it — the second run is the one that matters,
// because phantoms only appear once the resource exists to be diffed against.
//
// Permanent phantom on this resource: `NoEffect properties.version : None ->
// '12.0'`. The GET does not return `version`, so what-if diffs the declared
// value against nothing. `NoEffect` is what-if telling you outright that it
// changes nothing — believe that label; it is not the same as an unlabelled
// `+ Create` line.
//
// Also expect `Ignore Microsoft.Sql/servers/databases/master`. `master` is the
// system database, always present and never declared here. It is not drift.
//
// No `- Delete` was predicted on this resource type in either run.
//
// API version confirmed 2026-09-10 against `az provider show --namespace
// Microsoft.Sql --query "resourceTypes[?resourceType=='servers'].apiVersions"`,
// not copied from memory: 2025-01-01 is the newest STABLE (everything above it
// is -preview). `servers/firewallRules` is a child type the provider does not
// enumerate separately, so it shares this family deliberately.
resource sqlServer 'Microsoft.Sql/servers@2025-01-01' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'

    // Deliberately NOT declared here, each for a reason:
    //   administrators /        - Entra-ID-only ("passwordless") database auth
    //   azureADOnlyAuthentication  was considered and DECLINED for F-02, in
    //                              favour of the Key Vault reference that
    //                              infrastructure.md already prescribes.
    //                              Recorded here rather than only in the change
    //                              plan, so it is not re-litigated as an
    //                              oversight once that plan is archived.
    //   privateEndpointConnections - out of scope; see the firewall rule below
    //                              for what that costs. Real isolation needs
    //                              one, and nothing here substitutes for it.
    //   restrictOutboundNetworkAccess - not needed; nothing egresses from this
    //                              server.
  }
}

// "Allow Azure services" is a 0.0.0.0-0.0.0.0 entry, and that pair is a magic
// value rather than an address range: it admits traffic originating anywhere
// in Azure — any subscription, any tenant — not just this one. For
// Azure-originating traffic there is therefore NO network boundary here; the
// boundary is the SQL admin password, which is why that password lives in the
// vault and never on a development machine.
//
// Pinning to the app's possibleOutboundIpAddresses was considered and
// declined: App Service outbound IPs are shared across a scale unit, so it
// narrows "all of Azure" only to "every app on this scale unit", while adding
// a rule set that silently breaks the app when the IP set rotates on a tier or
// scale operation. Real isolation needs a private endpoint. Out of scope here.
//
// The development machine's own rule is deliberately NOT declared: it is a
// property of where you happen to be sitting, not of the infrastructure. It is
// set with `az sql server firewall-rule create` and must be re-added when the
// home IP changes. See context/deployment/deploy-plan.md.
// what-if characterisation, 2026-09-10, against an EMPTY database: this
// resource is CLEAN. `+ Create` on the first run, `NoChange` with an empty
// delta on the no-op redeploy — no phantoms, no NoEffect lines. Note this
// characterises only the ONE rule declared here; the dev-machine rule is
// created out of band and never appears in what-if at all, which is the
// expected consequence of it not being in the template rather than a defect.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2025-01-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// S0 (10 DTU, provisioned) is chosen over the auto-pausing serverless free
// offer on purpose: the first query after a pause can exceed the entire 2s
// acknowledgement budget the PRD sets. See infrastructure.md "## Budget
// Posture" — the rule is the cheapest option that removes a risk, not the
// cheapest option.
// what-if characterisation, 2026-09-10, against an EMPTY database.
//
// PERMANENT PHANTOM, and the most alarming-looking line this template produces:
//   Modify sku.name : 'Standard' -> 'S0'
// The databases GET returns the *tier* in `sku.name` ('Standard'), while this
// template correctly declares the *service objective* ('S0'). They are
// different fields with the same key, so every redeploy predicts a SKU change
// that never happens. Verified 2026-09-10 by deploy-then-diff: the database
// stayed S0/Standard/Online throughout.
//
// The dev database below does NOT show this line, because at Basic the tier and
// the service objective are the same string — which is a good reminder that the
// absence of a phantom proves nothing about its cause.
//
// Do not "fix" this by declaring sku.name: 'Standard'. That would deploy a
// tier-default service objective and silently change what you are paying for.
resource appDb 'Microsoft.Sql/servers/databases@2025-01-01' = {
  parent: sqlServer
  name: appDbName
  location: location
  sku: {
    name: 'S0'
    tier: 'Standard'
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: false
  }
}

// Basic is sufficient: this database is never on a user path, so its
// throughput does not matter. Same server as the app's database, so the SQL
// dialect and the EF Core provider are identical and a migration verified here
// applies unchanged there.
resource devDb 'Microsoft.Sql/servers/databases@2025-01-01' = {
  parent: sqlServer
  name: devDbName
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: false
  }
}

// The vault is infrastructure and belongs in this template. The secret VALUES
// are data-plane and never do — same principle as the appSettings omission
// above, and for the same reason: making the template authoritative over a
// value means a routine, successful-looking deployment deletes it.
//
// enableRbacAuthorization is declared explicitly rather than left to default.
// The default has changed over time, and under RBAC creating a vault does NOT
// grant its creator data-plane access — you must assign yourself Key Vault
// Secrets Officer separately, and then wait, because role assignments are
// eventually consistent and `az keyvault secret set` returns Forbidden until
// they propagate.
//
// enablePurgeProtection is deliberately left UNSET. With it on, the vault
// cannot be removed for the full retention window, which would break the
// single-command teardown recorded in deploy-plan.md. Soft-delete still
// reserves the NAME for 7 days after deletion: recreating this vault under the
// same name needs `az keyvault purge` first.
// API version: NOT the newest the provider reports. `az provider show` lists
// 2026-05-15 as the newest stable, and it deploys — but the Bicep CLI pinned
// here has no types for it and emits BCP081, meaning the template compiles
// WITHOUT any property validation. A typo'd property name would then compile
// clean and be silently dropped by ARM. 2024-11-01 is the newest version this
// Bicep build can type-check. Re-verify both facts before bumping it.
//
// what-if characterisation, 2026-09-10: this resource is CLEAN. A no-op
// redeploy reports `NoChange` with an empty delta — no phantoms at all, which
// makes any future line on this vault worth reading carefully rather than
// dismissing. Characterised against an EMPTY database and an EMPTY vault (the
// four secrets were written after this run); secrets are data-plane and this
// template declares none, so adding them should not change the output — but
// that has not been re-observed.
resource vault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: vaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    softDeleteRetentionInDays: 7

    // Public, deliberately, and for the same reason the SQL server is: App
    // Service outbound IPs are shared across a scale unit and rotate on tier
    // or scale operations, so there is no useful IP allow-list to write. RBAC
    // is the control here — the vault is reachable, not readable.
    publicNetworkAccess: 'Enabled'

    // Deliberately NOT declared here, each for a reason:
    //   networkAcls         - would be the place to restrict by IP/VNet. See
    //                         publicNetworkAccess above for why there is
    //                         nothing useful to put in it today.
    //   accessPolicies      - meaningless under enableRbacAuthorization: true.
    //                         Declaring both is the classic way to end up with
    //                         a permission model that does not do what it says.
    //   enablePurgeProtection - see the paragraph above; its ABSENCE is the
    //                         decision, and setting it to false is not the same
    //                         thing as leaving it unset.
    //   secrets (child)     - the vault is infrastructure; secret VALUES are
    //                         data-plane and are set with
    //                         `az keyvault secret set --file`. Same principle
    //                         as the appSettings omission further up.
  }
}

// what-if characterisation, 2026-09-10. PERMANENT PHANTOM:
//   Modify properties.principalId :
//     'a3558bdf-...' -> "[reference('.../sites/tenexcards-ka', '2023-12-01', 'full').identity.principalId]"
// what-if cannot evaluate a `reference()` expression, so it diffs the resolved
// GUID that is actually assigned against the literal, unevaluated ARM
// expression — and reports it as a Modify. It reads as though the deployment
// is about to overwrite a principal id with a string. It is not. Verified
// 2026-09-10 by deploy-then-diff: the assignment was unchanged.
//
// `NoEffect properties.principalType` accompanies it and is self-labelled.
//
// The resource NAME must be a deterministic GUID derived from scope, principal
// and role definition. A non-deterministic name (newGuid(), a literal typed
// once) makes every redeployment try to CREATE a duplicate assignment, which
// fails — the template stops being idempotent. Confirmed stable: both what-if
// runs and the deployment produced the same name, 4d35348c-adff-5bf6-82aa-58a4ea39db81.
//
// Characterised against an EMPTY database, like every other block in this
// change — worth stating because a populated database is the condition under
// which Microsoft.Sql/* what-if output has not been observed here.
//
// API version confirmed 2026-09-10 against `az provider show --namespace
// Microsoft.Authorization`. 2022-04-01 looks exactly like a value copied from
// memory, and it is worth recording that it is not: it really is the newest
// STABLE version (2025-10-01-preview and 2026-07-01-preview sit above it).
//
// principalType: 'ServicePrincipal' is not cosmetic either: without it ARM
// tries to look the principal up in Entra ID and fails on a just-created
// managed identity that has not replicated yet.
// SCOPE IS THE VAULT, NOT A SINGLE SECRET — a deliberate, examined tradeoff,
// reviewed 2026-09-10 (impl-review-phase-1, F3).
//
// This grants the site's identity read access to ALL four secrets in the vault,
// while it legitimately needs exactly one (sql-connection-string). The two it
// does not need are sql-dev-user-password and sql-dev-connection-string — the
// contained dev user that the comment at the top of this file calls the actual
// boundary between the dev and app databases. So the app can read the
// credential that separates it from the dev database.
//
// Why it is accepted anyway:
//   - The connection string the app legitimately reads already embeds the SQL
//     admin password, which outranks the dev user entirely. Narrowing scope
//     would not change what a compromised app process can reach in the app
//     database; it would only protect the dev database.
//   - Single operator, MVP, no CI principal yet.
//
// Why it was NOT fixed: secret-scoped RBAC needs `scope: <secret>`, and on the
// `az group delete` -> redeploy rebuild that deploy-plan.md documents, the
// template would assign a role at a secret that does not exist yet. Whether ARM
// rejects that is UNVERIFIED — so narrowing the scope risks breaking the
// single-command rebuild to protect a credential that ranks below one the app
// already holds. Revisit when a CI principal or a second consumer appears.
resource kvSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, site.id, keyVaultSecretsUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: site.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output appUrl string = 'https://${site.properties.defaultHostName}'
output planIsLinux bool = plan.properties.reserved
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output vaultUri string = vault.properties.vaultUri
output sitePrincipalId string = site.identity.principalId
