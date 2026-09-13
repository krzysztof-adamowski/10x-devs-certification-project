---
date: 2026-09-13T23:25:13+02:00
researcher: Krzysztof Adamowski
git_commit: 6a863c7f426644acf04abee0ff24a7a07f64c93d
branch: main
repository: 10x-devs-certification-project
topic: "Pipeline guarantees under test — risks #1 and #5"
tags: [research, codebase, pipeline, authorization, boot-path, deploy-gate, test-plan-phase-1]
status: complete
last_updated: 2026-09-13
last_updated_by: Krzysztof Adamowski
---

# Research: Pipeline guarantees under test — risks #1 and #5

**Date**: 2026-09-13T23:25:13+02:00
**Researcher**: Krzysztof Adamowski
**Git Commit**: `6a863c7f426644acf04abee0ff24a7a07f64c93d` (not pushed at time of writing — no GitHub permalinks, local paths only)
**Branch**: `main`
**Repository**: `10x-devs-certification-project`

## Research Question

Rollout Phase 1 of `context/foundation/test-plan.md`. Two risks, six grounding questions, taken
verbatim from that plan's §2 Risk Response Guidance — this document answers those and does not
restate the risks, the oracles or the anti-patterns, which are already stated twice (test-plan
`§2`, and `change.md` in this folder).

**Risk #1** — which guarantees are enforced by pipeline *ordering* versus by an attribute; what an
anonymous request for an asset actually returns; what the existing deploy gate does and does not
assert.

**Risk #5** — everything the boot path demands before it will serve; which test hosts supply each
and which do not; what the platform does when one is absent.

## Summary

1. **Phase 1 is not a build-out.** `TenExCardsWebApplicationFactory` already boots the real
   `Program.cs` — real authentication, real authorization fallback policy, real antiforgery, real
   endpoint graph, real `CardStore`, EF in-memory per factory instance, generator stubbed, no
   network. `Microsoft.AspNetCore.Mvc.Testing` 10.0.12 is referenced and
   `public partial class Program;` is declared with a comment forbidding its removal. Nothing is
   missing at the infrastructure level.
2. **Risk #1 is half-covered already, and the uncovered half is precisely the half status codes
   cannot see.** `AuthBoundaryTests` pins `302 → /Account/Login, never 401` for `/generate`,
   `/cards`, `/cards/new` and an unmatched route. What no test pins is the set of guarantees whose
   presence and absence produce the *same* status code.
3. **Six of sixteen guarantees are invisible to a status-code observer**, and the repository's
   defence for the most important of them is currently a `grep` in a hand-ticked plan checklist,
   not a test.
4. **The deploy gate's hole is larger than the brief states.** It is not only that a gated *asset*
   launders `302 → sign-in → 200`. The **root page itself** may redirect to sign-in and the gate
   still prints `DEPLOY VERIFIED`, because it fails only on a *different host*. The entire app
   could be gated and the gate would pass.
5. **Risk #5's sharpest fact: `DataProtection:KeyIdentifier` has no automated coverage in any
   environment.** It is Development-skipped locally, Development-skipped in CI, and live only in
   the deployed container. CI proves `Program.cs` boots *when every external demand is stubbed*.
6. **That produces the phase's one real design fork.** The existing factory forces
   `Environments.Development`, and the two production-only guarantees (`UseHsts()`, the Key Vault
   key-ring encryption guard) are *unreachable through it by construction*. Testing them needs a
   second factory shape running as Production — and that second shape is exactly the thing that
   would have caught the 2026-09-13 CI failure class before it reached CI.
7. **The repository has a 17-incident precedent set of "reported success while being wrong"**, two
   of which are already canonised in `lessons.md`. One of them is *live inside the test suite
   today* — `MigrationGuardTests` was a green-test-no-guarantee near-miss caught at plan review and
   narrowed, and it is the closest existing analogue to the pattern Phase 1 must produce.

---

## Detailed Findings

### 1. What already exists — the starting position

**The host.** [TenExCardsWebApplicationFactory.cs](TenExCards/TenExCards.Tests/TenExCardsWebApplicationFactory.cs)
subclasses `WebApplicationFactory<Program>` and injects four settings via `UseSetting`, plus an
explicit environment:

