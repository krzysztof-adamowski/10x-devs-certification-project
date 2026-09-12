<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Accounts and Sessions (S-01)

- **Plan**: `context/changes/accounts-and-sessions/plan.md`
- **Mode**: Deep
- **Date**: 2026-09-12
- **Verdict**: REVISE — **SOUND after triage** (all ten findings fixed in the plan)
- **Findings**: 5 critical, 5 warnings, 0 observations (F10 added after the main triage, raised by the user)

## Verdicts

| Dimension | Verdict | After fixes |
|-----------|---------|-------------|
| End-State Alignment | WARNING | PASS |
| Lean Execution | WARNING | PASS |
| Architectural Fitness | PASS | PASS |
| Blind Spots | FAIL | PASS |
| Plan Completeness | WARNING | PASS |

## Grounding

18/18 claimed paths exist (`TenExCards.sln` correctly claimed absent), 7/7 symbols confirmed,
brief↔plan consistent. One factual correction: the build marker lives in `MainLayout.razor`'s
footer, not on `Home.razor` as Phase 3 change 7 implied — it renders on every page, so the
criterion asserting it at `/` still holds. Corrected in the plan.

Two plausible failure points came back clean and are recorded so they are not re-investigated:

- The blast-radius sweep found **zero** references to `CircuitCheck`, `DbCheck` or `SpineProbe`
  in `scripts/` or `.github/`. Phase 5's deletions cannot break the pipeline.
- Adding a root `TenExCards.sln` changes no existing CI step, because the only SDK invocation
  (`Publish`) names its csproj by path and `pack.py` hardcodes its directories.

## Findings

### F1 — Fallback policy may gate MapStaticAssets endpoints, and CI cannot detect it

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Blind Spots
- **Location**: Phase 3, changes 1 and 7 (anonymous allowlist)
- **Detail**: An authorization fallback policy applies to every endpoint lacking authorization
  metadata. `Program.cs` endpoint-routes both `MapStaticAssets()` and
  `AddInteractiveServerRenderMode()`; Phase 3's allowlist enumerated only page components. The
  detection gap is the sharp part: `verify_deploy.py` sends no cookies and follows redirects, so a
  gated asset resolves `302 → login → 200` and is recorded as a pass. It asserts status only, never
  content type. The documented failure mode it catches is an asset that `404`s; this one returns
  `200`, so criterion "verify_deploy.py passes" goes green while the live site renders unstyled.
- **Fix A ⭐ Recommended**: Verify locally before the Phase 3 deploy and carve out framework endpoints
  - Strength: Costs one local `dotnet run` the phase already requires; catches a broken production
    deploy before it is a production deploy.
  - Tradeoff: Adds a manual gate to a phase that already has several.
  - Confidence: MEDIUM — the endpoint-metadata mechanism is certain; whether `MapStaticAssets`
    ships `AllowAnonymous` metadata in 10.0.12 could not be confirmed from this repository.
  - Blind spot: Whether the `/_blazor` hub is affected — minor now (Phase 5 deletes the only
    interactive page), material in `S-02`.
- **Fix B**: Drop the fallback policy; use `[Authorize]` per gated page
  - Strength: What the .NET 10 template does; no endpoint outside the component set can be affected.
  - Tradeoff: Loses "protected by default", which the brief names as the reason for the choice; a
    forgotten route in `S-02` fails open rather than closed.
  - Confidence: HIGH — well-trodden pattern.
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A — Phase 3 change 7 now covers endpoint-level allowlisting and the
  `verify_deploy.py` blind spot; criteria 3.5 and 3.6 assert content type (`text/css`, not
  `text/html`) locally and then on the deployed site.

