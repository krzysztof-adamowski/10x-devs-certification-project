# Repository Guidelines

10xCards is a `net10.0` C# web app that turns a passage the learner pastes into flashcard
candidates they triage one at a time. Product spec: `context/foundation/prd.md`.
Stack rationale: `context/foundation/tech-stack.md`. Deployment: `## Deployment` below —
platform research and the risk register are in `context/foundation/infrastructure.md`.

**Two conventions everything below relies on, so no later section has to restate them.**

*Paths.* Agent sessions are rooted at the **repo root**, one level above this file, and paths here
are written from there: `context/`, `infra/`, `scripts/` and `.github/` are siblings of
`TenExCards/`, not children of it. **`TenExCards/` is a solution folder, not the project folder**
— alongside this file it holds `TenExCards.slnx` and two project subfolders one level deeper:
`TenExCards/TenExCards/` (the product code) and `TenExCards/TenExCards.Tests/` (`S-01`, which
carries its own `AGENTS.md` holding the testing rules). Product
code is named relative to that inner folder — one level below this file's own directory —
`Program.cs` means `TenExCards/TenExCards/Program.cs` and `Components/Pages/Home.razor` means
`TenExCards/TenExCards/Components/Pages/Home.razor`. That is also why `dotnet` needs
`TenExCards/TenExCards/` — `cd` there, or pass `--project TenExCards/TenExCards/TenExCards.csproj`
— or build the whole solution from the repo root with `dotnet build TenExCards/TenExCards.slnx`.
The double `TenExCards/TenExCards/` is not a typo; do not "simplify" it by flattening one level
back out, and do not assume a bare `TenExCards/<file>` mention elsewhere in this repository (CI,
scripts, older dated records) still resolves — check whether it predates the `S-01` restructure.

*Slice IDs.* `F-nn` and `S-nn` are roadmap slices, each defined in
`context/foundation/roadmap.md` alongside the change-id that implements it. Look one up there
rather than inferring it from context. Two recur below because they own live decisions:
**`F-02` (`persistence-spine`)**, which landed persistence on 2026-09-10, and **`S-01`
(`accounts-and-sessions`)**, which landed the account boundary, the encrypted key ring and the
test project on 2026-09-12.

## What is wired, and what is not

The project is a Blazor Web App with **per-page interactivity**: pages are static-rendered unless
they carry `@rendermode InteractiveServer`. That is not an aesthetic choice — it is what lets the
Identity pages sign a learner in without a carve-out, because `SignInManager` writes cookies to the
HTTP response and a circuit cannot. Those pages therefore carry **no** `@rendermode`, and neither
does anything else that posts a form.

**Accounts are live** as of 2026-09-12 (`S-01`, change `accounts-and-sessions`): ASP.NET Core
Identity for email + password, on `IdentityUserContext<ApplicationUser>` — the **role-free** base,
chosen so that "never add roles" below is structural rather than conventional, since `AddRoles<>`
fails against it by design. Register and sign in are two hand-written statically rendered pages
under `Components/Account/Pages/`, not the template's 47 files; `## Never do these` says what must
not be added back. **Sign-out is not a third page** — it is a POST endpoint, `MapIdentityLogout()`
in `Components/Account/IdentityEndpoints.cs`, because clearing the cookie is a response-header
operation a component action cannot perform and a `GET` sign-out is trivially triggerable
cross-site. `Components/Account/` itself holds no routable pages — only that endpoint and the four
support types borrowed from the template: a redirect manager, a revalidating auth-state provider,
and two non-routable components. Authorization defaults to **protected**: a fallback policy
requires an authenticated user,
so a new page is gated unless it says `[AllowAnonymous]` — and some endpoints need that marker
explicitly, see `### Authorization defaults to protected`.

Still missing: an **LLM client** for generation. Not scaffolded; no package or configuration
exists. `S-02` brings it, and adding it is expected work rather than scope creep.

### Authorization defaults to protected

`Program.cs` sets an authorization **fallback policy** requiring an authenticated user. A fallback
policy applies to every endpoint carrying no authorization metadata, and this pipeline
endpoint-routes more than page components — so the rule has a trap in it:

- **Page components** are gated unless they carry `[AllowAnonymous]`. Four surfaces do: `Home`,
  `Error`, `NotFound`, and the Identity pages as a folder. Add nothing else without a reason.
  **The Identity pages inherit theirs from `Components/Account/Pages/_Imports.razor`, which is the
  trap**: a new page dropped into that folder is anonymous the moment it exists, without anyone
  adding it to any list or writing an attribute anyone would review. A page belongs there only if
  an unauthenticated learner must reach it; anything else goes under `Components/Pages/`.
- **`MapStaticAssets()` carries `.AllowAnonymous()`, and removing it breaks the site silently.**
  Without it every stylesheet and script `302`s to the login path, and the page renders unstyled.
  **`scripts/verify_deploy.py` cannot catch this**: it sends no cookies and follows redirects, so a
  gated asset resolves `302 → /Account/Login → 200` and is recorded as a pass. It asserts status,
  never content type. Verify by checking that each asset answers `text/css` or a JavaScript type
  rather than `text/html`.
- **`AddInteractiveServerRenderMode()`'s builder is deliberately left un-anonymous.**
  `MapRazorComponents<App>()` returns one builder covering *every* page route, and an
  `IAllowAnonymous` marker anywhere in an endpoint's metadata short-circuits authorization for it —
  so marking that builder anonymous would exempt every future gated page, not just the circuit hub.
  It is unnecessary today because no anonymous page uses `@rendermode InteractiveServer`. Revisit
  the first time one does.
- **`MapIdentityLogout()` carries `.AllowAnonymous()`, and it is not an oversight that sign-out is
  reachable to the signed-out.** Posting it twice, or after the cookie has already expired, must
  end at the home page rather than `302` to login — a logout that redirects to a login form reads
  as a failed sign-out and invites the learner to try again. It is mapped **after**
  `MapRazorComponents<App>()`, per the .NET Identity template's own convention for account
  endpoints.

**Exactly two `.AllowAnonymous()` calls exist in the pipeline** — static assets and sign-out — and
the render-mode bullet above is the one place that deliberately refuses a third. If you find
yourself adding one, record here which of the two it resembles and why the render-mode reasoning
does not apply to it.

An unauthenticated request for a gated route must produce a **`302` to the login path, never a
`401`**: `verify_deploy.py` treats `401` as a hard failure and `403` as transient, burning the whole
warm-up budget before failing with misleading diagnostics. Registering the cookie handler as the
default scheme with a `LoginPath` is what makes the challenge a redirect.

## Never do these

Product:

- **Never persist a submitted passage.** It is unrecoverable once its candidates exist.
- **Never persist untriaged candidates, or add batch resume.** They are discarded when the
  session ends.
- **Never add password recovery.** A forgotten password is a dead account in v1.
- **Never add roles, sharing, admin views, decks, tags, or export.** The user model is flat;
  scope every query to the owning account.
- **Never let the form freeze.** Acknowledge a submission within 2s with continuous visible
  progress; generation is bounded at 30s.
- **Never hold more in a circuit than you must.** Blazor Server memory is per-user — roughly
  250 KB per circuit *before* application state — and this app deliberately keeps a whole passage
  plus its candidates there until triage ends. B1 gives 1.75 GB total and there is no
  back-pressure: the ceiling arrives as OOM restarts that look like random disconnects, and every
  restart drops every circuit. Enforce the passage-length bound *before* generation begins.
- **Never use FluentAssertions.** Assertions use AwesomeAssertions; see
  `TenExCards.Tests/AGENTS.md`.

Deployment — each bullet is the rule **and the consequence that enforces it**, because a
prohibition without its failure mode is one an agent talks itself out of. `## Deployment` holds
the diagnostic detail that consequence implies: the commands, the dates, the exact strings, and
how to tell a false positive from a real one. A fact goes in one or the other, never both:

- **Never run `az webapp up`.** It is deprecated. Deploy with
  `az webapp deploy --src-path <zip> --type zip`.
