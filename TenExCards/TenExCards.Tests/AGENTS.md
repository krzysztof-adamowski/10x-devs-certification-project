# Testing Guidelines

Rules for `TenExCards.Tests`. Moved here from `TenExCards/AGENTS.md` when `S-01` created this
project on 2026-09-12, because they belong next to the tests. That file keeps only the
FluentAssertions prohibition, which is a licensing rule rather than a testing one.

Paths here are written from the **repo root**, like every other `AGENTS.md` in this repository:
this project is `TenExCards/TenExCards.Tests/` and the application is `TenExCards/TenExCards/`.

## This suite gates the deploy

`.github/workflows/deploy.yml` runs `dotnet test TenExCards/TenExCards.Tests/TenExCards.Tests.csproj`
between `Set up .NET` and `Publish`, and a push to `main` is a production deploy. **A failing test
stops the deploy**, verified on 2026-09-12 by committing a deliberately failing test and reading the
run's *step list* rather than its colour: `Test` failed and `Publish`, `Pack`, `Retain`, `Azure
login`, `Deploy` and `Verify` all showed skipped. That is why a red suite is never something to work
around.

Both project paths are named explicitly in CI, never resolved through `TenExCards.slnx` — a bare
`dotnet test` at the repo root would pull in the application project too. The solution is an IDE and
local-build convenience; CI must not discover anything implicitly.

## What to test

Test the **deterministic rules**, never the model's prose. Card quality is judged by the learner at
triage; an assertion against generated card text is a flaky test, not a quality gate.

Assert on what the code does: over-length submissions are refused *before* generation begins,
duplicate candidates are dropped, each candidate is triaged exactly once, and **every query is
scoped to the owning account**. That last one is the invariant this project exists to protect —
`S-01` built the boundary, and from `S-02` onward every persisting slice must assert its own queries
sit behind it.

Two categories deserve a test even though nothing in this repository *writes* them:

- **Configured policy that deviates from a framework default.** Password length 16, every
  character class disabled, lockout enabled, a seven-day sliding cookie. Each carries a comment in
  `Program.cs` explaining why — and a comment is what a later agent removes as an inconsistency. The
  assertion is what stops that. See `IdentityConfigurationTests`.
- **Security-relevant behaviour that is inherited rather than written.** Password storage is the
  framework's `PasswordHasher<ApplicationUser>` with `IdentityV3` compatibility mode; the test pins
  the iteration count **observed at implementation time**, not a number from a document, so that a
  future framework change surfaces as a failure naming the old and new values instead of passing
  silently. See `PasswordStorageTests`.

One assertion is easy to believe is covered when it is not: **a registered user's `UserName` must
equal the submitted email address.** `EmailIndex` is created non-unique and `RequireUniqueEmail` is
an application-level check, so the only database-level guard on email uniqueness is the unique
`UserNameIndex` standing behind it. The duplicate-email test passes either way — it goes through
that same application-level check — so the race would reopen silently with every test still green.

## Assertions use AwesomeAssertions

**Never FluentAssertions.** Its v8 moved to a paid commercial licence; AwesomeAssertions is the
Apache-2.0 fork of v7 with the same API, so the training-data reflex compiles cleanly and introduces
a licensing problem silently. Nothing fails to warn you.

## The host: `TenExCardsWebApplicationFactory`

It boots the real `Program.cs` pipeline — that is the point, since the auth boundary lives in that
pipeline — against an EF in-memory database, with **no real connection string, no boot-path
migration, and no network**. Measured on 2026-09-12 rather than argued: `testhost.exe` held zero
established connections for the whole run. (The outer `dotnet` CLI process opens its own telemetry
connections; those are the SDK's, not the code under test's.)

Four things it arranges, each non-obvious enough to be worth knowing before you change it:

1. **A dummy `ConnectionStrings:DefaultConnection` via `UseSetting`.** `Program.cs` throws on a null
   one *before* any service replacement can run, so the value must exist even though nothing reads
   it. This is why the no-network claim is "no *real* connection string", not "no connection string".
2. **`Testing:SkipStartupMigration`.** `WebApplicationFactory` intercepts at `IHost.Start()`, so
   everything between `builder.Build()` and `app.Run()` — the whole migration block — runs in tests,
   after the provider has already been swapped. `GetPendingMigrationsAsync()` throws against the
   in-memory provider. **The flag defaults to running the migration** and only this factory sets it;
   inverting that default would turn a schema failure into a silent one.
3. **Removing every `AppDbContext` DI descriptor, not just `DbContextOptions<AppDbContext>`.**
   `AddDbContextFactory` also registers its options-configuring delegate as its own entry, which
   `DbContextOptionsFactory` applies **additively**. Leaving it in place while adding an in-memory
   registration fails at first use with "Services for database providers … have been registered".
   Filtering by generic argument catches it without naming an internal interface type.
4. **`UseEnvironment(Development)`, belt-and-braces.** `WebApplicationFactory` already defaults to
   Development on its own — measured, and asserted as its own test — which is what makes
   `Program.cs`'s Key Vault guard skip. The explicit call is kept so a framework change to that
   default fails loudly instead of silently reaching Key Vault from every test run.

**A fresh, uniquely named database per factory instance.** `IClassFixture<>` gives one instance per
test class, so classes never share state while tests within a class share a database the way they
would share a real one.

## Test doubles

Stub the **data provider** and the **key protector**, and nothing else. When the LLM client arrives
in `S-02` it becomes the only other test double — it is stubbed because a real model response is
non-deterministic, not because calling out is inconvenient.