| Line | Setting | Why |
| --- | --- | --- |
| `:29` | `ConnectionStrings:DefaultConnection` = `Server=unused;Database=unused;` | satisfies the D1 guard; nothing reads it for real |
| `:32` | `Testing:SkipStartupMigration` = `true` | `GetPendingMigrationsAsync()` is relational-only and throws against in-memory |
| `:36` | `Gemini:ApiKey` = `test-key-not-a-real-credential` | satisfies the D4 guard |
| `:44` | `UseEnvironment(Environments.Development)` | **load-bearing for this phase — see §5.4** |

Only two services are swapped (`:60-73` the `AppDbContext` descriptors, `:76-77`
`ICardCandidateGenerator`). Identity, authentication, authorization, antiforgery, Data Protection,
options validation and the whole Blazor endpoint graph run for real.

**Existing coverage of the two risks**, per file:

| File | Already pins | Layer |
| --- | --- | --- |
| [AuthBoundaryTests.cs](TenExCards/TenExCards.Tests/AuthBoundaryTests.cs) | anonymous `200` on `/`, `/Error`, `/not-found`; `302 → /Account/Login` **never `401`** on `/generate`, `/cards`, `/cards/new`, unmatched route; register → sign-out → gated-access round trip | full host over HTTP |
| [MigrationGuardTests.cs](TenExCards/TenExCards.Tests/MigrationGuardTests.cs) | with `Testing:SkipStartupMigration` unset, touching `factory.Services` throws matching `*Relational-specific methods can only be used*` — i.e. the flag defaults to **running** the migration | bare factory |
| [IdentityConfigurationTests.cs](TenExCards/TenExCards.Tests/IdentityConfigurationTests.cs) `:19` | `WebApplicationFactory` defaults to Development with no override | bare factory |

Those two `AuthBoundaryTests` comments (`:88`, `:101`) already name `verify_deploy.py`'s
401-is-fatal contract, so the app-side and gate-side halves of risk #1 are already understood to be
one thing by the existing code.

### 2. Risk #1 — the guarantee inventory, classified

Sixteen guarantees live in [Program.cs](TenExCards/TenExCards/Program.cs) and its neighbours. The
classification that matters for test design is **not** "auth vs transport" but *what breaks them*
and *whether an HTTP observer can tell*.

| # | Guarantee | Enforced by | Pinned today? | Status alone distinguishes? |
| --- | --- | --- | --- | --- |
| G1 | Pages gated unless marked (`:150-152`) | metadata + order | ✅ `AuthBoundaryTests` | ✅ `302` vs `200` |
| G2 | `UseAuthentication` before `UseAuthorization` (`:258→259`) | **order** | ❌ | ✅ loud — infinite login loop |
| G3 | `UseAuthentication` before `UseAntiforgery` (`:258→261`) | **order** | ❌ | ✅ loud — every form POST `400` |
| G4 | **Static assets anonymous** (`:263`) | metadata | ❌ | ❌ **content type is the oracle** |
| G5 | Sign-out reachable signed-out (`:270`) | metadata | partial | ❌ both are `302`; `Location` is the oracle |
| G6 | Render-mode builder **not** anonymous (`:264-265`) | metadata, by **absence** | ❌ (a hand-run `grep` only) | ✅ but only if someone probes |
| G7 | Identity pages anonymous by folder `_Imports` | metadata, **inherited** | ❌ | ✅ but only if someone probes |
| G8 | `/not-found` renders rather than challenges (`:248`) | order + metadata | ✅ anonymous `404` | ✅ `404` vs `302` |
| G9 | HSTS header in production (`:246`) | order + platform | ❌ **unreachable in tests** | ❌ header presence only |
| G10 | Auth cookie carries `Secure` | platform (default `SameAsRequest`) | ❌ | ❌ `Set-Cookie` attributes only |
| G11 | HTTPS enforced | platform (`httpsOnly: true`) | ❌ | outside the app |
| G12 | Key ring survives restart (`:157-158`) | registration | ❌ | ❌ delayed, restart-triggered |
| G13 | Key ring encrypted at rest (`:192`) | registration, **Production-only** | ❌ **unreachable in tests** | ❌ no HTTP signal at all |
| G14 | E2E harness absent from Release | **build-time** | E2E `AGENTS.md` ritual | ❌ no HTTP signal |
| G15 | Migrations run on the boot path (`:212-239`) | order | ✅ `MigrationGuardTests` (partial) | ❌ absence of *any* response |
| G16 | Fail-fast on missing config (`:35,86,94,183`) | order | partial | ❌ absence of *any* response |

