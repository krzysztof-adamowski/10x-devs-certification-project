# Accounts and Sessions Implementation Plan

## Overview

`S-01` gives 10xCards its account boundary. A learner registers with an email address and a
password, signs in with the same pair, signs out, and everything they will later create belongs to
that account and is visible to nobody else. It implements FR-001, FR-002 and FR-003 and the
`## Access Control` section of the PRD.

It also settles three things earlier slices deliberately left to it: the Data Protection key ring is
plaintext and is about to start signing auth cookies; no test project exists and this is the first
slice with an application rule worth asserting; and two proof-of-life surfaces exist only until an
auth boundary does.

The slice is planned for correctness rather than to the PRD's `2026-09-14` frontmatter deadline —
see `## Open Risks & Assumptions` in the brief. That decision is why key-ring encryption and a real
integration-test harness are inside this plan rather than deferred.

## Current State Analysis

**Auth is entirely absent.** A repo-wide search for `Authorize`, `AuthenticationState`,
`UseAuthentication`, `SignIn` or `IdentityUser` returns no functional hit. `Program.cs` has no
`UseRouting`, no `UseAuthentication`, no `UseAuthorization`; its whole pipeline is
`UseExceptionHandler` + `UseHsts` (non-development), `UseStatusCodePagesWithReExecute`,
`UseAntiforgery`, `MapStaticAssets`, `MapRazorComponents<App>().AddInteractiveServerRenderMode()`.
`Routes.razor` uses a bare `RouteView`, not `AuthorizeRouteView`, and nothing cascades an
authentication state.

**The database spine is live and the shape it left is convenient.** `AppDbContext` derives from
plain `DbContext` and implements `IDataProtectionKeyContext`, exposing `DataProtectionKeys` and
`SpineProbes`. It has **no `OnModelCreating` override**, so adding one is a new file section rather
than an edit to existing configuration. The context is registered twice on purpose — a
`AddDbContextFactory<AppDbContext>` plus a scoped shim that resolves the factory — because
`PersistKeysToDbContext` resolves the context from a scope with `GetRequiredService` and a
factory-only registration throws at the first rendered form. Identity's `AddEntityFrameworkStores`
resolves that same scoped registration, so no third registration is needed.

**One migration exists**, `InitialSpine`, creating `DataProtectionKeys` and `SpineProbes`. It runs
on the boot path through `Database.MigrateAsync()` behind a pending-migrations check, so a migration
that throws means the container does not serve — on a B1 tier with no deployment slots.

**The key ring persists but is not encrypted.** `F-02` verified that the ring survives a container
restart, closing the antiforgery breakage. It left `DataProtectionKeys.Xml` as plaintext XML — one
row, verified readable on 2026-09-10. Today that buys token forgery for anyone with database read
access. The moment this slice makes the same ring sign auth cookies, the same access becomes session
forgery for every account. `F-02`'s own record hands the decision here explicitly.

**The render mode is already right for this slice.** `F-01` chose per-page interactivity precisely
so Identity's sign-in pages need no carve-out: `SignInManager` writes cookies to the HTTP response,
which a circuit cannot do. `DbCheck.razor` is the worked example — statically rendered, using an
`EditForm method="post" FormName="…"` whose SSR rendering emits `__RequestVerificationToken`
automatically, with a verified rule not to add `<AntiforgeryToken />` alongside it. The Identity
pages follow that pattern exactly.

**Two proof-of-life surfaces are due for retirement**, and one is reachable from two places.
`CircuitCheck.razor` has a nav entry *and* an in-body link on `Home.razor`; `DbCheck.razor` has a
single nav entry, carrying a comment that says so. Both nav entries live in `NavMenu.razor`, which
is the file this deletion list is easiest to get wrong.

**No test project and no solution file exist.** `TenExCards.csproj` is the only project file in the
repository. CI publishes it by path.

**CI deploys every push to `main`** through a workflow that is a thin caller over `scripts/pack.py`
and `scripts/verify_deploy.py`. Infrastructure and app settings are deliberately outside it, so
anything this plan needs in Azure is human work.

## Desired End State

A learner can reach `https://tenexcards-ka.azurewebsites.net/`, register with an email address and
a password of at least sixteen characters, be signed in, sign out, and sign back in. Every route
other than the home page, the Identity pages and the error pages requires authentication. A signed-in
session survives seven days of inactivity on a sliding window and survives a container restart. The
key ring that signs that session cookie is encrypted at rest with a Key Vault key, and the plaintext
key that preceded it no longer exists.

`TenExCards.Tests` exists, asserts the auth boundary end to end, and gates the deploy. Neither
proof-of-life page exists, `SpineProbes` is gone from the database, and the repository's own records
say so.

Verification is the combination of the phase criteria below; the three that would catch a silent
regression are the auth-cookie restart-survival check (Phase 3), the ciphertext assertion on the new
key row (Phase 1), and the negative CI test that proves a failing test actually blocks a deploy
(Phase 4).

### Key Discoveries:

- **`IdentityUserContext<TUser>` composes with `IDataProtectionKeyContext`, and the combination was
  proven rather than assumed.** A throwaway probe on the exact pinned versions compiled, resolved
  `UserManager`, `SignInManager` and `IDataProtectionProvider` at runtime, and generated a migration
  creating exactly `AspNetUsers`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`
  alongside `DataProtectionKeys` — no `AspNetRoles`, no `AspNetRoleClaims`, no `AspNetUserRoles`, no
  `AspNetUserPasskeys`.
- **`EmailIndex` is created non-unique.** `RequireUniqueEmail = true` is enforced by `UserManager` in
  application code, not by the database. `UserNameIndex` *is* unique. Email uniqueness is therefore
  only database-enforced if registration sets the user name to the email address.
- **The .NET 10 Identity template would ship forbidden surface.** `dotnet new blazor -au Individual`
  generates 47 files and 3,349 lines under `Components/Account/`, including `ForgotPassword`,
  `ResetPassword`, two-factor, passkeys, external logins and personal-data export — against a PRD
  that names "no password recovery" as a deliberate decision and an `AGENTS.md` that forbids roles.
- **`verify_deploy.py` was written anticipating this slice.** It follows a same-host redirect and
  verifies the *landed* page's assets, and its source says so. It does not crawl `<a href>`. The two
  real hazards are a `401` at the root, which is not in its transient set and fails immediately, and
  a landed page referencing no same-origin stylesheet or script.
- **The app's managed identity holds only `Key Vault Secrets User`, at vault scope.**
  `ProtectKeysWithAzureKeyVault` needs a vault *key* and key permissions, so a second role assignment
  is required — and `az role assignment` is unusable on this subscription, returning
  `MissingSubscription` for every command in the group including `az role definition list`.
- **`ASPNETCORE_FORWARDEDHEADERS_ENABLED` is already active by platform default** and needs no
  addition when Identity lands. Setting it to `false` would silently strip `Secure` from auth cookies
  under the default `CookieSecurePolicy.SameAsRequest`.

## What We're NOT Doing

- **No password recovery, no password reset, no email confirmation.** The PRD names the absence as a
  decision; `AGENTS.md` states it as a prohibition. Nothing in this slice sends mail, so no email
  sender abstraction is registered.
- **No two-factor authentication, no passkeys, no external login providers, no account management
  page.** FR-001 through FR-003 are register, sign in, sign out. Changing an email address or a
  password is not among them.
- **No roles, no claims-based authorization beyond "authenticated".** The user model is flat. The
  role-free context base makes this structural rather than conventional.
- **No account deletion and no personal-data export.** Not in the PRD, not in the roadmap.
- **No account-scoped product data.** There are no cards yet; `S-02` introduces them and owns the
  query-scoping rule. This slice creates the boundary those queries will sit behind and a test
  harness that can assert it.
- **No branch protection and no narrowing of the CI principal's `Contributor` role.** Real, recorded
  in the deployment record as unclosed, and out of scope here.
- **No Bicep deployment from CI, and no `appSettings` block in the template.** Both remain
  deliberately human work.
- **No second data store.** Identity goes in the existing `AppDbContext` against the existing
  database.
- **No rate limiting beyond Identity's lockout.** Lockout blunts online guessing against a single
  account; a general request limiter is not in the PRD.

## Implementation Approach

Six phases, ordered so that no step debugs two variables at once and so that two irreversible
things happen at the cheapest possible moment.

**The key-ring work goes first, before any account exists.** Enabling encryption does not encrypt
keys already written — it encrypts keys minted afterwards. Closing the exposure therefore means
discarding the existing plaintext key and letting a fresh encrypted one be minted, which invalidates
every outstanding antiforgery token. Today that costs nothing: there are no accounts and no sessions.
After Phase 3 it would sign out every learner, and after `S-02` it would do so mid-triage.

**The migration deploys alone, before any UI uses it.** Adding four unused tables is a boot-path
migration whose only variable is the migration itself. If the container fails to serve, the cause is
unambiguous. Wiring the UI in the same deploy would confuse a schema failure with a DI failure.

**`/db-check` survives until Phase 5.** It is the only surface that emits an SSR antiforgery token,
and the inherited restart-survival check has nothing to run against without it. Phase 1 uses it
twice — once against the plaintext ring as a baseline, once against the encrypted one. Phase 3
replaces it with the stronger check (an auth cookie surviving a restart). Only then is it deleted.

**Every migration reaches `sqldb-tenexcards-dev` before the app's database.** Migrations here are
forward-only, applied on the boot path, on a tier with no deployment slots. The rollback path is
redeploying the retained archive, and that does not reverse schema.

## Critical Implementation Details

**Timing & lifecycle — key-ring encryption and the boot path.** App Service resolves Key Vault
references at app *start* and caches the outcome, so a role assignment granted after a reference is
set does not heal on its own; restart and read again. The same eventual consistency applies to the
new key-permission role assignment: treat the first failure after granting as expected, not as a
wrong role. `ProtectKeysWithAzureKeyVault` adds a runtime dependency on Key Vault to the first
protect operation, which with `UseAntiforgery()` in the pipeline is the first rendered form — a
failure presents as forms breaking, not as a failed boot.

**Debug & observability — the warning that only appears once.** `No XML encryptor configured` is
logged only on the boot that *mints* a key, never again. Its absence from a log is therefore not
evidence of anything unless that boot minted a key. To observe the change, discard the existing key
rows first so the next protect operation mints one, then read the log. Confirm the outcome in the
data, not the log: the new row's `Xml` must contain an `<encryptedKey>` element rather than a
readable `<value>`.

**State sequencing — email uniqueness is only as strong as the user name.** `EmailIndex` is
non-unique and `RequireUniqueEmail` is an application-level check, so two concurrent registrations
for the same address can both pass it. Setting `UserName` to the email address at registration puts
the unique `UserNameIndex` behind the invariant and turns the race into a database error rather than
a duplicate account.

**User experience spec — the CI hazard is a status code.** An unauthenticated request for a gated
route must produce a `302` challenge to the login path, not a `401`. `verify_deploy.py` treats `401`
as a hard failure and `403` as transient, burning the full 120-second warm-up budget before failing
with misleading warm-up diagnostics. Registering the cookie handler as the default scheme with a
`LoginPath` is what makes the challenge a redirect.

**Timing & lifecycle — `WebApplicationFactory` needs a reachable entry point.** `Program.cs` uses
top-level statements, which generate an internal `Program` class. The test project cannot reference
it without either a `public partial class Program;` declaration in the application or an
`InternalsVisibleTo` entry.

---

## Phase 1: Close the key-ring exposure

### Overview

Verify the guarantee `F-02` left, then encrypt the ring at rest and prove the guarantee still holds.
Everything here happens while no account exists, which is what makes discarding the existing key
free.

**What this phase closes, and what it does not.** Encrypting the ring removes *session forgery* from
what database read access buys — that is the whole point, and it is why the phase runs before
Identity rather than after. It does **not** close database read access as a threat. The same access
still returns every `AspNetUsers.PasswordHash` row that Phase 2 is about to create, and no key-ring
encryption touches those. Do not read this phase as having closed the category.

What makes that residual exposure acceptable is the password policy, not the key ring. Identity
stores passwords as PBKDF2-HMAC-SHA512 with a per-user 128-bit salt and a high iteration count, and
`## Phase 3` sets a sixteen-character minimum on top of that — long enough that a stolen hash is
uneconomic to attack offline. **That is the actual security argument for the sixteen-character
minimum**, and it is load-bearing: the other two reasons the decision records — length beats
composition, and a forgotten password is a permanently dead account — are about usability, not about
this. Anyone reconsidering the length is trading against this paragraph.

### Changes Required:

#### 1. Baseline the inherited restart-survival guarantee

**File**: n/a — operational, against the deployed app.

**Intent**: Establish that key persistence works *before* changing anything about it, so a failure
after the change is attributable to the change. This is the check `AGENTS.md` says `S-01` owes, and
it is performed here rather than in a later phase because `/db-check` is the only surface that can
run it.

**Contract**: Render `/db-check` and leave the tab open without submitting; restart the container;
submit the already-rendered form. It must be accepted, not rejected with `400`. A key-count
comparison is recorded alongside it but is explicitly not the verdict — a ring with no reason to
rotate looks identical to a working one.

#### 2. A Key Vault key and a key-permission role assignment

**File**: `infra/main.bicep`

**Intent**: Give the app's managed identity a key it can wrap and unwrap with. The key is
infrastructure with no exported value, so unlike the four secrets it belongs in the template — the
same split the template already documents for the vault itself.

**Contract**: A `Microsoft.KeyVault/vaults/keys` child of the existing `vault`, RSA, with a name this
plan fixes so the application setting can reference it by identifier. Plus a second
`Microsoft.Authorization/roleAssignments` scoped to the vault, granting `Key Vault Crypto User` to
`site.identity.principalId`, following the existing `kvSecretsUser` assignment's shape and its
`guid(...)` naming. Add a dated characterisation comment above both, as every other resource in this
template carries.

**Also add an `output` for the key's identifier**, beside the five the template already declares
(`appUrl`, `planIsLinux`, `sqlServerFqdn`, `vaultUri`, `sitePrincipalId`). This is what stops the
next change from being a source-of-truth violation. The key identifier is not an independent value
like an API key — it is *derived* from a resource this template owns, so typing it into an app
setting by hand puts the key's name in two places with nothing linking them. A rename here would then
leave the app setting pointing at nothing, and that failure surfaces at the first rendered form
rather than at boot, which is the hard-to-attribute kind. An output makes the pasted value come *from*
the template rather than from a console reading.

Prefer the **versionless** key URI, so a future key rotation does not require re-setting the app
setting — the same reasoning as the versionless secret reference the connection string already uses.

#### 3. The Data Protection Key Vault package

**File**: `TenExCards/TenExCards.csproj`

**Intent**: Add the provider that `ProtectKeysWithAzureKeyVault` lives in.

**Contract**: `Azure.Extensions.AspNetCore.DataProtection.Keys`, pinned to an exact version
re-confirmed with `dotnet package search` at implementation time rather than taken from this plan.
It is not part of the shared framework, so it carries its own versioning independent of the 10.0.x
pins beside it.

#### 4. Publish the key identifier as an app setting — before the code that reads it

**File**: n/a — operational, `az webapp config appsettings set` against `tenexcards-ka`.

**Intent**: The encrypting build cannot boot without this value, and CI will never set it. It must
exist before the build that reads it is merged.

**Contract**: Set an app setting holding the key identifier of the key created in change 2, **read
from that deployment's output** rather than from a console reading:
`az deployment group show -g rg-tenexcards-plc -n <deployment> --query properties.outputs`. The value
is a pointer, not a secret, so it may be passed inline — unlike the connection string, which is a Key
Vault *reference*. Never in `appsettings*.json`.

**This is the answer to "doesn't an app setting break Bicep as the source of truth?"** It does not:
`infra/main.bicep`'s own header carves out exactly one exception — *"Anything set imperatively that
this template does not declare is drift — except `appSettings`, deliberately excluded"* — because
declaring even an empty `appSettings` block would make Bicep authoritative and delete the connection
string on the next routine deploy. The template owns the **key**; the app setting is only the
**pointer** the app reads it through, and app settings are the sole channel for that. Sourcing the
value from the template's output is what keeps the pointer honest without re-opening the hazard.

**Ordering is the whole point of this being its own change.** App settings are deliberately outside
the pipeline while a push to `main` *is* a production deploy, so merging change 5 first means
`new Uri(null)` during service configuration: the container does not serve, on a tier with no
deployment slots, and the only way out is another push. The setting is inert until code reads it —
setting it early costs nothing and removes the window entirely. Do this, confirm the site still
serves, and only then merge change 5.