### F2 — Phase 4's test host cannot stop the boot-path migration

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 4, changes 3 and 4; criterion 4.7 (as written)
- **Detail**: The migration block sits between `builder.Build()` and `app.Run()`.
  `WebApplicationFactory` runs the entry point and intercepts at `IHost.Start()`, so it executes in
  tests after `ConfigureTestServices` has swapped the provider — and `GetPendingMigrationsAsync()`
  throws against an in-memory provider. Nothing in the test project can prevent it, yet change 3
  limited the `Program.cs` edit to `public partial class Program;`. Criterion 4.7 had the same
  shape: `Program.cs` throws on a null connection string before any service replacement runs.
- **Fix A ⭐ Recommended**: Guard the migration block by configuration in `Program.cs`; widen change 3
  - Strength: Bounded, known cost — one `if` plus a comment. Production boot path unchanged; the
    test provider stays free.
  - Tradeoff: A test-shaped switch in production code, which needs a comment or a later agent
    removes it.
  - Confidence: HIGH — the WAF interception behaviour is well documented.
  - Blind spot: None significant.
- **Fix B**: SQLite in-memory; let the migration run for real
  - Strength: No production-code change; tests exercise the real migration.
  - Tradeoff: Whether it applies depends on `SqlServer:` annotations and the filtered
    `UserNameIndex` in a migration Phase 2 has not generated yet — unknown cost until it exists, and
    a failure would read as an auth-boundary failure.
  - Confidence: MEDIUM — usually works for stock Identity schema; unverified here.
  - Blind spot: Whether the Phase 2 migration emits SqlServer-only annotations.
- **Decision**: FIXED via Fix A — change 3 renamed and widened to authorise a configuration-guarded
  migration block (defaulting to *run*, so a missing setting can never silently skip a migration);
  change 4 covers the dummy connection string and tells the implementer to verify what
  `WebApplicationFactory` sets `ASPNETCORE_ENVIRONMENT` to rather than assume; change 6 names the
  test csproj by path so a bare `dotnet test` cannot resolve the new solution.

### F3 — The key-identifier app setting has no ordering step and no criterion

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 1, change 4 (as written) and Success Criteria
- **Detail**: The key identifier was mentioned inside the encryption change's contract but was not a
  numbered step, had no ordering relative to the code deploy, and no criterion asserted it existed.
  App settings are deliberately outside CI while a push to `main` *is* the deploy, so merging the
  `Program.cs` change first means `new Uri(null)` during service configuration — the container does
  not serve, on a tier with no deployment slots. That is the boot-path failure Phase 2 is sequenced
  to avoid, arriving a phase early through a different door.
- **Fix**: Promote the app setting to its own numbered change ordered before the code, plus criteria
  reading it back and confirming the site still serves before the merge.
  - Strength: Removes the only unsequenced boot-path dependency, and matches how the plan already
    treats every other irreversible step as its own numbered change.
  - Tradeoff: None material.
  - Confidence: HIGH — `Program.cs` already treats a missing configuration value as a deliberate
    hard failure.
  - Blind spot: Whether `ProtectKeysWithAzureKeyVault` validates the URI eagerly or lazily. Eager is
    documented and is the worse case; the plan assumes it.
- **Decision**: FIXED — new Phase 1 change 4 (old 4–6 renumbered to 5–7), criteria 1.6 and 1.11.

### F4 — Deleting the plaintext key rows mints nothing without a restart

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 1, change 6 (was 5)
- **Detail**: Data Protection resolves the key ring once and caches it — `KeyRingRefreshPeriod`
  defaults to 24 hours — so the running app still holds the plaintext key loaded at the encrypting
  build's own deploy restart. Deleting the row does not invalidate that cache: a form exercised
  without a restart succeeds using a key no longer in the database and mints nothing. Observed, that
  reads as the feature failing — zero rows instead of one, no minting boot to inspect, no new row to
  assert ciphertext on.
- **Fix**: Insert a container restart between the delete and the form, with the reason attached so
  it is not optimised away later.
- **Decision**: FIXED — change 6's contract is now "record, delete, restart, exercise", with the
  cache mechanism and the three-criteria failure mode spelled out.