- **Never upload an archive `scripts/pack.py` has not passed.** Build it with that script — it packs
  the publish directory and then *reads the archive back* to check its shape, because the banned
  `Compress-Archive` fails precisely by writing something other than what it was asked to. **The
  script is the authority on the assertions**, currently four (`TenExCards.dll` at the archive root;
  no entry prefixed `publish/`; no entry containing a backslash; at least one entry under
  `wwwroot/`); this bullet describes them and must not be trusted over the code. Each failure
  deploys **successfully** and then breaks at runtime — a nested zip 503s, and backslash entries
  serve a page whose every asset 404s. **A passing deploy command is not evidence**; nor is a green
  `az webapp deploy`. Verify afterwards with `scripts/verify_deploy.py`, which fetches the page and
  asserts `200` for every same-origin asset it references. On a push to `main` CI runs both scripts
  for you — see `### A push to main deploys`; this bullet governs any deploy you drive by hand.
  Mechanism and measurements: the 2026-09-08 and 2026-09-10 records in
  `context/deployment/deploy-plan.md`.
- **Never provision the F1 App Service tier.** It caps WebSockets at five connections and has no
  Always On. B1 Linux is the floor.
- **Never remove Data Protection key persistence or its encryption. They are two guarantees, not
  one, and each fails differently.** Keys are neither persistent nor encrypted by default.
  `Program.cs` calls `AddDataProtection().PersistKeysToDbContext<AppDbContext>()` (`F-02`, so the
  ring lives in the `DataProtectionKeys` table and survives a restart) and then chains
  `.ProtectKeysWithAzureKeyVault(...)` outside Development (`S-01`, so the row is ciphertext). That
  ring now signs **auth cookies**, so losing either property logs every learner out or hands session
  forgery to anyone with database read access.
  **The guard around the encryption call is deliberate** — local development points at
  `sqldb-tenexcards-dev`, whose ring protects nothing, and requiring vault key permissions on a
  development machine would widen access. Do not remove it as an inconsistency.
  **Both were verified on 2026-09-12, and neither verification is a deploy reporting success.**
  Persistence: sign in, restart the container, reuse the pre-restart cookie — the session must
  still be signed in. Encryption: the single key row's `Xml` must carry `AzureKeyVaultXmlDecryptor`
  and must **not** carry `<masterKey>` or the literal comment
  `Warning: the key below is in an unencrypted form.` Do **not** test for `<value>`; it appears in
  both forms, nested inside `<encryptedKey>` when correct, so matching it reports a false failure on
  a correctly encrypted row. Counting key rows is also not the verdict — a ring with no reason to
  rotate looks identical to a working one.
  `No XML encryptor configured` is logged only on the boot that **mints** a key, never again, so its
  absence from a log proves nothing unless that boot minted one.
- **Never set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=false`, and never add `ASPNETCORE_HTTPS_PORT`.**
  Neither is needed, and setting the first to `false` is not a no-op: it **silently** stops HSTS —
  no warning, no log line, no failing request — and, since `S-01` landed accounts, it also strips
  `Secure` from the auth cookie under the default `CookieSecurePolicy.SameAsRequest`, handing every
  live session to anyone on the path. Mechanism and the `307`
  loop: `### HTTPS: what enforces it, and what must never be set`.
- **Never run `az deployment group create` with `--mode Complete`.** It deletes every resource
  in the group absent from the template — the plan and web app included. Incremental is the
  default and the only mode used here.
- **Never declare `appSettings` in `infra/main.bicep`.** It makes the template authoritative, so
  a routine, successful-looking deployment then deletes the LLM API key and connection string.
  Secrets stay outside IaC, set with `az webapp config appsettings set` / Key Vault references.
  The general principle this is an instance of: **the vault is infrastructure and belongs in the
  template; secret *values* are data-plane and never do.** `infra/main.bicep` declares
  `kv-tenexcards-plc`; the four secrets in it were written with `az keyvault secret set --file`.
  The same split explains why the template declares no `secrets` child resource.

- **Never let a connection string or password reach a tracked file.** Not `appsettings.json`, not
  `appsettings.Development.json`, not a `.parameters.json` beside `main.bicep`, not a test fixture.
  Deployed, the app reads the app setting `ConnectionStrings__DefaultConnection`, whose value is a
  Key Vault *reference* (a pointer, not a secret — safe to pass inline). Locally it comes from
  `dotnet user-secrets`, keyed by the `UserSecretsId` in `TenExCards.csproj`. `.gitignore` blocks
  `infra/*.parameters.json`, but that is a backstop, not permission to create one elsewhere.
  Passing a secret as a command *argument* also loses: Windows PowerShell 5.1 appends every command
  line to `ConsoleHost_history.txt` indefinitely, where `git grep` will never find it. Use
  `--file`, or assign from `az keyvault secret show` into a variable.