Note that applying an app setting restarts the app. That restart is harmless here and is *not* the
restart change 6 needs; see that change.

#### 5. Encrypt the ring outside Development

**File**: `TenExCards/Program.cs`

**Intent**: Chain key encryption onto the existing `AddDataProtection().PersistKeysToDbContext<…>()`
call, guarded so local development is unaffected. Local development points at
`sqldb-tenexcards-dev`, whose ring protects nothing of value, and requiring vault key permissions on
the development machine would widen access that is already wider than anyone likes.

**Contract**: `.ProtectKeysWithAzureKeyVault(new Uri(keyIdentifier), new DefaultAzureCredential())`
applied only when the environment is not Development. The key identifier arrives as configuration,
not a literal — it is a pointer rather than a secret, so it may be set inline as an app setting with
`az webapp config appsettings set`, alongside the existing connection-string reference and never in
`appsettings*.json`. A comment must state why the guard exists, or a later agent will remove it as
an inconsistency.

#### 6. Discard the plaintext key

**File**: n/a — data-plane, T-SQL against `sqldb-tenexcards`.

**Intent**: Enabling encryption protects keys minted afterwards; the existing plaintext row stays
readable and stays the active key until it expires. Removing it forces a fresh, encrypted key to be
minted on the next protect operation.

**Contract**: Record the row's `Id` and `xml_len`, then — with the encrypting build live — **delete
the rows, restart the container, and only then exercise a form.** Outstanding antiforgery tokens
become invalid; no account or session exists to be affected.

**The restart is load-bearing and must not be dropped as a redundant step.** Data Protection
resolves the key ring once and caches it — `KeyRingRefreshPeriod` defaults to 24 hours — so the
running app is still holding the plaintext key it loaded at the encrypting build's own deploy
restart. Deleting the row does not invalidate that cache: a form exercised without a restart
succeeds using a key that no longer exists in the database, and mints nothing.

That failure is silent and reads as the feature not working. The key count would be zero rather than
one, the minting boot the next criterion inspects would never happen, and there would be no new row
to assert ciphertext on — three criteria failing for a reason none of them names.

#### 7. Re-verify the guarantee against the encrypted ring

**File**: n/a — operational.

**Intent**: Prove that encryption did not break the property `F-02` established. Encryption and
persistence fail in different places, and a passing deploy is not evidence of either.

**Contract**: Repeat change 1's check — render, restart, submit — against the encrypted ring.

### Success Criteria:

#### Automated Verification:

- `az bicep build --file infra/main.bicep` compiles with no `BCP` diagnostic
- `az deployment group what-if` run, snapshots taken before and after with `az appservice plan show`, `az webapp config show` and `az webapp config appsettings list`, and the post-deploy diff reconciled against the prediction — regardless of how the prediction looked
- ~~`az keyvault key show` returns the new key with an enabled status~~ **Superseded during Phase 1 — do not run this.** The operator holds `Key Vault Secrets Officer`, which confers nothing on a *key*, so this data-plane read answers `(Forbidden)` indistinguishably from a missing key. Substituted with the ARM control-plane read; see `### Phase 1 finding: the operator cannot run the plan's own key-verification command` in `change.md`
- **The check is proven before it is trusted**: the `az rest` GET against `providers/Microsoft.Authorization/roleAssignments` first returns the **existing** `Key Vault Secrets User` assignment for the site principal, which `infra/main.bicep` already declares as `kvSecretsUser`. An empty result here means the command is wrong, not that a role is missing — the failure `lessons.md` records under "Prove the check before trusting the result"
- Only then: the same command returns the new `Key Vault Crypto User` assignment for the same principal. Not `az role assignment`, every command of which is unusable on this subscription
- `az bicep build` shows the template declares a key-identifier `output` alongside its existing five
- The key-identifier app setting is present on `tenexcards-ka` and its value is **byte-identical to that deployment output**, compared directly rather than eyeballed, and read back with `az webapp config appsettings list` **before the encrypting build is merged**
- `dotnet build` succeeds with the new package at its pinned version
- The startup log for the boot that mints the new key does **not** contain `No XML encryptor configured`
- ~~The new `DataProtectionKeys` row's `Xml` contains an `<encryptedKey>` element and no readable `<value>` element~~ **Half superseded during Phase 1 — the `<value>` half is wrong.** `<value>` appears in *both* forms — as the master key when plaintext, as the ciphertext payload nested inside `<encryptedKey>` when encrypted — so asserting its absence reports a false failure on a correct row and invites re-running an irreversible deletion that already worked. Assert instead on the presence of `AzureKeyVaultXmlDecryptor` and the absence of `<masterKey>` and of the literal comment `Warning: the key below is in an unencrypted form.`; see the Phase 1 finding on `<value>` in an encrypted key row, in `change.md`

#### Manual Verification:

- Before the change: a `/db-check` form rendered before a restart is accepted when submitted after it, rather than rejected with `400`
- After the app setting is applied and **before** the encrypting build is merged, the site still serves — the setting is inert to a build that does not read it, and confirming that separates an app-setting mistake from a code mistake
- The pre-change key row's `Id` and `xml_len` are recorded, and the row is confirmed readable as plaintext, so the after state has something to be compared against
- After the change: the same render-restart-submit check passes again against the encrypted ring
- Exactly one key row exists after the change, and it is not the row recorded before it
- The what-if reconciliation is written into `change.md` as a table of predicted line against observed diff, with a verdict per line

**Implementation Note**: Do not start Phase 2 until the encrypted ring has passed the
render-restart-submit check. Every later phase depends on that ring, and Phase 3 makes it sign auth
cookies. Pause here for manual confirmation.

---

## Phase 2: Identity data model

### Overview

Put the four Identity tables in the database and nothing else. No UI, no DI for sign-in, no
authorization. The only variable under test is a forward-only migration on the boot path.

### Changes Required:

#### 1. The user entity

**File**: `TenExCards/Data/ApplicationUser.cs`

**Intent**: Introduce the application's own user type now, so `S-02`'s card-to-owner relationship and
`S-06`'s per-account outcome recording are additive rather than a type change rippling through the
context base, DI and every component.

**Contract**: `ApplicationUser : IdentityUser`, no members. A remark should say why it is empty, so
it is not deleted as speculative generality before `S-02` arrives.

#### 2. Switch the context base

**File**: `TenExCards/Data/AppDbContext.cs`

**Intent**: Give Identity's EF stores a context to bind to, on the role-free base, keeping the
existing Data Protection responsibility.

**Contract**: `AppDbContext : IdentityUserContext<ApplicationUser>, IDataProtectionKeyContext`,
preserving the primary-constructor style and both existing `DbSet` members. `IdentityUserContext`
rather than `IdentityDbContext` is the decision: it creates no role tables, which makes
`AGENTS.md`'s "never add roles" structural instead of conventional under forward-only migrations.
Add the `OnModelCreating` override the base now requires — the class currently has none — and note in
a comment that `AddRoles<>` will fail against this base by design.

#### 3. The Identity EF package

**File**: `TenExCards/TenExCards.csproj`

**Intent**: Add Identity's Entity Framework stores.

