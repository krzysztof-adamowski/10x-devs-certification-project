---
change_id: accounts-and-sessions
title: Accounts and sessions
status: implementing
created: 2026-09-12
updated: 2026-09-12
archived_at: null
---

## Notes

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->

### Phase 6 finding: the scaffold section could not simply be deleted, and two of its facts had no home

The plan's contract reads "remove the scaffold-state parts." Executing that literally would have
dropped three things that are not scaffold state and that the section's own warning named only two
of:

1. **The persistence decision** — moved to `### Persistence: two databases, one server, and what no
   template recreates`, as the warning directs.
2. **The budget rule** — moved to `## Budget Posture` in `context/foundation/infrastructure.md`,
   likewise. Recorded there explicitly as *the rule's only home*, because `CLAUDE.md` deliberately
   does **not** import `infrastructure.md`, so an agent reading only `AGENTS.md` will no longer find
   the rule. `AGENTS.md` now points at it from the persistence section rather than restating it.
3. **The per-page interactivity fact** — not named by the warning, and not scaffold state either.
   It is why the Identity pages carry no `@rendermode` (a circuit cannot write a cookie to the
   response), so deleting it would have removed the reason for a live design decision. Kept, with
   the retired `CircuitCheck.razor` worked-example sentence dropped from around it.

The section was retitled `## What is wired, and what is not` rather than deleted outright: the LLM
client is still genuinely missing, and the missing-pieces list was the only record of that.

### Phase 6 finding: two verified traps had no durable home outside this change record

Both were discovered during implementation and would otherwise have survived only in `change.md`,
which no future slice reads. Each is now a rule in `TenExCards/AGENTS.md`:

- **`MapStaticAssets()` needs `.AllowAnonymous()` under a fallback policy**, and
  `scripts/verify_deploy.py` structurally cannot catch its absence — it follows redirects and asserts
  status, so a gated stylesheet resolves `302 → /Account/Login → 200` and records a pass. Now
  `### Authorization defaults to protected`, together with why
  `AddInteractiveServerRenderMode()`'s builder was deliberately **not** given the same treatment.