- **Never treat the startup migration as reversible.** `Program.cs` calls
  `Database.MigrateAsync()` — not the synchronous `Database.Migrate()` a search may expect — on the
  **boot path**, behind a pending-migrations check that only skips the call and does nothing to make
  it safe. A migration that throws means the container does not serve at all — on a tier with no
  deployment slots. The rollback path is redeploying the retained previous archive, and **that does
  not reverse schema**. Migrations here are forward-only; no down migration is authored or relied
  on, and the `Down()` method EF generates is not a rollback story. Before any migration reaches
  the app's database, apply it to `sqldb-tenexcards-dev` first and keep the previous archive.
- **Never dismiss an `az deployment group what-if` deletion as noise — and never trust a clean
  one either.** Snapshot, deploy, then diff, *regardless of how the prediction looks*. On
  `Microsoft.Web/*` it is unreliable in **both** directions, so a boring what-if is not evidence
  that nothing will change — only the post-deploy diff is. Which predictions are phantoms and
  which was real: `### what-if: telling a real deletion from a phantom`.

These are recorded decisions, not omissions. The product rules trace to `## Non-Goals` and
`## Non-Functional Requirements` in `context/foundation/prd.md`; the deployment rules to
the risk register in `context/foundation/infrastructure.md`.

## Deployment

Live at `https://tenexcards-ka.azurewebsites.net` — resource group `rg-tenexcards-plc`, App
Service plan `asp-tenexcards-linux` (B1 Linux), **region `polandcentral`**. `infra/main.bicep`
is the source of truth for the infrastructure; what was actually run is in
`context/deployment/deploy-plan.md`.

The region is not the one `deploy-plan.md` opens with. West Europe is closed to new customers
and North Europe has zero B1 quota **on this Free Trial subscription** — both re-verified, both
requestable on a paid plan. Read them as constraints of this subscription, not as facts about
Azure. Note that `az appservice list-locations` named both regions anyway: it reports where a
SKU exists, not where you may deploy it, so a region is only proven by attempting a provision.

### A push to `main` deploys

`F-03` (`deploy-pipeline`) landed this: **`.github/workflows/deploy.yml` deploys every push to
`main`**, plus a bare `workflow_dispatch` with no inputs. **Do not hand-build and hand-deploy a
commit that is going to `main`** — push it and let the pipeline run. The manual `az webapp deploy`
in `## Never do these` is still the correct shape of that command and still what an out-of-band
deploy uses, but it is no longer the normal path. Concurrency is `deploy-production` with
`cancel-in-progress: false`: two merges queue rather than deploy over each other, because
cancelling a deploy mid-flight is worse than waiting behind one.

The workflow is a **thin caller** — every assertion it makes lives in a committed script a human
runs identically, `scripts/pack.py` then `scripts/verify_deploy.py`, the same two named in
`## Never do these`. CI therefore cannot drift from the documented rules without those scripts
changing. Three settings there are deliberate and must not be "fixed":

- **`dotnet publish` passes no `-o`.** `pack.py`'s default *is* the stock output path, so CI and a
  local run pack the identical directory. Adding `-o` silently decouples them.
- **`--track-status false`** on the deploy step. The flag was measured hanging on `Pending` while
  the site was already live; in CI that burns the job timeout and reports nothing.
  `verify_deploy.py` is the stronger signal, and `timeout-minutes` is the backstop.
- **`permissions` is exactly `id-token: write` + `contents: read`.** `actions: read` is absent on
  purpose — see the out-of-scope list below.