**Contract**: `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, pinned to the same 10.0.x patch as
the EF Core packages beside it, re-confirmed with `dotnet package search` before pinning.

#### 4. The migration

**File**: `TenExCards/Migrations/<timestamp>_<name>.cs` and the regenerated model snapshot

**Intent**: Add the Identity schema, forward-only, leaving `DataProtectionKeys` and `SpineProbes`
untouched.

**Contract**: One migration whose `Up` creates exactly `AspNetUsers`, `AspNetUserClaims`,
`AspNetUserLogins` and `AspNetUserTokens` with the unique `UserNameIndex` and the non-unique
`EmailIndex`. It must not drop, recreate or alter `DataProtectionKeys` — a base-class change that
also rewrites the key table would discard the ring Phase 1 just encrypted. No `Down` behaviour is
authored or relied on. `SpineProbes` is left alone; Phase 5 drops it in its own migration.

#### 5. Apply to the development database first

**File**: n/a — operational, local.

**Intent**: The standing rule: no migration reaches the app's database before it has applied cleanly
to `sqldb-tenexcards-dev`.

**Contract**: A local run against the user-secrets connection string, which names the development
database and the contained `tenexdev` user. Confirm `__EFMigrationsHistory` there gained the new
migration and that `db_owner` inside that database was sufficient to create the schema.

### Success Criteria:

#### Automated Verification:

- `dotnet build` succeeds with the Identity package at its pinned version
- `dotnet tool restore` succeeds and `dotnet ef migrations list --project TenExCards/TenExCards.csproj` lists the new migration after `InitialSpine`
- The migration source contains `AspNetUsers`, `AspNetUserClaims`, `AspNetUserLogins` and `AspNetUserTokens` and contains none of `AspNetRoles`, `AspNetRoleClaims`, `AspNetUserRoles` or `AspNetUserPasskeys`
- The migration source creates `UserNameIndex` with `unique: true`
- The migration source contains no `DropTable` or `CreateTable` naming `DataProtectionKeys`, and none naming `SpineProbes`
- A local run applies the migration to the development database and its `__EFMigrationsHistory` contains it
- The CI run is green and the deployed startup log reports applying exactly one pending migration, naming it, followed by `Migrations applied successfully`
- `scripts/verify_deploy.py` passes against the deployed site

#### Manual Verification:

- The four `AspNet*` tables exist in `sqldb-tenexcards` after the deploy, and `DataProtectionKeys` still holds the single encrypted row Phase 1 left
- `SpineProbes` is still present and `/db-check` still round-trips, because Phase 3 needs neither gone yet

**Implementation Note**: The app has no authentication wiring at this point and must behave exactly
as it did before. If anything user-visible changed, something in this phase did more than it was
asked to. Pause here for manual confirmation.

---

## Phase 3: Register, sign in, sign out

### Overview

The slice's actual outcome. Three hand-written statically-rendered pages, the DI and middleware that
make them work, and an authorization boundary whose default is "protected".

### Changes Required:

#### 1. Authentication, Identity and authorization services

**File**: `TenExCards/Program.cs`

**Intent**: Register the cookie scheme, Identity core with this project's password and sign-in
policy, the EF stores against the existing scoped `AppDbContext`, and a fallback authorization
policy that requires an authenticated user.

**Contract**: `AddAuthentication` defaulting to `IdentityConstants.ApplicationScheme` with
`AddIdentityCookies`; `AddIdentityCore<ApplicationUser>` with `RequireConfirmedAccount` false,
`RequireUniqueEmail` true, `Password.RequiredLength` 16 and every character-class requirement
disabled, then `AddEntityFrameworkStores<AppDbContext>` and `AddSignInManager`;
`ConfigureApplicationCookie` with a seven-day `ExpireTimeSpan`,
`SlidingExpiration` true and a `LoginPath`; `AddCascadingAuthenticationState`; and an authorization
fallback policy requiring an authenticated user.

**No `AddDefaultTokenProviders`, deliberately.** It registers the email-confirmation, phone and
authenticator token providers — precisely the surface `## What We're NOT Doing` forbids — and nothing
in this slice mints or consumes a token. Register, sign in, sign out and lockout all work without it.
This is the same judgement that hand-writes three pages rather than inheriting the template's 47
files: absence beats deletion, and a later slice that needs a token provider adds one line.

**Password *storage* is inherited, not configured, and that is the intent.** `AddIdentityCore`
registers the default `IPasswordHasher<ApplicationUser>`; `ApplicationUser` inherits `PasswordHash`
from `IdentityUser` and has no plaintext password property, so a password never reaches the entity
or the database. Do not register a custom hasher and do not set `PasswordHasherOptions` — the
default is PBKDF2-HMAC-SHA512 with a per-user salt, the stored format carries a version marker so
`UserManager` can rehash transparently if the default ever moves, and the work factor is left
untuned deliberately (see `## Performance Considerations`). Because it is inherited rather than
written, **Phase 4 asserts it** rather than trusting that it is still what this plan assumed.

