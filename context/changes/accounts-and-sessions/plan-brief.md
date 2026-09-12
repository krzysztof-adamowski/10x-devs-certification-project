# Accounts and Sessions — Plan Brief

> Full plan: `context/changes/accounts-and-sessions/plan.md`

## What & Why

`S-01` gives 10xCards its account boundary: register with an email and password, sign in, sign out,
and own everything you create. It implements FR-001 through FR-003 and the PRD's `## Access Control`.
It is also the slice three earlier decisions were deferred into — the Data Protection key ring is
plaintext and is about to start signing auth cookies, no test project exists, and two proof-of-life
pages exist only until an auth boundary does. `S-02`, the north star, is blocked on this.

## Starting Point

The app is a deployed Blazor Server shell with per-page interactivity, EF Core against Azure SQL, one
migration, and a Data Protection key ring that persists to the database but is stored unencrypted.
Auth is entirely absent — no authentication middleware, no authorization, no `AuthorizeRouteView`,
no user entity. `AppDbContext` derives from plain `DbContext` with no `OnModelCreating`. There is no
test project and no solution file anywhere in the repository.

## Desired End State

A learner registers, is signed in, signs out, and signs back in. Every route except the home page,
the Identity pages and the error pages requires authentication. A session survives seven days of
inactivity and survives a container restart — and the key ring signing that session cookie is
encrypted at rest, with the plaintext key that preceded it gone. `TenExCards.Tests` asserts the auth
boundary and gates the deploy. Both scaffolding pages and the `SpineProbes` table are gone.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Identity UI surface | Hand-write Register/Login/Logout; borrow 4 template infrastructure pieces | The template generates 47 files / 3,349 lines, most of it forbidden by the PRD (password recovery) or `AGENTS.md` (roles) — absence beats deletion. |
| Schema base | `IdentityUserContext<ApplicationUser>` | Creates four tables and no role or passkey tables, making "never add roles" structural rather than conventional under forward-only migrations. |
| User entity | `ApplicationUser : IdentityUser`, empty | `S-02` and `S-06` need it; adding it now is 8 lines instead of a type change rippling through DI, the context base and the snapshot later. |
| Auth boundary | Fallback policy requires auth; `/`, Identity and error pages anonymous | New routes are protected by default, and `/` staying open keeps the build marker readable and keeps CI verifying the real app page. |
| Deadline posture | Correctness over the PRD's `2026-09-14` frontmatter date | Chosen explicitly; it is why key-ring encryption and a real test harness are in scope rather than deferred. |
| Session lifetime | 7-day sliding, always persistent | Resolves PRD Open Question 1 — a weekly learner never re-authenticates, an abandoned session dies within a week. |
| Password policy | Minimum 16 characters, no character-class rules | Length beats composition, and a forgotten password is a permanently dead account here. |
| Lockout | Identity default, enabled | Blunts online guessing against an account that cannot be recovered; the lock expires on its own so it creates no dead accounts. |
| Key ring at rest | Encrypt with a Key Vault key, production only | Free now, before any account exists; after `S-02` the same change signs every learner out mid-triage. |
| Test scope | Integration via `WebApplicationFactory` | Tests the invariant that stops `S-02` leaking one learner's cards to another, and hands `S-02` a working harness. |
| Solution file | Add `TenExCards.sln` | A second project needs one entry point; CI keeps publishing the csproj by path. |
| CI test gate | `dotnet test` before publish in `deploy.yml` | A push to `main` is a production deploy; a suite that gates nothing decays. |

## Scope

**In scope:** register, sign in, sign out; role-free Identity schema; auth boundary with an anonymous
allowlist; 7-day sliding session; key-ring encryption at rest plus discarding the plaintext key;
`TenExCards.sln` and `TenExCards.Tests` with a CI gate; deletion of both proof-of-life surfaces and
the `SpineProbes` table; updating `AGENTS.md`, the risk register, the deployment record, the roadmap
and the PRD's open question.

**Out of scope:** password recovery, reset and email confirmation; two-factor, passkeys, external
logins; account management, deletion and data export; roles and any claim beyond "authenticated";
account-scoped product data (that is `S-02`); branch protection and narrowing the CI principal's
`Contributor` role; Bicep deployment from CI; a second data store.

## Architecture / Approach

Identity goes into the existing `AppDbContext` — no second store — on the role-free
`IdentityUserContext` base, keeping its `IDataProtectionKeyContext` responsibility. The three pages
are statically rendered SSR forms following the pattern `DbCheck.razor` already proves, because
`SignInManager` writes cookies to the HTTP response and a circuit cannot. `Program.cs` gains cookie
authentication, Identity core, a fallback authorization policy, and `UseAuthentication`/
`UseAuthorization` ahead of the existing `UseAntiforgery`. The key ring chains
`ProtectKeysWithAzureKeyVault` onto the existing `PersistKeysToDbContext`, guarded to non-Development
so the laptop needs no vault key permissions.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Close the key-ring exposure | Encrypted ring, plaintext key discarded, guarantee re-verified | A new boot-path dependency on Key Vault; role assignments propagate slowly and `az role assignment` is unusable here |
| 2. Identity data model | Four tables in the database, nothing else | Forward-only migration on the boot path with no deployment slots |
| 3. Register, sign in, sign out | The slice's actual outcome | A `401` instead of a `302` at a gated route fails CI immediately |
| 4. Test project and CI gate | `TenExCards.sln`, `TenExCards.Tests`, deploy gated on tests | A test harness that reaches the real database, or a gate never observed failing |
| 5. Retire the proof-of-life surfaces | Both pages, the entity, and the `SpineProbes` table gone | Two entry points for one page; deleting before Phase 3's check passes removes the fallback |
| 6. Update the repository record | `AGENTS.md` surgery, risk register, deploy record, roadmap, PRD | Two paragraphs inside the deleted section must be moved, not dropped |

**Prerequisites:** `F-01` and `F-02` landed (both have). Azure access to create a Key Vault key and a
role assignment. The development-machine firewall rule `dev-machine-krzychu` current for your home IP.

**Estimated effort:** ~4–6 sessions across six phases; Phase 1 and Phase 3 are the largest.

## Open Risks & Assumptions

- **The PRD's `hard_deadline` is `2026-09-14` and five slices are unstarted.** Planning proceeded on
  an explicit decision to prioritise correctness; the date is treated as superseded and Phase 6 flags
  the frontmatter for correction.
- **Key Vault becomes a runtime dependency of the first rendered form.** A failure presents as forms
  breaking rather than a failed boot, which is harder to attribute.
- **Purge protection is deliberately off on the vault** to keep teardown a single command. Losing the
  key makes the encrypted ring unreadable — recoverable here only because it costs a re-login.
- **`EmailIndex` is created non-unique.** Email uniqueness is database-enforced only because
  registration sets `UserName` to the email address; changing that silently reopens a race.
- **`az role assignment` returns `MissingSubscription` on this subscription** — every command in the
  group. The role assignment must be verified through `az rest`.
- **Deployment trust is unchanged.** Anyone who can push to `main` can cause Azure to mint a
  `Contributor` token; branch protection is still absent and this slice does not add it.

## Success Criteria (Summary)

- A learner who has never used the product can register, be signed in, sign out, and sign back in —
  and a signed-in session survives a container restart.
- No route other than the home page, the Identity pages and the error pages serves content to an
  anonymous visitor.
- The single `DataProtectionKeys` row is ciphertext, and the plaintext row that preceded it is gone.
- A deliberately failing test blocks the deploy, confirmed by reading the failed run's step list.