**Auth is OIDC; there is no stored Azure credential.** Only three non-confidential identifiers
sit in GitHub secrets (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`), and the
federated credential is **exact-match on subject**, scoped to this repository on
`refs/heads/main` — a run on any other ref cannot authenticate even holding all three.

**Never hand-type a federated-credential subject; derive it.** This repository has
`use_immutable_subject: true`, so GitHub presents
`repo:<owner>@<owner_id>/<repo>@<repo_id>:ref:refs/heads/main` — **not** the name-based form its own
documentation shows. A credential built from the documented form matches nothing on any ref and
fails only at the first workflow run, as `AADSTS700213`. Measured 2026-09-10: it cost a phase.
Build the subject from the value GitHub reports:
`gh api repos/<owner>/<repo>/actions/oidc/customization/sub --jq .sub_claim_prefix`.

The app registration `gh-tenexcards-deploy` therefore carries **two** federated credentials.
`gh-main-immutable` is live; `gh-main` matches nothing and is kept deliberately, as the one that
would become live if `use_immutable_subject` were switched off. **Do not delete either as a
duplicate** — see the 2026-09-10/11 record in `../context/deployment/deploy-plan.md`.

**OIDC removed the stored credential; it did not narrow who can deploy.** Anyone who can push to
`main` can cause Azure to mint a `Contributor` token for the resource group. Branch protection and
a narrower role are the controls for that, and neither is in place — do not read "we use OIDC" as
meaning the deployment trust boundary is settled.

**Never run `az webapp deployment list-publishing-profiles`.** It is `deny`-listed in
`.claude/settings.json`: read-only against Azure, but it prints a live credential to stdout and an
agent's stdout is logged. A publish-profile secret was the planned fallback if app registration
were refused; it was not needed and is not in use.

Deliberately **out of scope** for the pipeline, so each is still human work:

- **Infrastructure.** `infra/main.bicep` is never deployed and `what-if` never run from CI, because
  every predicted deletion needs the human judgment the last bullet of `## Never do these`
  describes.
- **App settings.** The workflow never runs `az webapp config appsettings set` and never introduces
  an `appSettings` block. Secrets are data-plane and stay in Key Vault.
- **Redeploying a prior archive.** Every deployed archive is retained 90 days (and because only a
  passing `pack.py` produces one, anything in that store is known shape-valid), but restoring one is
  a manual procedure. Automating it would need a `run_id` input, `actions: read`, and a rollback
  drill that mutates production.

### Persistence: two databases, one server, and what no template recreates

**Persistence is decided and live** as of 2026-09-10 (`F-02`, change `persistence-spine`): **EF Core
10.0.12 against Azure SQL**, S0 provisioned, in `polandcentral` beside the app. Two databases on one
logical server — below is which is which and why that split is load-bearing rather than tidy. Do not
re-open the provider question; do ask before adding a *second* store. The rule that picked it is in
`context/foundation/infrastructure.md` under `## Budget Posture`, which is its only home.

Provisioned by `F-02` on 2026-09-10 and extended by `S-01` on 2026-09-12, all declared in
`infra/main.bicep`:

| Resource | Name | Notes |
| --- | --- | --- |
| SQL logical server | `sql-tenexcards-plc` | `polandcentral`, TLS 1.2 floor, public networking |
| App database | `sqldb-tenexcards` | **S0 provisioned** — not auto-pausing, deliberately |
| Development database | `sqldb-tenexcards-dev` | Basic; never on a user path |
| Key Vault | `kv-tenexcards-plc` | RBAC, purge protection **off**, 7-day retention |
| Vault key | `dataprotection-key` | RSA; wraps the Data Protection ring (`S-01`) |

The site's managed identity holds **two** vault-scoped role assignments, both declared in the
template, and they are not duplicates: `Key Vault Secrets User` for the connection-string secret
(`F-02`) and `Key Vault Crypto User` for the key above (`S-01`). Secrets and keys are separate
data-plane surfaces with separate roles — a secrets role confers nothing on a key.

**Local development points at `sqldb-tenexcards-dev`, never at the app's database.** This is not a
convention — it is the reason two databases exist. `Database.MigrateAsync()` runs on the boot path, so a
local `dotnet run` applies whatever migrations sit in your working tree to whatever database the
connection string names, forward-only. And the Data Protection key table has **no per-application
partition**: point local development at the app's database and your laptop holds the ring that signs
production tokens. Same server, so the SQL dialect is identical and a migration verified locally
applies unchanged.

The boundary is the **contained database user** `tenexdev` — its own password, no server-level
login, `db_owner` inside the dev database only. A contained user authenticates against the database
named in its own connection string and *cannot* address another on the same server; verified by
being refused by `sqldb-tenexcards`. That is what makes the split real, because the two connection
strings otherwise differ by `Initial Catalog` alone.

**Three data-plane objects no template recreates.** After any teardown these are gone and must be
rebuilt by hand — `az deployment group create` will not do it:

1. The four vault secrets — `sql-admin-password`, `sql-connection-string`,
   `sql-dev-user-password`, `sql-dev-connection-string`. The two `-dev` ones are the **only** copy
   of the contained user's credential; it has no server login, so it cannot be recovered, only
   dropped and recreated.
2. The contained `tenexdev` user (T-SQL, in the dev database).
3. The development-machine firewall rule (`dev-machine-krzychu`). **Re-add it when your home IP
   changes** — set with `az sql server firewall-rule create`, deliberately not in the template
   because it is a property of where you are sitting.

**How the Data Protection key identifier reaches the app, and why it is not a Key Vault reference.**
The template owns the **key**; the app setting `DataProtection__KeyIdentifier` is only the **pointer**
the app reads it through, and app settings are the sole channel for that (`appSettings` stays out of
the template — see `## Never do these`). Its value is the versionless key URI, and it must be taken
from the template's own `dataProtectionKeyUri` output rather than read off a console:
`az deployment group show -g rg-tenexcards-plc -n <deployment> --query properties.outputs`. Sourcing
it anywhere else puts the key's name in two places with nothing linking them, and a rename would
then surface at the first rendered form rather than at boot. Unlike the connection string this is a
plain pointer, not a `@Microsoft.KeyVault(...)` reference, so it may be passed inline — but never in
`appsettings*.json`. CI never sets it, so it must exist **before** a build that reads it is merged;
the container otherwise does not serve at all.

**`az keyvault key show` is not the way to check that the key exists** — it is a *data-plane* read, and
the operator account holds `Key Vault Secrets Officer`, which confers nothing on a key. It answers
`(Forbidden)`, indistinguishable from a missing key. Use the ARM control-plane read instead:
`az rest --method get --url ".../Microsoft.KeyVault/vaults/kv-tenexcards-plc/keys/dataprotection-key?api-version=2024-11-01"`.

**Checking that the Key Vault reference resolves.** The app setting
`ConnectionStrings__DefaultConnection` holds a versionless reference (note the **trailing slash**
after the secret name — omitting it changes the meaning rather than erroring). A malformed reference
fails *silently*: App Service stores the literal string and nothing logs a failure. This endpoint
returns a **collection**, so `az resource show` cannot address it and answers `Not Found`, which is
indistinguishable from a genuine failure. The command that works:

```bash
az rest --method get --url "https://management.azure.com/subscriptions/<sub>/resourceGroups/rg-tenexcards-plc/providers/Microsoft.Web/sites/tenexcards-ka/config/configreferences/appsettings?api-version=2023-12-01"
```

Expect `"status": "Resolved"`. App Service resolves references at app **start** and caches the
outcome, so a reference set before its role assignment propagated stays unresolved until you
restart — treat the first non-`Resolved` reading as expected, restart, and read again.

**`az role assignment` is broken on this subscription.** Every command in that group — including
`az role definition list` — returns `(MissingSubscription) The request did not have a subscription
or a valid tenant level resource provider`. ARM is fine; use `az rest` against
`providers/Microsoft.Authorization/roleAssignments`. Do not read this as a permissions problem.

### Scaling past one worker

**ARR session affinity** (`clientAffinityEnabled`, on by default and declared in
`infra/main.bicep`) is **cookie-based**. A client that blocks cookies gets routed at random, and
a Blazor circuit's state lives in memory on exactly one instance. This cannot manifest at one
worker, so it surfaces first under load. Test affinity with cookies disabled before trusting it.

**Data Protection keys are not a scaling concern and never were** — an ephemeral ring broke
antiforgery at **one** instance, on every restart. `F-02` closed persistence on 2026-09-10 and
`S-01` closed encryption at rest on 2026-09-12; see `## Never do these` for both rules and how each
is actually verified. What remains genuinely scaling-shaped is ARR affinity above, not the keys.

### HTTPS: what enforces it, and what must never be set

The Linux .NET container supplies `X-Forwarded-Proto` and forwarded-header processing is **on by
platform default**, so `Request.IsHttps` is already correct — which is why *disabling* it is what
breaks things rather than a no-op. `UseHsts()` emits its header only when `Request.IsHttps`, so
turning it off just stops HSTS. Setting `ASPNETCORE_FORWARDEDHEADERS_ENABLED=false` and adding
`ASPNETCORE_HTTPS_PORT` together produced a `307` to the request's own URL — an infinite redirect —
back when `UseHttpsRedirection()` was still in the pipeline; the port alone is harmless, and
harmless today only because that middleware is gone. Measurements are in
`context/deployment/deploy-plan.md` under `## Deliberately not set`.

**`UseHttpsRedirection()` was removed on 2026-09-08 and is not coming back.** The platform is now
the sole enforcement point, and that is safe because the `site` resource in `infra/main.bicep`
declares `httpsOnly: true` — the enforcement lives in the infrastructure source of truth, not in a CLI flag
someone once typed by hand. Anyone deploying this app somewhere else owns that guarantee: reinstate
redirection in the application only if the new environment cannot make it at the platform, and
record the decision here.

The startup line `HttpsRedirectionMiddleware[3] Failed to determine the https port for redirect`
came from App Service's internal plain-HTTP warm-up probe hitting that middleware. **It no longer
appears** — confirmed absent from the 2026-09-08 startup log. Kept here because it is documented as
a non-defect elsewhere: if you see it again, the middleware is back.

**`UseHsts()` is present, deliberately, and is new production behaviour** compared with the
scaffold. It is not drift. HSTS and `httpsOnly` cover different moments — `httpsOnly` redirects at
the edge *after* a plain-HTTP request has been made, while HSTS tells the browser not to make that
request next time. The default options exclude `localhost`, so local development is unaffected, and
the header scopes to the exact host without `includeSubDomains`. One practical consequence: the
default 30-day `max-age` means plain HTTP cannot be tested against a hostname for 30 days after its
first response. Unverified either way, and the decision does not depend on it: whether
`azurewebsites.net` is already covered by browser HSTS preloading, which would make the header a
no-op on *this* host while still mattering on a custom domain.

### what-if: telling a real deletion from a phantom

The output carries a "may contain false positive predictions" banner, and on `Microsoft.Web/*`
it earns it. `siteConfig.localMySqlEnabled` and `siteConfig.netFrameworkVersion` are permanent
phantoms that change nothing. But on 2026-08-31 a predicted
`- properties.freeOfferExpirationTime` on the plan was **real**: a deployment reporting
`Succeeded` cleared it, and no `az` command restores it.

It also **under-reports**: on 2026-09-08 it predicted only those two phantoms on the site and never
mentioned the `identity: SystemAssigned` block that was the deployment's one intended change and
did land.

Nothing in the output separates the two. Snapshot (`az appservice plan show`,
`az webapp config show`, `az webapp config appsettings list`), deploy, then diff.
`infra/main.bicep` carries a dated characterisation above each resource.

## Testing

**`TenExCards.Tests` exists** (xUnit, landed by `S-01` on 2026-09-12) and **gates the deploy** — the
`Test` step in `.github/workflows/deploy.yml` runs before `Publish`, so a failing test means nothing
ships. That was verified by deliberately failing one and reading the run's *step list*: every step
after `Test` showed skipped, not merely a red run.

**The rules for writing tests live next to the tests, in `TenExCards.Tests/AGENTS.md`.** Read that
file before adding one. Only the FluentAssertions prohibition stays here, in `## Never do these`,
because it is a licensing rule rather than a testing rule.

Ship the test in the same change as the code it asserts.

## Conventions

Card quality is the product, and it is specified rather than delegated: read
`## Business Logic` in `context/foundation/prd.md` before touching generation or triage.
A candidate must test one load-bearing claim, be reformulated rather than copied, admit one
defensible answer, and not duplicate another card in the set.

Product commits use **Conventional Commits**, subject `<type>(<change-id>): <phase title> (p<N>)`
— for example `feat(blazor-server-shell): Blazor Server host replaces the API scaffold (p1)`. The
scope is the change-id from `context/changes/<change-id>/`, and `(p<N>)` is the plan phase, so a
commit traces back to the plan step that authorised it. Types in use: `feat`, `fix`, `chore`,
`refactor`, `docs`. The body says *why*, then lists the touched files.

Commits predating 2026-09-08 track course milestones (`Completed M1L3`) instead. Do not extend that
style, and do not rewrite them.

Do **not** add `Co-Authored-By` trailers.