### F5 — Criterion 5.1 cannot pass, and chasing it risks editing an applied migration

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 5, Automated Verification
- **Detail**: `20260910190508_InitialSpine.cs` and its `.Designer.cs` name `SpineProbes`
  permanently. That is applied migration history — EF replays its source against
  `__EFMigrationsHistory` — so the repo-wide search criterion was unsatisfiable, and the obvious way
  to "satisfy" it was the one destructive action available in the phase.
- **Fix**: Scope the search to exclude `TenExCards/Migrations/`; state that applied migrations are
  immutable; name `AppDbContextModelSnapshot.cs` as the only migration-directory file that changes.
- **Decision**: FIXED — criteria re-scoped (5.1–5.3), immutability stated in change 4, progress
  renumbered to 5.1–5.10.

### F6 — Phase 6 audits AGENTS.md only; four live stale references sit elsewhere

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 6, changes 3 and 4; criteria 6.1 and 6.3
- **Detail**: The plan is meticulous about `AGENTS.md`'s two must-move paragraphs and then
  enumerates the other records loosely. Four live references go stale: `roadmap.md`'s "Two tables
  exist (`SpineProbes`, a throwaway `S-01` deletes …)"; `## Open Roadmap Questions` item 3, the same
  inactivity-window question marked `Block: S-01`; the `## Backlog Handoff` row still reading
  `Ready for /10x-plan: no — Needs F-01 and F-02`; and a **runnable command** in `deploy-plan.md`,
  `verify_deploy.py --base-url .../db-check`, which after Phase 5 returns `404` — not in the
  script's transient set, so it hard-fails with wording that says the app answered and this is not a
  warm-up problem. A red that looks like a broken deploy.
- **Fix**: Name all four in changes 3 and 4; widen the search criterion to `roadmap.md`; state
  explicitly that `context/changes/` and the dated `deploy-plan.md` measurements stay untouched.
- **Decision**: FIXED — criteria 6.1–6.4, progress renumbered to 6.1–6.8.

### F7 — The load-bearing email-uniqueness invariant is promised but not tested

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: End-State Alignment
- **Location**: `## Testing Strategy` vs. Phase 4 change 5 and Success Criteria
- **Detail**: The plan flags this race three times — Key Discoveries, Critical Implementation
  Details, and the brief's Open Risks — and `## Testing Strategy` promises the assertion. No Phase 4
  change listed it and no criterion checked it. The phase's actual test, "a duplicate email address
  is refused", passes through `RequireUniqueEmail` in application code whether or not `UserName` is
  the email, so the predicted regression would reopen with every test still green. The
  configured-policy tests had the same gap.
- **Fix**: Add both assertion sets to change 5 with their own criteria.
  - Strength: Closes the gap between the document's own risk analysis and its verification, using
    the harness the phase builds anyway; the policy assertions are `IOptions` reads needing no
    database.
  - Tradeoff: A handful more tests in a phase whose stated point is the negative CI gate.
  - Confidence: HIGH — the plan identifies the invariant itself.
  - Blind spot: Whether the unique-index race also warrants an integration test. Probably not at
    this scale.
- **Decision**: FIXED — criteria 4.3 and 4.4, progress renumbered to 4.1–4.10.

### F8 — AddDefaultTokenProviders registers exactly the forbidden feature surface

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Lean Execution
- **Location**: Phase 3, change 1
- **Detail**: It registers the email-confirmation, phone and authenticator token providers — the
  exact surface `## What We're NOT Doing` forbids — in a plan whose central decision was to
  hand-write three pages rather than inherit the template's 47 files because absence beats deletion.
  Nothing in scope mints or consumes a token.
- **Fix**: Drop it from the contract; a later slice that needs a token provider adds one line.
- **Decision**: FIXED — removed, with a paragraph recording why it is absent so it is not restored
  as an omission.