The password policy is a deliberate deviation from Identity's defaults — length over composition,
because a forgotten password is a permanently dead account here, and because sixteen characters is
what makes a stolen `PasswordHash` uneconomic to attack offline (see `## Phase 1`'s overview) — and
needs a comment saying so or a
later agent will restore the defaults as a fix.

#### 2. Middleware ordering

**File**: `TenExCards/Program.cs`

**Intent**: Insert authentication and authorization into a pipeline that currently has neither.

**Contract**: `UseAuthentication()` then `UseAuthorization()`, placed **before** the existing
`UseAntiforgery()`. Do not add `UseHttpsRedirection()`; its absence is a recorded decision backed by
`httpsOnly: true` in the template. Do not set `ASPNETCORE_FORWARDEDHEADERS_ENABLED` — it is on by
platform default, and setting it to `false` silently strips `Secure` from the auth cookie.

#### 3. The logout endpoint

**File**: `TenExCards/Components/Account/IdentityEndpoints.cs` (or equivalent)

**Intent**: Sign-out must be a `POST` to a real endpoint rather than a component action, because
clearing the cookie is a response-header operation and because a `GET` sign-out is trivially
triggerable cross-site.

**Contract**: A single mapped `POST` endpoint that calls `SignInManager.SignOutAsync` and redirects
to the home page, mapped after `MapRazorComponents<App>()`. This is a trimmed equivalent of the
template's `MapAdditionalIdentityEndpoints`, carrying only the logout route — the external-login and
personal-data routes it also maps are out of scope.

#### 4. Identity infrastructure components

**File**: `TenExCards/Components/Account/` — redirect manager, revalidating authentication state
provider, status message, redirect-to-login

**Intent**: Reuse the four pieces of the .NET 10 template that are genuinely infrastructure rather
than forbidden surface, instead of reinventing them.

**Contract**: An `IdentityRedirectManager` for navigation that must survive a static-rendered POST;
an `IdentityRevalidatingAuthenticationStateProvider` registered as the `AuthenticationStateProvider`,
so a deleted or changed account stops being trusted by a long-lived circuit rather than being trusted
until it reconnects; a status-message component; and a redirect-to-login component for the
unauthorized route case. Adapted to `ApplicationUser` and to this project's namespaces.

#### 5. Register, Login and Logout pages

**File**: `TenExCards/Components/Account/Pages/Register.razor`, `Login.razor`, and a logout
confirmation surface

**Intent**: The three FR-001 through FR-003 surfaces, written rather than scaffolded, so nothing
forbidden exists to be routed to.

> **Implemented differently — only two of the three are pages.** Sign-out became a POST endpoint
> (`MapIdentityLogout()`), not a logout confirmation page, because clearing the cookie is a
> response-header operation a component action cannot perform. See the Phase 3 finding on sign-out
> in `change.md`. The heading above names three *surfaces*, not three pages.

**Contract**: Statically rendered — **no `@rendermode`** — using the `EditForm Model="…"
method="post" FormName="…"` pattern `DbCheck.razor` already proves, with no `<AntiforgeryToken />`
alongside it. Registration sets `UserName` to the submitted email address, which is what puts the
unique `UserNameIndex` behind email uniqueness; it signs the new account in and redirects. Sign-in
passes `lockoutOnFailure: true` and handles the locked-out result with its own message. Both take a
`[CascadingParameter] HttpContext`, as `Error.razor` already demonstrates. Neither page links
password recovery, email confirmation, two-factor or external providers, because none exist.

The password property on each form model carries `[DataType(DataType.Password)]`, so the field
renders masked and is marked as sensitive wherever model state is surfaced. The model holds the
plaintext only for the duration of the request — it is handed to `UserManager` / `SignInManager` and
never assigned to `ApplicationUser`, which has no property to receive it. Do not log the model, and
do not echo a submitted password back into the form on a validation failure.

#### 6. Route-level authorization and cascading state

**File**: `TenExCards/Components/Routes.razor`

**Intent**: Make the router authorization-aware, so a gated route renders a redirect rather than its
content.

**Contract**: `AuthorizeRouteView` in place of `RouteView`, with a `NotAuthorized` fragment
redirecting to login, preserving the existing `NotFoundPage` and `FocusOnNavigate`.

#### 7. The anonymous allowlist

**File**: `TenExCards/Components/Pages/Home.razor`, `Error.razor`, `NotFound.razor`, and the Account
pages

**Intent**: With a fallback policy in place, everything is protected unless it says otherwise. Four
things must say otherwise — and the allowlist must cover *endpoints*, not only page components.

**Contract**: `[AllowAnonymous]` on the home page, the error page, the not-found page and the
Identity pages. The home page gains an `AuthorizeView` so it reads sensibly to a signed-out visitor
and to a signed-in one. (The build marker itself is in `MainLayout.razor`'s footer, not on the home
page, so it renders on every page including the login page; `/` stays anonymous because
`verify_deploy.py` fetches the root and nothing else.)

**A fallback policy applies to every endpoint that carries no authorization metadata, and this
pipeline endpoint-routes more than page components.** `MapStaticAssets()` and
`AddInteractiveServerRenderMode()` both map endpoints. If either inherits the fallback policy, every
stylesheet and script 302s to the login path and the deployed site renders unstyled.

**`verify_deploy.py` cannot detect that failure**, which is why this is called out rather than left
to Phase 3's criteria. The script sends no cookies and follows redirects, so a gated asset resolves
`302 → /Account/Login → 200` and is recorded as a pass; it asserts status only, never content type.
The documented failure mode it *does* catch is an asset that `404`s. This one returns `200`.

Therefore: **verify this locally against `sqldb-tenexcards-dev` before the Phase 3 deploy** — the
phase already requires a local run — by loading `/` signed out and confirming the stylesheet
responses are `text/css` rather than the login page. If they are not, add explicit anonymous
authorization metadata to the static-asset and Blazor-hub endpoint builders. Do not discover this
from production.

#### 8. Sign-in display and navigation

**File**: `TenExCards/Components/Layout/MainLayout.razor`, `NavMenu.razor`, `_Imports.razor`

**Intent**: Give the learner somewhere to see who they are signed in as and to sign out.

**Contract**: The currently empty top row in `MainLayout` gains an `AuthorizeView` showing the email
address and a sign-out form posting to the logout endpoint when signed in, and register and sign-in
links when not. `_Imports.razor` gains the authorization and authentication-state namespaces it
currently lacks. `NavMenu.razor` is not given Identity entries — the top row owns that.

### Success Criteria:

#### Automated Verification:

- `dotnet build` succeeds
- An unauthenticated request for a gated route returns `302` with a `Location` pointing at the login path — **not** `401`, which `verify_deploy.py` treats as a hard failure
- An unauthenticated `GET /` returns `200` and its body contains the build marker
- The rendered login page references at least one same-origin stylesheet or script, so `verify_deploy.py`'s asset assertion cannot trip if the root is ever gated later
- Signed out and running locally, every same-origin stylesheet and script referenced by `/` responds `200` with its own content type — `text/css` or a JavaScript type, **not** `text/html`, which is what a fallback-policy redirect to the login page returns and what `verify_deploy.py` records as a pass
- The same content-type check passes against the deployed site after the Phase 3 deploy
- `scripts/verify_deploy.py` passes against the deployed site
- A repository search finds no route or component for password reset, forgot password, email confirmation, two-factor or external login

#### Manual Verification:

- A new email address and a sixteen-character password register successfully and arrive signed in
- Signing out returns the learner to an anonymous home page, and a gated route then redirects to login
- Registering a second account with an already-used email address is refused with a message that names the reason
- A password shorter than sixteen characters is refused before the account is created, and the message says the length requirement
- Six consecutive failed sign-ins produce a lockout that expires on its own within roughly five minutes
- **Sign in, restart the container, and confirm the session survives** — the auth-cookie form of the key-ring check, and the strongest evidence this slice can produce that encryption did not break persistence
- The auth cookie carries `Secure` and `HttpOnly` over the deployed HTTPS origin

**Implementation Note**: The restart-survival check above supersedes the `/db-check` form check as
this slice's evidence. Do not start Phase 5 until it has passed — that is the phase that deletes the
fallback. Pause here for manual confirmation.

---

## Phase 4: Test project and CI gate

### Overview

`TenExCards.Tests` lands here because this is the first slice with an application rule worth
asserting, and it gates the deploy because a suite that gates nothing decays.

### Changes Required:

#### 1. A solution file

**File**: `TenExCards.sln` at the repository root

**Intent**: Give `dotnet build` and `dotnet test` a single entry point now that a second project
exists.

**Contract**: A classic `.sln` listing both projects. `deploy.yml` must keep publishing
`TenExCards/TenExCards.csproj` by path — `dotnet publish` against a two-project solution is
ambiguous — and that constraint needs a comment at the publish step, because adding a solution is
exactly the change that invites someone to simplify the path away.

#### 2. The test project

**File**: `TenExCards.Tests/TenExCards.Tests.csproj`

**Intent**: An xUnit project positioned to test the deployed application's own rules.

**Contract**: xUnit, `Microsoft.AspNetCore.Mvc.Testing` at the same 10.0.x patch as the application's
packages, an EF provider suitable for tests, a project reference to `TenExCards`, and
**AwesomeAssertions** — never FluentAssertions, whose v8 moved to a paid commercial licence and whose
API is identical enough that the training-data reflex compiles cleanly and introduces the licensing
problem silently.

#### 3. Entry-point reachability and a skippable boot-path migration

**File**: `TenExCards/Program.cs`

**Intent**: `WebApplicationFactory` needs to name the application's entry point, and top-level
statements generate an internal `Program`. It also needs the boot path to be survivable without a
database — which the test project cannot arrange on its own.

**Contract**: Two changes, both in this file and both required by the harness.

First, a `public partial class Program;` declaration at the end of the file, with a comment saying
the test project depends on it. The alternative, `InternalsVisibleTo`, opens more surface than it
needs to.

Second, a configuration-controlled guard around the existing startup migration block. **This cannot
be done from the test project.** `WebApplicationFactory` runs the entry point and intercepts at
`IHost.Start()`, so everything between `builder.Build()` and `app.Run()` — the whole
`GetPendingMigrationsAsync()` / `MigrateAsync()` block — executes in tests, after
`ConfigureTestServices` has already swapped the provider. Against an EF in-memory provider,
`GetPendingMigrationsAsync()` throws.

The guard reads a configuration flag defaulting to *run the migration*, so the deployed boot path is
unchanged and a missing setting can never silently skip a migration. Only the test factory sets it.
It needs a comment in the style the surrounding block already uses, saying the test harness depends
on it and that inverting the default would turn a schema failure into a silent one.

The same reasoning applies to the connection string. `Program.cs` throws on a null
`ConnectionStrings:DefaultConnection` before any service replacement can run, so the factory must
supply a value through `UseSetting`. Criterion 4.7 is therefore stated as "no *real* connection
string and no network", not "no connection string present" — the harness supplies a dummy one and
the assertion is that nothing reaches Azure SQL.

#### 4. A test host that does not touch a real database

**File**: `TenExCards.Tests/` — web application factory fixture

**Intent**: The auth boundary must be assertable without Azure SQL, a connection string, or a
network.

**Contract**: A factory that replaces the `AppDbContext` registrations — **both** of them, the
factory and the scoped shim — with a test provider; supplies a dummy `DefaultConnection` through
`UseSetting`, since `Program.cs` throws on a null one before any replacement runs; sets the
migration-skip flag from change 3, which is the only way to stop the boot-path migration; and
disables the Key Vault key protector so the environment guard cannot reach out to Azure.

On the environment guard: verify what `WebApplicationFactory` actually sets `ASPNETCORE_ENVIRONMENT`
to before relying on it either way. If it is `Development`, the Phase 1 guard already suppresses the
protector and the explicit disable is belt-and-braces; if it is not, the explicit disable is what
stops a unit test authenticating to Azure. Do not assume.

#### 5. Auth boundary tests

**File**: `TenExCards.Tests/` — boundary and registration tests

**Intent**: Assert the invariant that will stop `S-02` leaking one learner's cards to another.

**Contract**: Anonymous requests to the home, error and not-found routes succeed; an anonymous
request to a gated route redirects to the login path rather than returning `401`; a register,
sign-in, sign-out round trip succeeds and the gated route is reachable only between sign-in and
sign-out; a password shorter than the configured length is refused before an account exists; a
duplicate email address is refused.

**Two more, both from `## Testing Strategy`, which previously named tests no change delivered.**

First, **assert that a registered user's `UserName` equals the submitted email address.** This is the
one assertion that protects the invariant this plan flags three times: `EmailIndex` is non-unique, so
the only database-level guard on email uniqueness is the unique `UserNameIndex` standing behind it.
The duplicate-email test above does *not* cover it — that passes through `RequireUniqueEmail` in
application code whether or not `UserName` is the email, so the race the plan predicts would reopen
silently with every test still green.

Second, **assert the configured policy rather than Identity's defaults**: password length sixteen,
every character-class requirement off, lockout enabled, and a seven-day sliding cookie expiry. These
read `IOptions` off the test host and need no database. They are what stops a later agent restoring
the defaults as a fix — the deviation each carries a comment about is worth an assertion too.

Third, **assert password storage, which is the one security-relevant setting this slice inherits
rather than writes.** Resolve `IPasswordHasher<ApplicationUser>` from the test host and assert it is
the framework's `PasswordHasher<ApplicationUser>` and not a substitute; assert
`PasswordHasherOptions.CompatibilityMode` is `IdentityV3`; and record its `IterationCount` in an
assertion rather than a comment, so a future framework change to the default surfaces as a failing
test naming the old and new values instead of passing silently. Additionally, assert that a
registered user's `PasswordHash` is neither null nor equal to the submitted password — a
one-line guard against the class of mistake where a custom hasher or a mis-wired store round-trips
plaintext.

**Pin the iteration count to whatever the framework actually returns at implementation time, not to
a number taken from this plan.** The default was raised once already (10,000 → 100,000 in .NET 8);
the assertion's job is to make the *next* change visible, not to assert a value this document
guessed.

#### 6. The CI test step

**File**: `.github/workflows/deploy.yml`

**Intent**: Make a failing test block a deploy, on a branch where a push is a production deploy.

**Contract**: A `dotnet test` step placed between `Set up .NET` and `Publish`, keeping the workflow a
thin caller over commands a human runs identically. **Name the test project by path** —
`dotnet test TenExCards.Tests/TenExCards.Tests.csproj` — for the same reason the publish step names
its csproj: a bare `dotnet test` at the repository root resolves the new solution and pulls in the
non-test application project. The solution stays an IDE and local-build convenience, never something
CI discovers implicitly. The existing deliberate settings are untouched: no `-o` on publish,
`--track-status false` on deploy, and `permissions` exactly `id-token: write` plus `contents: read`.

### Success Criteria:

#### Automated Verification:

- `dotnet build TenExCards.sln` builds both projects
- `dotnet test` passes locally from a clean `dotnet tool restore`
- A test asserts that a registered user's `UserName` equals the submitted email address, and it fails when registration is changed to leave `UserName` unset
- Tests assert the configured password length, disabled character classes, enabled lockout and seven-day sliding cookie, rather than Identity's defaults
- Tests assert password storage: the resolved hasher is the framework's `PasswordHasher<ApplicationUser>`, `CompatibilityMode` is `IdentityV3`, the iteration count matches the value observed at implementation time, and a registered user's `PasswordHash` is neither null nor equal to the submitted password
- A repository search finds `AwesomeAssertions` and no reference to `FluentAssertions`
- The CI run is green and its step list shows the test step running before publish
- `deploy.yml` names `TenExCards/TenExCards.csproj` at the publish step and `TenExCards.Tests/TenExCards.Tests.csproj` at the test step — neither resolved implicitly through the solution
- The migration-skip flag defaults to running the migration: with the flag unset, a local run still applies pending migrations to the development database

#### Manual Verification:

- A deliberately failing test is pushed; the run fails at the test step and **the deploy step never runs** — confirmed by reading the failed run's step list, not its colour — and the failing test is then reverted
- The test suite passes with no *real* connection string and no network reachable, proving it does not touch Azure SQL — the harness supplies a dummy connection string because `Program.cs` refuses to start without one

**Implementation Note**: The negative test is the point of this phase. A test step that has never
been observed failing has not been shown to gate anything. Pause here for manual confirmation.

---

## Phase 5: Retire the proof-of-life surfaces

### Overview

Both scaffolding pages go, along with the entity and table behind one of them. The auth-cookie
restart-survival check from Phase 3 has already replaced what `/db-check` was kept for.

### Changes Required:

#### 1. The circuit proof page and both of its entry points

**File**: `TenExCards/Components/Pages/CircuitCheck.razor`, `Components/Layout/NavMenu.razor`,
`Components/Pages/Home.razor`

**Intent**: Delete the page and every route to it. It is reachable from two places, which is the
documented reason this deletion list is longer than it looks.

**Contract**: The page file, its nav entry, and the in-body link in the home page's second paragraph
— which must be rewritten rather than merely stripped of its link, since the sentence exists to
explain that page.

#### 2. The database proof page and its single entry point

**File**: `TenExCards/Components/Pages/DbCheck.razor`, `Components/Layout/NavMenu.razor`

**Intent**: Delete the page and its one nav entry.

**Contract**: The page file and the nav entry, together with the comment above that entry declaring
itself the only entry point — the comment describes a deletion that has now happened. Both nav
entries live in the same file; it is the one file holding something for both surfaces.

#### 3. The probe entity and its `DbSet`

**File**: `TenExCards/Data/SpineProbe.cs`, `TenExCards/Data/AppDbContext.cs`

**Intent**: Remove the entity now that nothing renders it.

**Contract**: Delete the entity file and its `DbSet` member. `DataProtectionKeys` and the Identity
configuration are untouched.

#### 4. A migration that drops the table

**File**: `TenExCards/Migrations/<timestamp>_<name>.cs` and the regenerated model snapshot

**Intent**: Remove `SpineProbes` from the schema, forward-only like every other migration here.

**Contract**: One migration whose `Up` drops `SpineProbes` and touches nothing else. Applied to
`sqldb-tenexcards-dev` before the app's database, same as every migration in this project.

**`20260910190508_InitialSpine.cs` and its `.Designer.cs` are immutable and will keep naming
`SpineProbes` forever.** That is applied migration history — EF replays its source against
`__EFMigrationsHistory`, so editing it to make a repository search come up clean desynchronises the
schema from the history table. The only migration-directory file this phase changes is
`AppDbContextModelSnapshot.cs`, and `migrations add` rewrites it for you.

### Success Criteria:

#### Automated Verification:

- A search over `TenExCards/`, **excluding `TenExCards/Migrations/`**, finds no occurrence of `CircuitCheck`, `circuit-check`, `DbCheck`, `db-check` or `SpineProbe`
- `TenExCards/Migrations/AppDbContextModelSnapshot.cs` — which *is* live state, regenerated by `migrations add` — no longer contains a `SpineProbe` entity block, while `20260910190508_InitialSpine.cs` and its `.Designer.cs` are **unchanged** and still name `SpineProbes`
- Nothing under `context/changes/` or `context/deployment/` was edited to satisfy the search
- `dotnet build TenExCards.sln` and `dotnet test` both pass
- The migration source drops `SpineProbes` and contains no other table operation
- A local run applies it to the development database and `SpineProbes` is gone there
- The CI run is green and the deployed startup log reports applying exactly one pending migration
- Requests for the two retired routes return the not-found page

#### Manual Verification:

- `SpineProbes` is absent from `sqldb-tenexcards`, while the four `AspNet*` tables and the single encrypted `DataProtectionKeys` row are intact
- The navigation shows no stale entries, the home page reads coherently without the removed link, and `/` still renders the build marker

**Implementation Note**: This phase removes the fallback verification surface. It must not run before
Phase 3's auth-cookie restart-survival check has passed. Pause here for manual confirmation.

---

## Phase 6: Update the repository record

### Overview

The records that tell the next agent what exists. `AGENTS.md` carries a section explicitly written to
be deleted by this slice, with two paragraphs inside it that must survive that deletion by being
moved elsewhere.

### Changes Required:

#### 1. The scaffold-state surgery

**File**: `TenExCards/AGENTS.md`

**Intent**: Execute the deletion the document asks this slice to perform, without losing the two
paragraphs it warns a section-wide delete would silently drop.

**Contract**: Remove the scaffold-state parts — the missing-pieces list and the deletion list at the
end of that section — along with the Identity entry now satisfied and the references to both retired
pages. **Move, do not delete**: the persistence decision paragraph goes to the persistence section,
and the budget rule goes to `## Budget Posture` in `context/foundation/infrastructure.md`, which
already holds its full reasoning. Update the Data Protection bullet in `## Never do these`: key
persistence is now verified and encryption at rest is now done, so the sentence declaring encryption
open and owned by `S-01` no longer describes reality. Update `## Testing` — the test project now
exists, and that section says to move its rules next to the tests once it does, leaving the
FluentAssertions prohibition behind.

#### 2. The risk register

**File**: `context/foundation/infrastructure.md`

**Intent**: Close the key-ring encryption item and record what this slice did not close.

**Contract**: The row covering unencrypted keys is marked closed with the date and the mechanism.
ARR session affinity remains open and untouched. Receive the budget-rule paragraph moved out of
`AGENTS.md`. Note explicitly that branch protection and the CI principal's `Contributor` scope remain
open, so closing the key-ring row is not misread as closing the deployment trust boundary.

**Record the same distinction for database read access**, for the same reason. Closing the key-ring
row removes session forgery from what that access buys; it does not remove the access, and from
Phase 2 onward the same access returns every `AspNetUsers.PasswordHash`. State what carries that
residual risk — PBKDF2-HMAC-SHA512 with a per-user salt and a sixteen-character minimum, making a
stolen hash uneconomic to attack offline — so a future reader can see it was weighed rather than
missed, and knows which decision to revisit if the password policy is ever loosened.

#### 3. The deployment record

**File**: `context/deployment/deploy-plan.md`

**Intent**: Add a dated record for this slice's deploys, in the style of the four already there.

**Contract**: What was provisioned (the vault key and the second role assignment), the app setting
added for the key identifier, the migrations applied and in what order, the discarded plaintext key
row with its recorded identifier, and the what-if reconciliation. Include a "looks like a failure but
is not" note if any was encountered — that section has earned its place three times.

**Correct one runnable command that Phase 5 breaks.** The `F-02` record carries
`verify_deploy.py --base-url https://tenexcards-ka.azurewebsites.net/db-check`. That route no longer
exists, and the failure is specifically misleading: `404` is not in the script's transient set, so it
fails immediately with wording that says the app answered and this is not a warm-up problem —
a red that reads like a broken deploy. Mark it superseded with a note naming the route's removal,
in the style the document already uses for its superseded teardown and rollback sections.

**Everything else in that file stays.** The `/circuit-check → 200` route checks and the captured
`CREATE TABLE [SpineProbes]` startup log are dated measurements of what was true then; so is every
occurrence under `context/changes/`. Do not edit a record to make a search come up clean.

#### 4. Roadmap and change record

**File**: `context/foundation/roadmap.md`, `context/changes/accounts-and-sessions/change.md`

**Intent**: Reflect the slice's state and resolve the open question it owned.

**Contract**: `change.md` carries the phase findings and deviations discovered during implementation,
in the `### Phase N finding:` style the persistence slice established.

`roadmap.md` records `S-01` in **four** places, and the obvious two are not all of them. Update each:

1. The slice table row — status advances from `planning`.
2. The `### S-01` item body — its `**Status:**` line, and its `**Unknowns:**` entry resolved with the
   chosen seven-day sliding window rather than left standing.
3. `## Open Roadmap Questions` item 3 — the same inactivity-window question, carried verbatim from
   the PRD and marked `Block: S-01`. Strike it through and mark it resolved, in the style item 4
   already uses for the HTTPS-redirect question that `F-01` closed.
4. The `## Backlog Handoff` table — the `S-01` row still reads `Ready for /10x-plan: no` with
   `Needs F-01 and F-02`. Both landed and the plan exists.

Also correct the `F-02` item's line reading "Two tables exist (`SpineProbes`, a throwaway `S-01`
deletes, and …)" — Phase 5 makes it false, and it sits in a foundation document rather than a change
record, so it describes the present rather than a past measurement.