- **Testing for `<value>` misreads a correctly encrypted key row.** The Data Protection bullet in
  `## Never do these` now states the discriminating check (absence of `<masterKey>` and of the
  plaintext writer's `Warning: the key below is in an unencrypted form.` comment) rather than
  leaving the obvious wrong one available.

### Phase 6 finding: one runnable command was corrected beyond the plan's contract

The plan names one command Phase 5 broke — `verify_deploy.py --base-url .../db-check` in the `F-02`
record — and says everything else in `deploy-plan.md` stays, because dated measurements must not be
edited to make a search come up clean.

A second broken command was found and corrected on the same reasoning **inverted**: the `## Rollback`
section's second choice still read `dotnet publish TenExCards/TenExCards.csproj`, which Phase 4's
solution-folder restructure invalidated. That is not a dated measurement — it is a live procedure
someone runs when production is already broken, so leaving it stale would fail on the day it matters
most. Corrected with a dated note saying so. The distinction that decided both: **a record of what
was run stays; an instruction for what to run gets fixed.** The `/db-check` block was kept unedited
under a superseded marker because it is the former; the rollback line is the latter.

### Phase 5 finding: the inherited tests named the retired routes, and removing the pages required fixing the auth-boundary assumption baked into them

`AuthBoundaryTests.cs` (landed in Phase 4) used `/circuit-check` as its worked example of "a gated
route" in two tests: an unauthenticated redirect check, and the round-trip test's "reach a gated
route while signed in" step, which asserted `200 OK`. Both assumptions broke once `CircuitCheck.razor`
was deleted, and the break was not the one it looked like.

**Verified empirically, per the standing rule not to assume the framework's behavior at a route
boundary.** A throwaway `WebApplicationFactory` probe (deleted after use) against a path matching no
page at all showed: unauthenticated, the global fallback authorization policy redirects to
`/Account/Login` before Blazor's own router ever runs — identical to a real gated page, since the
policy is enforced at the endpoint-routing layer, ahead of component matching. Authenticated, the
same unmatched path clears the fallback policy and resolves through the router's own `NotFound`
handling, returning `404` with the not-found page's content. This is also the mechanism behind
criterion 5.8 ("requests for the two retired routes return the not-found page"): that behavior only
holds for an authenticated request, since an unauthenticated one is redirected to login first and
never reaches the router at all.

Fixed by replacing the two `/circuit-check` literals with a private `UnmatchedRoute` constant holding
a path that matches no page, and changing the "reach a gated route while signed in" assertion from
`OK` to `NotFound`. The literal-string replacement was necessary independent of the test fix: Phase
5's own criterion 5.1 searches `TenExCards/` (excluding `Migrations/`) for the retired names, and the
inherited tests would have failed that search on their own once the pages were gone. A stale
`DbCheck.razor` reference in a `Register.razor` comment was corrected for the same reason.

### Phase 5 finding: a `BadImageFormatException` during route-table warm-up, not a regression

The deploy's startup log shows the migration succeeding (`Applying 1 pending migration(s):
20260912135900_DropSpineProbes` → `Migrations applied successfully`) followed, a few seconds before
`Application started`, by a `BadImageFormatException` thrown inside ASP.NET Core's lazy endpoint
route-table initialization (`RouteEndpointDataSource.CreateRouteEndpointBuilder` →
`TypeHelper.IsCompilerGeneratedType`), surfacing as a confusingly-worded `InvalidOperationException`
about a mangled type name.

**Checked against the full log archive before treating it as a regression**: the identical exception
appears in the 2026-09-10 startup log too — two days before this phase existed — and both boots are
recorded by App Service's own health check as `success`. It is a pre-existing platform artifact of
this container image's first-request route-table construction, unrelated to anything Phase 5 changed.
The live site was confirmed healthy after this boot (root page, retired-route redirects, and the
`build 9fccf42` marker all correct).

### Phase 4 finding: `TenExCards/` restructured into a solution folder, on request

The plan's Phase 4 contract reads "`TenExCards.sln` at the repository root." Mid-phase, on explicit
instruction, the layout changed instead to `TenExCards/` as a **solution** folder — `TenExCards.slnx`
(XML format, also a deviation from the literal `.sln` the contract names) plus two project
subfolders one level deeper: `TenExCards/TenExCards/` (product code, moved via `git mv`) and
`TenExCards/TenExCards.Tests/` (the new test project). `AGENTS.md`, `CLAUDE.md` and `.gitignore`
stay at the outer `TenExCards/` level — verified necessary, not just convenient, since `CLAUDE.md`'s
own `@AGENTS.md` and `@../context/foundation/prd.md` imports only resolve from that location.

Every path this broke was updated: `deploy.yml`'s publish/artifact/deploy paths and its new `Test`
step, `scripts/pack.py`'s default publish and archive paths, `scripts/README.md`'s examples, and
`AGENTS.md`'s own `*Paths.*` convention paragraph (made explicit about the double
`TenExCards/TenExCards/` rather than leaning on "relative to this file's own directory," which now
hides an extra hop). `context/deployment/deploy-plan.md` and `context/foundation/roadmap.md` were
deliberately left alone — their `TenExCards/…` mentions are dated measurements of a past state, not
present-tense claims a search should find clean. Verified end to end from the new layout: solution
build, `dotnet-ef`, `dotnet user-secrets`, `dotnet publish`, `pack.py`'s four assertions, `dotnet
test`, and a full CI deploy.

### Phase 4 finding: swapping `AppDbContext` to the EF in-memory provider needs more than `RemoveAll<DbContextOptions<T>>`

`AddDbContextFactory<AppDbContext>` — called once in `Program.cs` for SqlServer, once in the test
factory for InMemory — does not only register `DbContextOptions<AppDbContext>`; it also registers
the options-configuring delegate itself as its own DI entry (an `IDbContextOptionsConfiguration
<AppDbContext>`-shaped registration), which `DbContextOptionsFactory<TContext>` applies **additively**
to every entry found, not last-registration-wins. Removing only `DbContextOptions<AppDbContext>` and
`IDbContextFactory<AppDbContext>` before re-registering left that first entry in place, so the built
options carried both `UseSqlServer` and `UseInMemoryDatabase` — failing at first use with "Services
for database providers ... have been registered in the service provider." Fixed by removing every DI
descriptor whose service type is, or is generic over, `AppDbContext` (filtering by
`ServiceType.GetGenericArguments().Contains(typeof(AppDbContext))`), which catches that entry without
needing to name its internal interface type.

### Phase 4 finding: `WebApplicationFactory` defaults to Development on its own — measured, not assumed

The plan calls for verifying this rather than assuming it, since it decides whether
`TenExCardsWebApplicationFactory`'s explicit `UseEnvironment(Environments.Development)` is
belt-and-braces or load-bearing (the only thing stopping a unit test from reaching Key Vault).
Measured via a bare `WebApplicationFactory<Program>` with no environment override: resolving
`IHostEnvironment` reports `Development`. It is belt-and-braces — kept anyway, and asserted as its
own test (`IdentityConfigurationTests.WebApplicationFactory_NoEnvironmentOverride_DefaultsToDevelopment`)
so a future framework change to that default fails loudly rather than silently starting to reach Key
Vault from every test run.

### Phase 4 finding: the negative-test proof (criterion 4.10), read from the step list rather than its colour

A throwaway `_DeliberatelyFailingTest.cs` (one `Assert.Fail`) was committed and pushed on its own.
The resulting run (`34697369523`) shows exactly the shape the criterion asks for: `Test` failed, and
every step after it — `Publish`, `Pack and verify archive shape`, `Retain the archive`, `Azure login
(OIDC)`, `Deploy`, `Verify the deployed site` — shows skipped (`-`) in the step list, not merely a
red run overall. Reverted in the next commit (`dbd121e`); CI re-ran green.

### Phase 4 finding: no real network reachable (criterion 4.11), observed rather than argued

Rather than resting on the architecture (EF in-memory provider, Key Vault guard skipped in
Development, boot-path migration skipped), the test run was monitored directly: `Get-NetTCPConnection`
polled every ~1.5s against both the outer `dotnet test` CLI process and the `testhost.exe` process
actually executing the suite. The outer CLI process opened three `Established` connections to
`20.50.88.242:443` — a Microsoft/Azure IP consistent with .NET SDK CLI telemetry (`DOTNET_CLI_
TELEMETRY_OPTOUT` was unset), a background behaviour of the `dotnet` command itself and unrelated to
the code under test. `testhost.exe` — where `AppDbContext`, `UserManager` and `SignInManager` actually
ran — showed zero `Established` entries at any point, only listening sockets. That absence, not an
assumption about what the in-memory provider "should" do, is the evidence for this criterion.

### Phase 3 finding: the fallback policy gates `MapStaticAssets()` but not the render-mode endpoint

The plan warned that `MapStaticAssets()` and `AddInteractiveServerRenderMode()` both map endpoints
carrying no authorization metadata, so the global fallback policy (`RequireAuthenticatedUser`)
could 302 every stylesheet and script to the login path — and that `verify_deploy.py` cannot catch
this, since it follows redirects and asserts status only.

**Verified empirically, per the plan's own instruction not to discover this in production.**
Running locally against `sqldb-tenexcards-dev` before committing: with only
`app.MapStaticAssets().AllowAnonymous()` added, every asset on `/` (bootstrap.css, app.css,
`TenExCards.styles.css`, `ReconnectModal.razor.js`, `blazor.web.js`) returned `200` with its own
content type while signed out, and a genuinely gated route (`/circuit-check`, which carries no
`[AllowAnonymous]`) still correctly redirected `302` to `/Account/Login`.

**`AddInteractiveServerRenderMode()`'s own builder was deliberately left un-anonymous.** Calling
`.AllowAnonymous()` on it was considered and rejected: `MapRazorComponents<App>()` returns one
`IEndpointConventionBuilder` covering every page route, and per-page `[Authorize]`/`[AllowAnonymous]`
metadata composes with (does not override) a convention applied at that builder level — an
`IAllowAnonymous` marker anywhere in an endpoint's metadata short-circuits authorization for that
endpoint. Marking the whole builder anonymous risked exempting every future gated page, not just the
interactive-circuit hub. It was not needed regardless: no anonymous page in this slice uses
`@rendermode InteractiveServer` (the only interactive page, `CircuitCheck.razor`, has no
`[AllowAnonymous]` and is now itself gated), so `blazor.web.js` never attempts to negotiate a circuit
for an anonymous visitor. Revisit this the first time an anonymous page needs interactivity.

### Phase 3 finding: sign-out is an endpoint, not the third page the plan named

The plan's `#### 5. Register, Login and Logout pages` asks for "a logout confirmation surface"
alongside the two real pages. It was implemented instead as a POST endpoint —
`MapIdentityLogout()` in `Components/Account/IdentityEndpoints.cs`, a trimmed equivalent of the
template's `MapAdditionalIdentityEndpoints` carrying only that route — and the reason is the same
one that decided the render mode for the whole slice: **clearing the cookie is a response-header
operation**, so a component action cannot perform it, and a `GET` sign-out would be trivially
triggerable cross-site. A confirmation *page* would have bought nothing, since the POST it submits
is the part that must exist.

Two consequences worth carrying: it is mapped **after** `MapRazorComponents<App>()`, per the
template's own convention for account endpoints, and it needs an explicit `.AllowAnonymous()` —
posting logout twice, or after the cookie has already expired, must land on the home page rather
than `302` to login.

Recorded because the plan's "three pages" phrasing propagated into `TenExCards/AGENTS.md` and
`roadmap.md` before anyone compared it against the code; both were corrected on 2026-09-12. Read
the plan's section 5 heading as naming three *surfaces*, only two of which are pages.

### Phase 3 finding: lockout triggers on the 5th failed attempt, not the 6th

The plan's manual step (3.13) describes "six consecutive failed sign-ins." Tested against a real
registered account with `lockoutOnFailure: true` and no `Lockout` options overridden: the account
locked out on the **5th** failed attempt (Identity's default `MaxFailedAccessAttempts = 5`); the 6th
attempt also reported locked-out. This is the framework default, left untouched deliberately — the
same "inherited, not configured" reasoning Phase 3's `## Changes Required` applies to password
storage. Read the plan's "six" as an approximate description, not a value this slice sets.

### Phase 3 finding: restart-survival, verified against the deployed site with a throwaway account

Following the "verify a restart from the log, never from the first 200" lesson: registered
`deploy-verify@example.com` against `https://tenexcards-ka.azurewebsites.net`, confirmed
`Set-Cookie: .AspNetCore.Identity.Application=...; secure; samesite=lax; httponly`, issued
`az webapp restart` at `12:33:33Z`, then polled the startup log rather than the site's first `200`.
A fresh `Application started` line appeared at `12:34:54Z` — about 81 seconds later, consistent with
Phase 1's ~2-minute finding for the same platform behavior. Reused the pre-restart cookie
afterward: `/` still rendered "Signed in as deploy-verify@example.com" and the gated
`/circuit-check` route returned `200` rather than redirecting to login — the auth-cookie form of the
key-ring check, and the strongest evidence this slice can produce that Phase 1's encryption did not
break persistence. The test account was deleted from `sqldb-tenexcards` afterward
(`DELETE FROM AspNetUsers WHERE Email = 'deploy-verify@example.com'`) to leave the production
database clean.

### Phase 3 finding: an in-flight background poll's timestamp check could never succeed

A first attempt at the restart-survival check above used a backgrounded shell loop with an `awk`
range-match against the exact restart timestamp string to decide when a fresh startup line had
appeared. Because no log line's sub-second timestamp exactly matched the literal comparison string,
the range never opened and the loop would have polled forever without ever reporting success or
failure — silent, not merely slow. Caught by inspecting the task's raw output file directly (zero
lines after several minutes, on a check that Phase 1 measured taking about two minutes) rather than
trusting the wakeup schedule alone. Replaced with a single direct check comparing the actual log
timestamps by eye. Any future restart-survival automation should compare epoch seconds, not
substring-match a timestamp literal.

### Phase 3 finding: code-behind convention adopted mid-phase, scoped to files this phase touches

Partway through Phase 3, all `.razor` files carrying a `@code` block were split into markup-only
`.razor` plus a `.razor.cs` partial class, on request. Scope was deliberately limited to files this
phase created or touched (`Register`, `Login`, `StatusMessage`, `RedirectToLogin`, `Error`,
`MainLayout`) rather than the whole project — `DbCheck.razor` and `CircuitCheck.razor` are untouched
and both retire in Phase 5 regardless. One gotcha: `@inherits` must stay in the `.razor` file rather
than being restated as a base class on the `.cs` partial — the Razor-generated partial and the
hand-written partial disagreeing on the base class is a compile error (`CS0263`), not a merge.

### Phase 1 finding: the operator cannot run the plan's own key-verification command

Criterion 1.3 is written as `az keyvault key show`. That command returns `(Forbidden) Caller is not
authorized to perform action on resource` for the operator account
(`krz.adamowski_gmail.com#EXT#@krzadamowskigmail.onmicrosoft.com`).

The cause is not a missing key and not a broken deployment. Vault RBAC holds four assignments, and
the operator's three — Owner, Contributor and **Key Vault Secrets Officer** — are all either control
plane or *secrets* data plane. **Secrets and keys are separate data-plane surfaces with separate
roles**, so Secrets Officer confers nothing on a key. This is the same shape as the `az role
assignment` breakage already recorded in `AGENTS.md`: a command that fails for a reason unrelated to
the thing being checked, whose output is indistinguishable from the failure it appears to report.

**Resolved by substituting the ARM control-plane read**, which the operator's Contributor role
already covers:

```bash
az rest --method get --url "https://management.azure.com/subscriptions/<sub>/resourceGroups/rg-tenexcards-plc/providers/Microsoft.KeyVault/vaults/kv-tenexcards-plc/keys/dataprotection-key?api-version=2024-11-01"
```

Proven before it was trusted, per `lessons.md`: run against the keys *collection* before the
deployment, it returned `{"value": []}` — a 200 with an empty list, establishing that the command
works and that the vault held zero keys. An empty result was therefore readable as a verdict rather
than as a broken command. Granting the operator `Key Vault Crypto Officer` was considered and
declined: it would add an imperative operator role assignment outside the template to buy nothing
the ARM read does not already provide.

### Phase 1 finding: what-if reconciliation, 2026-09-12

Deployment `s01-keyring-20260912-111827`, `provisioningState: Succeeded`. Snapshots taken before and
after with `az appservice plan show`, `az webapp config show`, `az webapp config appsettings list`,
`az webapp show` and the `az rest` roleAssignments GET, then diffed — per `lessons.md`, regardless of
how the prediction looked.

| Predicted line | Observed | Verdict |
| --- | --- | --- |
| `CREATE keys/dataprotection-key` | Key exists, `enabled: true`, RSA 2048, `keyOps: [wrapKey, unwrapKey]` | **Real — intended** |
| `CREATE roleAssignments/22ceb7ae-9e57-5d50-9bc6-96b673955633` | Assignment exists, Key Vault Crypto User, site principal, vault scope | **Real — intended** |
| `MODIFY roleAssignments/4d35348c…` `properties.principalId` | Unchanged — still `a3558bdf-…` | Phantom, as the template documents |
| `NoEffect properties.principalType` | Unchanged | Phantom, self-labelled |
| `MODIFY sqldb-tenexcards` `sku.name 'Standard' -> 'S0'` | Unchanged — `Standard` / `Standard` / `Online` | Phantom, as the template documents |
| `NoEffect sku.tier` (both databases) | Unchanged | Phantom, self-labelled |
| `NoEffect properties.version` (sql server) | Unchanged | Phantom, as the template documents |
| `IGNORE databases/master` | Not declared, not touched | Expected |
| `MODIFY sites/tenexcards-ka` `siteConfig.localMySqlEnabled` | Unchanged | Phantom — **fourth** confirmation |
| `MODIFY sites/tenexcards-ka` `siteConfig.netFrameworkVersion` | Unchanged | Phantom — **fourth** confirmation |

**Nothing was predicted as `- Delete`, and nothing was deleted.** The `appSettings` snapshot is
byte-identical across the deploy, so `ConnectionStrings__DefaultConnection` survived — the hazard the
template's `appSettings` omission exists to prevent.

**`properties.freeOfferExpirationTime` was already `null` before this deployment.** It was cleared on
2026-08-31 and there was nothing left to lose, so this run cannot be read as evidence that the
destructive behaviour recorded against it has stopped. The `plan` snapshot is byte-identical.

The only difference across the entire post-deploy diff was `site.lastModifiedTimeUtc`
(`2026-09-11T13:46:13` → `2026-09-12T09:19:10`) — a timestamp, not a property.

**what-if under-reported nothing this time**, unlike the 2026-09-10 run that omitted the `identity`
block. Both intended creates were predicted. Recorded because it is the first clean prediction on
this template, not because it makes the next one trustworthy.

### Phase 1 finding: the inherited restart-survival guarantee, baselined before any change

This is the check `AGENTS.md` records as owed by `S-01`, run against the **plaintext** ring while the
deployed build was still the non-encrypting one, so that a failure after the change is attributable
to the change. Executed with `curl` rather than a browser, which is what made the middle step
provable.

| Step | Observation |
| --- | --- |
| Render `/db-check` | `200`; `__RequestVerificationToken` 155 chars, `_handler=write-probe`, cookie `.AspNetCore.Antiforgery.RtGCWVXC8-4` captured; **7** probe rows |
| Restart | `az webapp restart`, then a **fresh** `Application started` at `2026-09-12T09:33:07Z` in the container log |
| Submit the pre-restart form | **`200`**, not `400`; no antiforgery rejection text; probe rows **7 → 8** |

**The restart was verified from the log, not from the site answering.** The first poll after issuing
the restart returned `200` within a second — the old container still serving. Submitting then would
have proved nothing at all, since no key ring would have been reloaded. The check only becomes
meaningful once a new `Application started` line exists, and that took roughly two minutes to appear.
Anyone repeating this must wait for that line rather than for a `200`.

**The row count is the assertion, not the status code.** A `200` alone is consistent with a
re-rendered page that quietly rejected the post; `7 → 8` proves the handler actually ran. The
key-count comparison `AGENTS.md` warns about is deliberately not the verdict here — a ring with no
reason to rotate looks identical to a working one.

### Phase 1 finding: looks like a failure but is not — a 500 on the app-setting restart

Applying `DataProtection__KeyIdentifier` restarts the app, and the first request into that restart
window returned **`500` on `/db-check`** while `/` returned `200`. The log shows a genuine SQL error,
not a rendering one:

```
fail: Microsoft.EntityFrameworkCore.Database.Connection[20004]
      An error occurred using the connection to database 'sqldb-tenexcards' ...
      Microsoft.Data.SqlClient.SqlException: A connection was successfully established with the
      server, but then an error occurred during the login process.
```

**This is not the failure `main.bicep` warns about, and telling them apart matters.** That warning
says supplying a `sqlAdminPassword` differing from the vault's silently rotates the server admin
password, breaking the app at its *next restart* — which is exactly when this appeared. Three things
rule it out:

1. The documented symptom is `Login failed for user 'tenexadmin'`. This is a different error, raised
   during the login *process* rather than as a credential rejection.
2. The password was taken from the vault, so the deployment re-asserted the same value.
3. **It did not persist.** Eleven seconds later the new container connected cleanly, ran
   `SELECT COUNT(*) FROM [SpineProbes]`, reported `Database schema is current`, and read
   `DataProtectionKeys`. `/db-check` returns `200` on retry and `scripts/verify_deploy.py` passes with
   the root page and all five same-origin assets at `200`.

It was the old container being torn down mid-request. Recorded because the *timing* is a perfect
match for a credential rotation and the next operator to see it will reach for that explanation
first. Retry before diagnosing; a rotated password does not heal on the following boot.

### Phase 1 finding: `<value>` is present in an ENCRYPTED key row, and the obvious check misreads it

Criterion 1.10 reads "contains an `<encryptedKey>` element and no readable `<value>` element". The
obvious implementation — `Xml LIKE '%<value>%'` — **reports a false positive on a correctly encrypted
row**, and did here.

`<value>` appears in *both* forms. In the plaintext form it is the master key itself. In the
encrypted form it is the ciphertext payload, nested inside `<encryptedKey>`. Matching the string
alone cannot tell them apart, and reading it as a failure would have led to re-running an
irreversible deletion that had already worked.

The structural check that *does* discriminate, and its result on the new row:

| Probe | Result |
| --- | --- |
| `LIKE '%unencrypted form%'` — the comment the plaintext writer always emits | **absent** |
| `LIKE '%<masterKey>%'` — the plaintext container element | **absent** |
| `LIKE '%AzureKeyVaultXmlDecryptor%'` | present |
| `LIKE '%<encryptedSecret%'` | present |
| `CHARINDEX` of `<value>` vs `<encryptedKey>` … `</encryptedKey>` | `1435`, between `863` and `1878` — **nested inside**, so it is the payload |

The decisive probes are the first two. ASP.NET Core writes a literal
`Warning: the key below is in an unencrypted form.` comment beside a plaintext master key; its
absence, together with the absence of `<masterKey>`, is what proves the key material is not readable.
**Assert on those, not on `<value>`.**

### Phase 1 finding: the encrypted ring survives a restart, and is unwrapped rather than re-minted

Change 7, run against the encrypted ring after the plaintext row was discarded.

| Step | Observation |
| --- | --- |
| Render `/db-check` | `200`, token 155 chars, **9** probe rows |
| Restart | fresh `Application started` at `2026-09-12T09:55:39Z` |
| Submit the pre-restart form | **`200`**, no rejection, probe rows **9 → 10** |
| `DataProtectionKeys` afterwards | still **one** row, still `Id = 2` |

**The unchanged row is the strongest part of this result and is worth more than the `200`.** If the
app had been unable to unwrap the key through Key Vault, Data Protection would have minted a
replacement and the table would show a third key. It did not. So the restart did not merely leave the
ciphertext in place — the app read it back, called Key Vault to unwrap it, and recovered the same
ring. That is the property `F-02` established, now re-established through an encryption layer.

### Phase 1 finding: pre-change Data Protection key row

Recorded before anything was changed, so the after state has something to be compared against.
Read from `sqldb-tenexcards` on 2026-09-12:

| Field | Value |
| --- | --- |
| `Id` | `1` |
| `FriendlyName` | `key-f3bfba0e-5050-4374-a085-8797b359289a` |
| `LEN(Xml)` | `887` |
| Shape | **PLAINTEXT** — matches `%<value>%`, does not match `%<encryptedKey%` |

Exactly one row. This is the row change 6 discards; the row that replaces it must have a different
`Id` and must be ciphertext.

**After the change**, for comparison — one row, and not this one:

| Field | Before | After |
| --- | --- | --- |
| `Id` | `1` | **`2`** |
| `FriendlyName` | `key-f3bfba0e-5050-4374-a085-8797b359289a` | `key-73a4c584-d073-4dce-a6bf-277a82d63f70` |
| `LEN(Xml)` | `887` | `1942` |
| Key material | plaintext | **wrapped with `dataprotection-key`** |

`DELETE FROM DataProtectionKeys` reported `1` row affected and `0` remaining, the container was
restarted, and the next protect operation minted `Id = 2`. The growth from 887 to 1942 bytes is the
Key Vault encryption envelope — the `kid`, the wrapped key, the IV and the ciphertext.