**The finding that reframes the phase:** the ORDER-enforced set is small and *loud* — G2 and G3 are
the whole of it, and both fail in ways nobody could miss. **The dangerous surface is metadata.**
Five of the six status-invisible guarantees are metadata or platform, and the two metadata ones
(G4, G7) fail by an attribute appearing in, or disappearing from, a file that never mentions
authorization.

**G6 deserves separate note because it is enforced by an absence.** `MapRazorComponents<App>()`
returns one builder covering *every* page route, and an `IAllowAnonymous` marker anywhere in an
endpoint's metadata short-circuits authorization for it. The decision not to mark it is recorded
three times (`S-01`, re-checked in `S-02` and `S-04`), and its guard today is a checklist line —
`context/archive/2026-09-13-manage-saved-cards/plan.md:611`, *"`AllowAnonymous` still appears on
exactly the four known surfaces"* — ticked by a human running `grep`. Nothing fails if a future
change adds a third call.

### 3. Risk #1 — the anonymous-asset mechanism, traced

Static assets are served **only** by `MapStaticAssets()` ([Program.cs:263](TenExCards/TenExCards/Program.cs#L263)).
`UseStaticFiles()` appears nowhere in the repository. So every `wwwroot` file and every
framework/scoped-CSS asset is a **routed endpoint**, which is exactly why the fallback policy
reaches them.

Today an anonymous `GET /app.css` returns `200 text/css`. With `.AllowAnonymous()` removed from
`:263`, the hop chain is:

1. endpoint carries no authorization metadata → `UseAuthorization()` (`:259`) applies the fallback
   policy (`:150-152`, `RequireAuthenticatedUser()`)
2. anonymous fails → challenge on the default scheme, set at `:107-110` to
   `IdentityConstants.ApplicationScheme`
3. the cookie handler's challenge is a redirect to `LoginPath`, set at `:115` to `/Account/Login`
4. → `302` to `/Account/Login?ReturnUrl=%2Fapp.css`
5. `/Account/Login` is anonymous by **inheritance** from
   [Components/Account/Pages/_Imports.razor:2](TenExCards/TenExCards/Components/Account/Pages/_Imports.razor#L2)
6. → `200 text/html` — **the sign-in page delivered as the stylesheet**

Blast radius: `_framework/blazor.web.js` is referenced the same way
([App.razor:20](TenExCards/TenExCards/Components/App.razor#L20)), so both interactive pages lose
their circuit while every response stays `200`.

This was **measured once, by hand, on 2026-09-12** and never pinned —
`context/archive/2026-09-12-accounts-and-sessions/change.md:176-181` records every asset returning
`200` with its own content type while signed out, against a deliberately gated control route. That
is the shape of the test Phase 1 owes, and the control-route half of it matters (see §7).

### 4. Risk #1 — what the deploy gate asserts, and what it structurally cannot

[scripts/verify_deploy.py](scripts/verify_deploy.py) is 258 lines of standard library only.

**The entire oracle is one integer**, `:241`:

```python
status, _, _ = fetch(url, args.timeout)     # :236 — body and landed URL discarded
...
if status != 200:                            # :241 — the whole assertion
```

`fetch()` (`:70-83`) returns `(status, body, landed_url)` from *outside* its `with` block, so the
`HTTPResponse` — and with it `.headers` and `Content-Type` — is closed and destroyed at `:80`
before the caller ever sees it. Redirects are followed unconditionally through urllib's default
opener; there is no cookie jar anywhere, so every request is the anonymous case that triggers the
redirect.

**Asset discovery is narrow.** `AssetCollector` (`:47-67`) extracts exactly
`<link rel="…stylesheet…">/@href` and `<script src>`. Not `modulepreload`, not `preload`, not
`icon`, not `img`, not `@import` inside CSS, and nothing a script fetches at runtime.

**Failure classes the gate records as a PASS** (from the code):

| Class | Why invisible |
| --- | --- |
| a gated asset redirecting to sign-in | follows redirects (`:79`); asserts the final status only (`:241`) |
| **the root page itself redirecting to sign-in** | `:125` fails only on a **different host**; a same-host `/` → `/Account/Login` is logged (`:121`) and accepted, and the login page's assets become what is verified |
| any asset served as `text/html` | `Content-Type` destroyed at `:80` |
| an empty or truncated `200` body | body discarded at `:236` |
| assets referenced from CSS, or injected by script | no CSS parse, no JS execution |
| a deployed build that is the **wrong commit** | CI stamps `SourceRevisionId` into the footer ([deploy.yml:105](.github/workflows/deploy.yml#L105)) and the gate never reads it |
| every route other than `/` | exactly one page is fetched (`:200`) |

**The insertion point, and a constraint on it.** A content-type assertion must sit beside `:241`,
but it requires changing `fetch()`'s return, because the header is destroyed at `:80`. Redirect
detection for *assets* needs no such change — the landed URL is already the third element and is
merely thrown away by the `_, _` at `:236`.

**A narrowing the plan must respect.** The root-redirect handling is not an oversight; it is a
2026-09-10 fix that was deliberately scoped.
`context/archive/2026-09-08-deploy-pipeline/reviews/impl-review-phase-1-2.md:105` records:
*"Narrowed from the proposed fix: an http→https upgrade on the same host is `httpsOnly` working and
is logged, not failed."* So a stricter root assertion must distinguish a **scheme upgrade** from a
**gating redirect** — failing all same-host redirects would break `httpsOnly`.

**Both call sites pass zero arguments** (`deploy.yml:113` and `:154`), so any new assertion is on
by default in CI with no workflow edit. Neither script has any test coverage, no Python version is
pinned, and `deploy.yml` triggers only on push to `main` — so a syntax error in either script is
first observed **on `main`, mid-deploy**.

### 5. Risk #5 — everything the boot path demands

#### 5.1 The demands

| # | Demand | Missing ⇒ | Env-conditioned? |
| --- | --- | --- | --- |
| D1 | `ConnectionStrings:DefaultConnection` (`:35-41`) | throw at builder config | no |
| D2 | `Generation:*` (`:64-71`) | **silent defaults**; invalid ⇒ throw at `app.Run()` | no |
| D3 | `Cards:MaxResults` (`:77-80`) | **silent default 20**; ≤0 ⇒ throw at `app.Run()` | no |
| D4 | `Gemini:ApiKey` (`:86-90`) | throw at builder config | **no** — deliberately unconditional |
| D5 | `Gemini:Models` non-empty (`:94-99`) | throw at builder config | no |
| D6 | `DataProtection:KeyIdentifier` (`:181-193`) | throw at builder config | **yes — skipped in Development** |
| D7 | scoped `AppDbContext` for the key ring (`:53-54`) | **first rendered form** throws, not boot | no |
| D8 | reachable + migratable database (`:212-239`) | throw before listening | no |

D2 and D3 are worth noting as the quiet ones: the section missing entirely does **not** throw,
because the options types carry passing C# defaults. Only a *configured* bad value fails, and it
fails later, at `app.Run()`.

#### 5.2 The three-environment matrix

| Demand | Local dev | CI test job | Deployed container |
| --- | --- | --- | --- |
| D1 connection string | user-secrets → `sqldb-tenexcards-dev` | **dummy string inside the test project** | Key Vault reference |
| D4 Gemini key | user-secrets (real) | **dummy string inside the test project** | Key Vault reference |
| D6 key identifier | ➖ skipped (Development) | ➖ skipped (factory forces Development) | ✅ plain pointer app setting |
| D8 database | `sqldb-tenexcards-dev` | ➖ **never exercised** — flag set; the one test that lets it run asserts it **throws** | S0, DDL, on the boot path |
| Environment name | Development | Development | **Production** (by framework default — verified §6) |

**`deploy.yml` contains no `env:` block anywhere** — zero at workflow, job and step level. The
three GitHub secrets are Azure OIDC identifiers consumed five steps *after* `Test`. CI supplies the
test run with **nothing**; every demand is satisfied by hard-coded strings inside the test project.

#### 5.3 The coverage hole

**`DataProtection:KeyIdentifier` (D6) is exercised in none of the three environments.** It is
skipped locally by the Development guard, skipped in CI because the factory forces Development, and
live only in the container. The same is true of `UseHsts()` (`:246`), which sits in the same
`if (!IsDevelopment())` branch.

So the gate at `deploy.yml:83-84` proves that `Program.cs` boots **when every external demand is
stubbed** — not that it boots when they are real. The 2026-09-13 CI failure is the recorded
instance of the *inverse* asymmetry: a guard CI caught precisely because CI lacks the user-secrets a
dev machine has (58/58 locally, one failure in CI, `deploy-plan.md:1007-1013`).

#### 5.4 The design fork this creates — the phase's central constraint

The existing factory's `UseEnvironment(Environments.Development)` at `:44` is not incidental. Its
own comment says it is *"Kept explicit so a future framework change to that default cannot silently
make this factory start reaching Key Vault"* — i.e. it exists to keep tests **away** from the
Production branch.

That makes G9 (HSTS) and G13/D6 (key-ring encryption + its guard) **unreachable through the
existing host by construction**. Any Phase 1 test of them needs a *second* factory shape running as
Production — which then demands `DataProtection:KeyIdentifier`, and whose `DefaultAzureCredential`
would be constructed. Note `ProtectKeysWithAzureKeyVault` is registration-time; the credential is
used lazily by the key-ring encryptor, so the code does **not** prove a boot-time credential call
either way. Whether that boundary is crossable without network access is the single most important
thing for `/10x-plan` to settle, and the cheap options are:

- assert the **guard** fires (a Production-shaped host with the setting *absent* must throw with the
  known message) without ever constructing a working encryptor — this is the "check the check" shape
  `lessons.md` demands, and needs no Key Vault;
- leave the encryptor itself to the existing manual `Xml`-column verification, which is already
  documented with its false-positive trap.

The first is the one that maps to the risk: the recorded failure is *a container that does not
serve*, and its cause is a setting absent at merge time — not a mis-encrypted ring.

### 6. Live deployed state (verified this session, 2026-09-13)

`az webapp config appsettings list -g rg-tenexcards-plc -n tenexcards-ka` returns **exactly four**
settings:

| Setting | Value shape |
| --- | --- |
| `WEBSITE_HTTPLOGGING_RETENTION_DAYS` | `3` |
| `DataProtection__KeyIdentifier` | plain versionless key URI (correct — a pointer, not a reference) |
| `ConnectionStrings__DefaultConnection` | `@Microsoft.KeyVault(SecretUri=…/secrets/sql-connection-string/)` — trailing slash present |
| `Gemini__ApiKey` | `@Microsoft.KeyVault(SecretUri=…/secrets/gemini-api-key/)` — trailing slash present |

Three consequences:

1. **`ASPNETCORE_ENVIRONMENT` is absent**, confirming by measurement what was previously only
   inferable from `main.bicep` and `deploy.yml`: the container runs **Production** by framework
   default. That is what makes the Key Vault guard and `UseHsts()` fire there. Nothing pins it — an
   app setting added by hand would disable both, with every response still `200`. This is a
   platform-class sibling of G4.
2. All three configuration demands are present and correctly shaped.
3. It **closes a provenance gap**: `deploy-plan.md` records `az webapp config appsettings set` for
   the connection string (`:619`) and the key identifier (`:868`) but has **no dated entry** for
   `Gemini__ApiKey`, which `AGENTS.md` asserts exists. It does exist. The record, not the setting,
   was missing.

All four values above are pointers, not secrets.

### 7. The precedent set — checks that reported success while being wrong

Seventeen recorded instances across the archive, of which three bear directly on this phase:

- **`MigrationGuardTests` — a live near-miss inside this very suite.** Plan review F8, 2026-09-13
  (`context/archive/2026-09-12-passage-to-saved-cards/reviews/plan-review.md:239-244`): the
  unconditional `Gemini:ApiKey` guard throws the *same* `InvalidOperationException` earlier, at
  service configuration, so the assertion would be satisfied before the migration block is ever
  reached — *"green test, no guarantee"*. The fix was to match on
  `*Relational-specific methods can only be used*`. **This is the closest existing analogue to what
  Phase 1 must produce**, and the reason every new absence/guard assertion must pin the *cause*,
  not merely the exception type.
- **The `strings | grep -c` absence check** (`lessons.md:80-93`) — returned `0` for a type that was
  certainly present. A wrong command returning the *desired* answer. Every absence check in this
  phase needs a control that must be found in the same run.
- **`verify_deploy.py` F3, 2026-09-10** — the false green was predicted *before* Identity landed and
  fixed only for the different-host case; §4 above is the residue of that deliberate narrowing.

The positive precedent is equally usable: `S-01` proved the CI gate could actually fail by
committing a deliberately failing test and reading the run's **step list** rather than its colour
(`context/archive/2026-09-12-accounts-and-sessions/change.md:149-155`). Phase 1's gate-strengthening
work should be proven the same way.

---

## Code References

- [Program.cs:150-152](TenExCards/TenExCards/Program.cs#L150-L152) — the authorization fallback policy
- [Program.cs:107-120](TenExCards/TenExCards/Program.cs#L107-L120) — default scheme and `LoginPath`, the two lines that turn a failed authorization into a `302`
- [Program.cs:258-261](TenExCards/TenExCards/Program.cs#L258-L261) — the order-enforced triple
- [Program.cs:263](TenExCards/TenExCards/Program.cs#L263) — `MapStaticAssets().AllowAnonymous()`, guarantee G4
- [Program.cs:264-265](TenExCards/TenExCards/Program.cs#L264-L265) — the render-mode builder, deliberately un-anonymous (G6)
- [Program.cs:270](TenExCards/TenExCards/Program.cs#L270) — `MapIdentityLogout().AllowAnonymous()`
- [Program.cs:181-193](TenExCards/TenExCards/Program.cs#L181-L193) — the Production-only key-identifier guard (D6)
- [Program.cs:212-239](TenExCards/TenExCards/Program.cs#L212-L239) — the boot-path migration and its test-only flag
- [Program.cs:274-276](TenExCards/TenExCards/Program.cs#L274-L276) — `public partial class Program;`
- [Components/Account/Pages/_Imports.razor:2](TenExCards/TenExCards/Components/Account/Pages/_Imports.razor#L2) — folder-wide `[AllowAnonymous]`, guarantee G7
- [TenExCardsWebApplicationFactory.cs:29-44](TenExCards/TenExCards.Tests/TenExCardsWebApplicationFactory.cs#L29-L44) — the four injected settings and the Development pin
- [AuthBoundaryTests.cs:88](TenExCards/TenExCards.Tests/AuthBoundaryTests.cs#L88) — the `302`-never-`401` contract, with `verify_deploy.py` named in the comment
- [verify_deploy.py:70-83](scripts/verify_deploy.py#L70-L83) — `fetch()`, where the response object is destroyed
- [verify_deploy.py:117-130](scripts/verify_deploy.py#L117-L130) — root redirect handling; fails only on a different host
- [verify_deploy.py:234-246](scripts/verify_deploy.py#L234-L246) — the asset loop and its single-integer oracle
- [deploy.yml:83-84](.github/workflows/deploy.yml#L83-L84) — the gating test step, with no `env:` anywhere in the file

## Architecture Insights

- **Metadata, not ordering, is the fragile surface.** Ordering failures here are loud (login loop,
  `400` on every form). Metadata failures are silent and are caused by attributes in files that
  never mention authorization — an inherited `_Imports.razor`, or a `.AllowAnonymous()` on a builder
  that covers every route.
- **Two guarantees are enforced by an absence** (G6, and `ASPNETCORE_ENVIRONMENT` not being set).
  Absences cannot be asserted by exercising the happy path; they need a probe that would change
  answer if the absence ended.
- **The app-side and gate-side halves of risk #1 are one mechanism.** The app returns `302 → 200
  text/html`; the gate follows redirects and reads only the integer. Fixing either alone leaves the
  hole open from the other side, which is why the phase's two test types belong in one change.
- **The test host's environment pin is a deliberate wall, not an accident.** It buys "tests never
  reach Key Vault" at the cost of "the Production branch is untested". Phase 1 is where that trade
  gets re-priced.
- **CI's guarantee is narrower than it appears.** It proves the app boots with every external
  demand stubbed by constants inside the test project. The deployed boot path shares no mechanism
  with it except the code.

## Historical Context (from prior changes)

There are **no `research.md` files in `context/archive/`** — the equivalents are `plan-brief.md`
and the `### Key Discoveries` sections inside each `plan.md`.

- `context/archive/2026-09-12-accounts-and-sessions/plan.md:493-620` — the authoritative
  request-pipeline research: the pre-Identity pipeline verbatim, where `UseAuthentication`/
  `UseAuthorization` go, the endpoint-metadata mechanism of fallback policies, and why
  `Testing:SkipStartupMigration` cannot live in the test project.
- `context/archive/2026-09-12-accounts-and-sessions/change.md:176-192` — the **measured** static-asset
  result and the recorded rejection of marking the render-mode builder anonymous. Do not re-derive.
- `context/archive/2026-09-12-accounts-and-sessions/reviews/plan-review.md:36-121` — F1 (static-asset
  gating and why CI cannot detect it, both fixes costed), F2 (boot-path migration vs
  `WebApplicationFactory`), F3 (the app-setting ordering rule). All CRITICAL, all fixed.
- `context/archive/2026-09-08-deploy-pipeline/reviews/impl-review-phase-1-2.md` — the densest source
  on what the deploy scripts do and do not prove: F1 fail-open, F3 false green, F4 dead circuits,
  plus a `## Verified and explicitly NOT findings` section that pre-clears eight questions
  *"so they are not re-raised in a later review."*
- `context/archive/2026-09-08-deploy-pipeline/follow-ups/review-fixes.md:57-63` — **F4 is still
  open**: `blazor.web.js` returns `200` whether or not any circuit works, so a deploy with every
  circuit dead passes fully green. Deferred in 2026-09-10 on the grounds that circuits carried no
  product behaviour until `S-01`. `S-01`, `S-03` and `S-04` have since landed.
- `context/deployment/deploy-plan.md:1069-1075` — the two checks *"`verify_deploy.py` structurally
  cannot make"*, both currently done by hand after every CI run. Phase 1 is the change that stops
  them being manual.
- `context/foundation/lessons.md` — "Prove the check before trusting the result" and "A negative
  check needs a control, or it cannot fail" are the two that govern every assertion this phase adds.

## Related Research

- `context/foundation/test-plan.md` §2 — risk statements, oracles and anti-patterns for #1 and #5.
  Not restated here by design.
- `context/changes/testing-pipeline-guarantees/change.md` — the phase brief.
- `context/changes/outcome-recording/` — `S-06`, whose data Phase 4 protects; unrelated to this
  phase but adjacent on the roadmap.

## Open Questions

1. **`Routes.razor`'s `<NotAuthorized><RedirectToLogin /></NotAuthorized>` may have no trigger.**
   The fallback policy is an HTTP-pipeline construct enforced by `UseAuthorization()`;
   `AuthorizeRouteView` evaluates component-level `[Authorize]` metadata, and **no page carries
   one**. `RedirectToLogin.razor:1-5`'s own comment asserts the opposite intent — that it is what
   redirects an in-circuit client-side navigation to a gated route. Either the component is dead
   code or there is a real in-circuit gap where an interactive nav to a gated route does not
   redirect. Worth one probe; it is cheap and it is exactly the "guarantee that looks present" shape
   of risk #1.
2. **Can a Production-shaped test host exist without network access?** §5.4. The guard-fires
   assertion appears to need no Key Vault, but this has not been executed.
3. **`UseStatusCodePagesWithReExecute("/not-found")` catches every 4xx**, so a `403` renders "does
   not exist". `F-01` flagged this as a forward-looking note and asked `S-01` to branch on it; the
   record does not show it being done
   (`context/archive/2026-09-08-blazor-server-shell/plan.md:275-278`).
4. **F4 (dead circuits pass the gate) is still open** and its deferral rationale has expired.
   In scope for Phase 1 or explicitly re-deferred?
5. **Nothing pins `ASPNETCORE_ENVIRONMENT` being unset.** Adding it as an app setting would silently
   disable HSTS and key-ring encryption. Is that a test, a documented check, or accepted?
6. **Neither deploy script has any test**, and a syntax error in either surfaces first on `main`,
   mid-deploy. Phase 1 modifies `verify_deploy.py`; does it also owe that script a way to be proven
   runnable before it gates a deploy?