#### 5. The PRD's open question

**File**: `context/foundation/prd.md`

**Intent**: `## Open Questions` item 1 names the session inactivity window with the user as owner and
classes it a planning detail. It has now been decided.

**Contract**: Mark it resolved with the chosen window and a one-line rationale, in the style the
document already uses for its other resolved-after-generation note. Also flag the `hard_deadline`
frontmatter as superseded, since planning proceeded on the explicit decision to prioritise
correctness over that date.

### Success Criteria:

#### Automated Verification:

- A search of `TenExCards/AGENTS.md` **and `context/foundation/roadmap.md`** finds no occurrence of `CircuitCheck`, `DbCheck` or `SpineProbe` that describes the present tense
- The persistence paragraph and the budget rule are each present at exactly one location, and it is the new one
- All four `S-01` locations in the roadmap agree — slice table row, item body status, `## Open Roadmap Questions` item 3, and the `## Backlog Handoff` row — and the inactivity-window question no longer reads as open in either place it appears
- `context/deployment/deploy-plan.md` carries no runnable command targeting `/db-check`, and its dated `F-01`/`F-02` measurements are otherwise unedited
- No `file:line` citation was introduced in any edited document — cite property and section names, which a later edit cannot silently invalidate

#### Manual Verification:

- `TenExCards/AGENTS.md` reads start to finish as a fresh agent would, with no dangling antecedents and no reference to a deleted section
- All records agree about what exists, what it is called, and how the key identifier reaches the app
- The PRD's open question reads as decided rather than as still owned by the user

