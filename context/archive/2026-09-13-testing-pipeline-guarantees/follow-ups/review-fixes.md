# Follow-ups — `testing-pipeline-guarantees`

Deferred from the implementation review of 2026-09-14. Everything else the review raised was fixed
in this branch's review-fix commit; these are the items where the fix was larger than the finding.

## 1. The in-memory `AppDbContext` block now exists in three copies

`TenExCardsWebApplicationFactory.cs`, `MigrationGuardTests.cs` and `BootPathGuardTests.cs` each
carry the same descriptor-removal-plus-re-registration block verbatim.

This is not cosmetic. `TenExCards.Tests/AGENTS.md` documents the block as subtle and easy to get
wrong — `RemoveAll<DbContextOptions<AppDbContext>>` alone is insufficient, because
`AddDbContextFactory` also registers its options-configuring delegate as its own entry, which EF
applies **additively**. Three copies means a future correction has to land in three places or the
suite drifts silently.

**Fix**: extract `internal static class TestHost` with
`UseInMemoryStore(IServiceCollection services, string databaseName)`, call it from all three, and
point `TenExCards.Tests/AGENTS.md` point 3 at that method instead of describing the block.

**Why deferred**: it edits the shared factory that every other test class depends on, which is
beyond what a review of this change should change. Worth doing before a fourth copy appears.

## 2. `ShippedModelRotation_IsNotEmpty` boots a host it does not need

It starts a full Development host — which loads the developer's `secrets.json` — to read a value
that ships in `appsettings.json`. Because user-secrets can only *add* keys, a developer who happened
to have `Gemini:Models` in their secret store would see it pass locally for the wrong reason while
CI failed. The failure direction is the safe one (deploy blocked, not shipped), so this is not
urgent.

**Fix**: read the shipped file directly with a `ConfigurationBuilder`, removing both the secrets
exposure and the `CreateClient()` call.

**Why deferred**: locating `appsettings.json` from the test assembly's output directory needs a path
that holds in CI and locally, and getting that wrong trades a small hazard for a flaky test. See
also `test-plan.md` §7, which records the underlying guard as deliberately unpinned.

## 3. `redirect_rejection` compares `netloc` exactly

`https://h/` versus `https://h:443/`, or a host differing only in case, both read as "a different
host" and would fail the deploy. Neither arises against `*.azurewebsites.net`, which is lowercase
and sends no explicit port in `Location`.

**Fix**: compare `hostname.lower()` plus a port normalised against the scheme's default.

**Why deferred**: it adds a normalisation function — and its own self-test cases — to guard a case
this deployment cannot produce. Revisit if the app ever moves behind a custom domain or a
non-default port.

## 4. `--self-test` structurally cannot cover the script's plumbing

It exercises the extracted predicates only. `fetch()`, `fetch_root()`, `normalise_base()`,
`same_origin()`, `AssetCollector` and `main()` all need a network or a full run.

**This is not theoretical.** During the review a patch to `verify_deploy.py` silently deleted four
functions — `fail`, `fetch_root`, `same_origin`, `normalise_base`. Both `py_compile` **and**
`--self-test` passed; only running the script against the live site caught it, as a `NameError` at
line 277. `py_compile` checks syntax, not name resolution, and the self-test returns before touching
any of it.

**Fix**: add cases that walk the plumbing without a network — feeding `AssetCollector` a small HTML
string and asserting the extracted list, and calling `main(["--base-url", "not-a-url"])` to exercise
`normalise_base`'s failure path.

**Why deferred**: it changes the self-test's shape from "predicates" to "predicates plus a harness",
which is a design decision rather than a fix. The over-claim it caused has been corrected everywhere
in the meantime — `scripts/README.md`, `deploy.yml`'s step comment and `TenExCards/AGENTS.md` now
all say plainly what the self-test does not cover.