### F9 — The az rest role-assignment check is not proven against a known-good case

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1, Automated Verification
- **Detail**: `lessons.md`, "Prove the check before trusting the result", records this exact failure
  from `F-02`: a wrong command is indistinguishable from a failed check. The criterion correctly
  ruled out `az role assignment` but nothing established that the `az rest` GET works before its
  empty result would be read as a verdict. A control already exists — `infra/main.bicep` declares
  `kvSecretsUser`, vault-scoped, for the same principal.
- **Fix**: Split the criterion — prove the command returns the existing `Key Vault Secrets User`
  assignment first, then assert the new `Key Vault Crypto User` one.
- **Decision**: FIXED — criteria 1.4 and 1.5.

### F10 — Password storage is correct by default but unasserted, and the post-Phase-1 threat model is unstated

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 1 overview; Phase 3 changes 1 and 5; Phase 4 change 5; Phase 6 change 2
- **Raised by**: the user, after the main triage — not by this review's own analysis.
- **Detail**: `ApplicationUser` inherits `PasswordHash` from `IdentityUser` and has no plaintext
  password property, so a password never reaches the entity; `AddIdentityCore` registers the default
  `PasswordHasher<T>` (PBKDF2-HMAC-SHA512, per-user salt, versioned stored format). **There is no
  design gap** — the mechanism is correct by construction and hard to get wrong, which is why
  neither the plan nor the first pass of this review flagged it.

  What was genuinely missing is that the plan never *states* any of it. The only mention of hashing
  anywhere was in `## Performance Considerations`, framed as a cost: "Identity's default work factor
  ... is not tuned here." That records it as a performance decision, never a security property, so
  (a) nothing asserted it, even though the plan had just gained criteria asserting configured policy
  against Identity's defaults precisely because inherited defaults drift silently; and (b) Phase 1's
  argument — "database read access becomes session forgery" — could be read as having closed
  database read access as a threat. It narrows it. The same access still returns every
  `AspNetUsers.PasswordHash` from Phase 2 onward, and key-ring encryption does nothing for those.

  The sixteen-character minimum is what makes that residual exposure acceptable, and that was the
  real security argument for a decision the plan justified only on usability grounds ("length beats
  composition", "a forgotten password is a permanently dead account").
- **Fix**: Three parts — (a) assert password storage alongside the F7 policy assertions: framework
  hasher, `IdentityV3` compatibility mode, iteration count pinned to the value observed at
  implementation time rather than one guessed by this plan, and a `PasswordHash` that is neither
  null nor the submitted password; (b) state in Phase 1's overview and the Phase 6 risk register
  what database read access still buys after encryption, and connect the sixteen-character minimum
  to it; (c) require `[DataType(DataType.Password)]` on the Register and Login form models.
  - Strength: No implementation changes — the mechanism was already right. This makes it verifiable
    and makes the reasoning survivable, in the same shape the plan already uses for the deployment
    trust boundary ("closing this row is not closing that category").
  - Tradeoff: Three more assertions and two paragraphs of prose.
  - Confidence: HIGH for the mechanism and the gap; the iteration-count default is deliberately not
    written into the plan, which is what the assertion is for.
  - Blind spot: None significant.
- **Decision**: FIXED — all three parts. Phase 1 overview gained the threat-model paragraph, Phase 3
  change 1 gained the "inherited, not configured — do not register a custom hasher" contract,
  change 5 gained the `DataType` requirement and the no-logging rule, Phase 4 change 5 gained the
  assertions (criterion 4.5), `## Testing Strategy` gained the matching unit-test line, and Phase 6
  change 2 gained the residual-risk record.

## Post-triage validation

Progress↔Phase contract re-checked mechanically after renumbering: one `## Progress` heading; all
six `## Phase N: <name>` headings matched by `### Phase N: <name>`; per-phase criteria counts equal
to progress-line counts (1: 9+6, 2: 8+2, 3: 8+7, 4: 9+2, 5: 8+2, 6: 5+3), re-run after F10; numbering contiguous from
`N.1` in every phase; zero checkbox bullets outside the Progress section.