**Implementation Note**: This is the last phase. After it, a fresh agent reading only the repository's
own documents should be able to describe the auth boundary, the key ring's protection, and the test
project without reading this plan.

---

## Testing Strategy

### Unit Tests:

- Configured policy is what this project intends, not what Identity defaults to: password length,
  disabled character classes, lockout enabled, seven-day sliding cookie
- Password storage is what this plan assumed, even though the slice inherits it rather than writing
  it: the framework's own hasher, `IdentityV3` compatibility mode, the iteration count observed at
  implementation time, and a stored `PasswordHash` that is neither null nor the submitted password
- Registration sets the user name to the email address — the property that puts the unique index
  behind email uniqueness

### Integration Tests:

- Anonymous access: home, error and not-found routes serve without a session
- Gated access: a protected route redirects to the login path, with a `302` rather than a `401`
- Round trip: register, land signed in, reach a gated route, sign out, lose access to it
- Refusals: a short password and a duplicate email are both rejected before an account exists

Stub nothing beyond the data provider and the key protector. There is no LLM client yet; when it
arrives in `S-02` it becomes the only other test double.

### Manual Testing Steps:

1. Register with a sixteen-character password; confirm arrival at a signed-in home page
2. Sign out; confirm a gated route now redirects to login
3. Sign back in; restart the container from Azure; reload and confirm the session survived
4. Attempt registration with the same email address; confirm a clear refusal
5. Fail sign-in six times; confirm lockout, and confirm it lifts on its own
6. Inspect the auth cookie over HTTPS for `Secure` and `HttpOnly`
7. Read `DataProtectionKeys` and confirm the single row is ciphertext

## Performance Considerations

The 2-second acknowledgement budget belongs to generation, which this slice does not touch. Two
things here are nonetheless on a user path.

Sign-in performs a password hash verification, which is deliberately expensive; Identity's default
work factor on a B1 tier is acceptable for the handful of sign-ins this product sees and is not
tuned here.

`ProtectKeysWithAzureKeyVault` adds a Key Vault round trip to key ring unwrapping. It happens on key
ring resolution rather than per request, so it is a cold-start cost, not a per-request one. The
existing `EnableRetryOnFailure` on the SQL connection covers transient database faults; the Key Vault
call has its own retry in the Azure SDK defaults.

Blazor Server circuit memory is roughly 250 KB per user before application state. This slice adds an
authentication state to each circuit and nothing else; the passage-and-candidates memory pressure
`AGENTS.md` warns about arrives with `S-02`.

## Migration Notes

Two migrations, in two separate phases and two separate deploys: one adding the Identity tables, one
dropping `SpineProbes`. Both are forward-only, both run on the boot path through
`Database.MigrateAsync()` before the application serves, and both go to `sqldb-tenexcards-dev` before
the app's database.

A migration that throws means the container does not serve, on a tier with no deployment slots. The
rollback path is redeploying the retained previous archive — every deployed archive is kept 90 days —
and **that does not reverse schema**. The `Down` method EF generates is not a rollback story.

Phase 1 performs one irreversible data-plane operation that is not a migration: discarding the
existing Data Protection key rows. Record the row identifier and length before deleting. It is safe
only because no account exists yet, which is the reason that phase is first.

## References

- Slice definition: `context/foundation/roadmap.md`, item `S-01`
- Requirements: `context/foundation/prd.md` — FR-001 through FR-003, `## Access Control`, `## Open Questions` item 1
- The inherited key-ring finding: `context/changes/persistence-spine/change.md`, the phase 2 finding on unencrypted persistence
- The restart-survival check this slice owes: `TenExCards/AGENTS.md`, `## Never do these`, the Data Protection bullet
- The deletion list: `TenExCards/AGENTS.md`, `## The scaffold is not the target`
- Render-mode rationale for Identity pages: `context/changes/blazor-server-shell/plan.md`
- CI behaviour under a login redirect: `scripts/verify_deploy.py`, and `context/changes/deploy-pipeline/reviews/impl-review-phase-1-2.md`
- Recurring rules: `context/foundation/lessons.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Close the key-ring exposure

#### Automated

- [x] 1.1 az bicep build compiles main.bicep with no BCP diagnostic — 874cd61
- [x] 1.2 what-if run, snapshots taken, and the post-deploy diff reconciled against the prediction — 874cd61
- [x] 1.3 The key exists and is enabled — **verified by the ARM control-plane read, not by `az keyvault key show`**, which the operator cannot run (Phase 1 finding) — 874cd61
- [x] 1.4 The az rest roleAssignments GET first returns the existing Key Vault Secrets User assignment, proving the command works — 874cd61
- [x] 1.5 The same command then returns the new Key Vault Crypto User assignment for the site principal — 874cd61
- [x] 1.6 The template declares a key-identifier output alongside its existing five — 874cd61
- [x] 1.7 The key-identifier app setting is byte-identical to that deployment output, compared directly, before the encrypting build is merged — 874cd61
- [x] 1.8 dotnet build succeeds with the Data Protection Key Vault package pinned — 874cd61
- [x] 1.9 The key-minting boot's log does not contain No XML encryptor configured — 874cd61
- [x] 1.10 The new DataProtectionKeys row is ciphertext — **verified by AzureKeyVaultXmlDecryptor present and masterKey absent, not by the absence of a value element**, which is present in both forms (Phase 1 finding) — 874cd61

#### Manual

- [x] 1.11 Pre-change: a form rendered before a restart is accepted after it, not rejected with 400 — 874cd61
- [x] 1.12 The site still serves after the app setting is applied and before the encrypting build is merged — 874cd61
- [x] 1.13 The pre-change key row's identifier and length are recorded and confirmed plaintext — 874cd61
- [x] 1.14 Post-change: the render-restart-submit check passes against the encrypted ring — 874cd61
- [x] 1.15 Exactly one key row exists afterwards, and it is not the recorded one — 874cd61
- [x] 1.16 The what-if reconciliation is written into change.md as predicted against observed — 874cd61

### Phase 2: Identity data model

#### Automated

- [x] 2.1 dotnet build succeeds with the Identity EF package pinned — 175097f
- [x] 2.2 dotnet tool restore succeeds and migrations list shows the new migration after InitialSpine — 175097f
- [x] 2.3 The migration creates the four AspNet tables and none of the role or passkey tables — 175097f
- [x] 2.4 The migration creates UserNameIndex as unique — 175097f
- [x] 2.5 The migration touches neither DataProtectionKeys nor SpineProbes — 175097f
- [x] 2.6 A local run applies it to the development database and its history contains it — 175097f
- [x] 2.7 CI is green and the startup log applies exactly one pending migration, then reports success — 175097f
- [x] 2.8 verify_deploy.py passes against the deployed site — 175097f

#### Manual

- [x] 2.9 The four AspNet tables exist in the app database and the encrypted key row is intact — 175097f
- [x] 2.10 SpineProbes is still present and /db-check still round-trips — 175097f

### Phase 3: Register, sign in, sign out

#### Automated

- [x] 3.1 dotnet build succeeds — 606e084
- [x] 3.2 An unauthenticated gated route returns 302 to the login path, not 401 — 606e084
- [x] 3.3 An unauthenticated GET of the root returns 200 and its body contains the build marker — 606e084
- [x] 3.4 The rendered login page references at least one same-origin stylesheet or script — 606e084
- [x] 3.5 Locally and signed out, every same-origin stylesheet and script on the root responds 200 with its own content type, not text/html — 606e084
- [x] 3.6 The same content-type check passes against the deployed site after the Phase 3 deploy — 606e084
- [x] 3.7 verify_deploy.py passes against the deployed site — 606e084
- [x] 3.8 No route or component exists for password reset, email confirmation, two-factor or external login — 606e084

#### Manual

- [x] 3.9 Registration with a sixteen-character password succeeds and arrives signed in — 606e084
- [x] 3.10 Sign-out returns to an anonymous home page and a gated route then redirects to login — 606e084
- [x] 3.11 A duplicate email address is refused with a message naming the reason — 606e084
- [x] 3.12 A short password is refused before the account is created, naming the length requirement — 606e084
- [x] 3.13 Six failed sign-ins produce a lockout that lifts on its own within roughly five minutes — 606e084
- [x] 3.14 A signed-in session survives a container restart — 606e084
- [x] 3.15 The auth cookie carries Secure and HttpOnly over the deployed HTTPS origin — 606e084

### Phase 4: Test project and CI gate

#### Automated

- [x] 4.1 dotnet build against the solution builds both projects — dbd121e
- [x] 4.2 dotnet test passes from a clean dotnet tool restore — dbd121e
- [x] 4.3 A test asserts a registered user's UserName equals the submitted email, and fails when registration stops setting it — dbd121e
- [x] 4.4 Tests assert the configured password length, disabled character classes, lockout and seven-day sliding cookie — dbd121e
- [x] 4.5 Tests assert password storage: framework hasher, IdentityV3 compatibility mode, observed iteration count, and a PasswordHash that is neither null nor the submitted password — dbd121e
- [x] 4.6 AwesomeAssertions is referenced and FluentAssertions is not — dbd121e
- [x] 4.7 CI is green and its step list shows the test step running before publish — dbd121e
- [x] 4.8 deploy.yml names both csproj paths explicitly, neither resolved through the solution — dbd121e
- [x] 4.9 The migration-skip flag defaults to running the migration when unset — dbd121e

#### Manual

- [x] 4.10 A deliberately failing test fails the run and the deploy step never runs, read from the step list — dbd121e
- [x] 4.11 The suite passes with no real connection string and no network, proving it does not touch Azure SQL — dbd121e

### Phase 5: Retire the proof-of-life surfaces

#### Automated

- [x] 5.1 No occurrence of CircuitCheck, circuit-check, DbCheck, db-check or SpineProbe remains under TenExCards/, excluding TenExCards/Migrations/ — 9fccf42
- [x] 5.2 The model snapshot lost its SpineProbe block while InitialSpine and its Designer file are unchanged — 9fccf42
- [x] 5.3 Nothing under context/changes/ or context/deployment/ was edited to satisfy the search — 9fccf42
- [x] 5.4 dotnet build and dotnet test both pass — 9fccf42
- [x] 5.5 The migration drops SpineProbes and contains no other table operation — 9fccf42
- [x] 5.6 A local run applies it to the development database and SpineProbes is gone there — 9fccf42
- [x] 5.7 CI is green and the startup log applies exactly one pending migration — 9fccf42
- [x] 5.8 Requests for the two retired routes return the not-found page — 9fccf42

#### Manual

- [x] 5.9 SpineProbes is absent from the app database while the AspNet tables and the key row are intact — 9fccf42
- [x] 5.10 Navigation shows no stale entries, the home page reads coherently, and the build marker still renders — 9fccf42

### Phase 6: Update the repository record

#### Automated

- [x] 6.1 Neither AGENTS.md nor roadmap.md describes CircuitCheck, DbCheck or SpineProbe in the present tense — c0751b0
- [x] 6.2 The persistence paragraph and the budget rule each exist at exactly one, new, location — c0751b0
- [x] 6.3 All four roadmap S-01 locations agree and the inactivity-window question reads resolved in both places it appears — c0751b0
- [x] 6.4 deploy-plan.md carries no runnable /db-check command, and its dated measurements are otherwise unedited — c0751b0
- [x] 6.5 No file-and-line citation was introduced in any edited document — c0751b0

#### Manual

- [x] 6.6 AGENTS.md reads start to finish with no dangling antecedents or references to deleted sections — c0751b0
- [x] 6.7 All records agree on what exists, what it is called, and how the key identifier reaches the app — c0751b0
- [x] 6.8 The PRD's open question reads as decided rather than owned by the user — c0751b0
