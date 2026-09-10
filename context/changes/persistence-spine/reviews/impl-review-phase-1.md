<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Persistence Spine

- **Plan**: context/changes/persistence-spine/plan.md
- **Scope**: Phase 1 of 4
- **Date**: 2026-09-10
- **Commit**: 2df6bf7
- **Verdict**: NEEDS ATTENTION (triaged 2026-09-10: 7 fixed, 2 accepted, 1 skipped)
- **Findings**: 0 critical, 6 warnings, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

All 15 Phase 1 criteria were independently re-verified green during this review, including the Key
Vault reference reporting `Resolved`. Every literal contract term of change 3 checks out — notably
the two easiest to get wrong: `enablePurgeProtection` is genuinely absent rather than `false`, and
the role assignment name is genuinely `guid(...)`-derived.

## Findings

### F1 — Stale comment instructs the one thing AGENTS.md forbids

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: infra/main.bicep:232-233
- **Detail**: The appSettings block closes with "When Identity lands, move secrets to Key Vault
  references and add them above — that is the change this block is waiting for." The text is
  pre-existing, but THIS change made it live: `ConnectionStrings__DefaultConnection` now exists as a
  Key Vault reference, set out of band. A future agent reading the declared source of truth is told
  to move it into the template — which AGENTS.md `## Never do these` records as deleting the
  connection string on the next routine deploy, and which the same comment block argues against
  fourteen lines earlier.
- **Fix**: Rewrite the closing paragraph to state the opposite — the Key Vault reference has landed,
  stays outside the template, and is applied with `az webapp config appsettings set`.
  - Strength: Removes a direct contradiction between main.bicep and AGENTS.md; the template is the
    declared source of truth, so it should not point the other way.
  - Tradeoff: None material — a comment rewrite in a file already dense with such notes.
  - Confidence: HIGH — the prohibition is recorded verbatim in AGENTS.md and the mechanism is proven.
  - Blind spot: None significant.
- **Decision**: FIXED

### F2 — The four-vs-two secrets deviation exists only in the commit message

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: context/changes/persistence-spine/change.md
- **Detail**: Phase 1 change 6 specifies two vault secrets; four exist. The extra two
  (`sql-dev-user-password`, `sql-dev-connection-string`) were added by approved deviation so the
  contained dev user had a durable home. But grep over the change folder finds no mention — the
  deviation is recorded ONLY in the git commit body. Phase 4 writes AGENTS.md and deploy-plan.md
  from the plan and change.md, so the records will document two secrets while four exist, and the
  two undocumented ones hold the ONLY copy of the dev credential (the user is contained, with no
  server-level login, so it cannot otherwise be recovered). Criterion 1.5 ("Both vault secrets
  exist") is now under-specified against reality.
- **Fix**: Record the deviation in change.md's findings section, naming all four secrets and why the
  dev pair exists, so Phase 4's document edits pick it up.
  - Strength: change.md is what Phase 4 reads; git log is not. Closes the gap at its source.
  - Tradeoff: None — additive documentation.
  - Confidence: HIGH — verified by grep that nothing in the change folder mentions the dev secrets.
  - Blind spot: Does not fix criterion 1.5's wording, which sits in a read-only phase block.
- **Decision**: FIXED

### F3 — The app's identity can read the development credentials

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: infra/main.bicep:441-442
- **Detail**: `Key Vault Secrets User` is granted at vault scope. The site needs exactly one secret;
  the vault now holds four. The app's identity can therefore read `sql-dev-user-password` — the
  credential that main.bicep:55-59 says IS the boundary between the dev and app databases. This is a
  direct consequence of the F2 deviation and was not flagged when that deviation was proposed.
  Mitigating: the connection string the app legitimately reads already embeds the admin password, so
  `sql-admin-password` grants nothing extra; only the dev pair does.
- **Fix**: Scope the assignment to the single secret (`scope: vault::secret`) rather than the vault.
  - Strength: Least privilege; restores the boundary the template claims to establish.
  - Tradeoff: Adds a child secrets resource reference, and the secret must then exist before the role
    assignment — an ordering constraint the current shape avoids.
  - Confidence: MEDIUM — Key Vault RBAC supports secret-scoped assignments, but this template has
    not been deployed in that shape and the plan deliberately kept secret VALUES out of the template.
  - Blind spot: Whether declaring the secret resource without its value conflicts with the plan's
    "vault is infrastructure, values are data-plane" principle.
- **Decision**: ACCEPTED-AS-RISK

### F4 — Every redeploy re-asserts the SQL admin password

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: infra/main.bicep:291
- **Detail**: `administratorLoginPassword: sqlAdminPassword` is re-asserted on every deployment, and
  the parameter has no default, so every future run supplies a value at a prompt. If that value
  differs from the one embedded in the vault's `sql-connection-string` — a mistyped prompt, a second
  operator — the deployment reports Succeeded and silently rotates the admin password out from under
  the app. The app keeps working until its next restart, then fails with `Login failed`. This is
  structurally the same hazard the file already documents for appSettings, but it is undocumented
  here.
- **Fix**: Document it on the `sqlAdminPassword` parameter — supplying a value that does not match
  the vault's `sql-admin-password` rotates the admin silently, surfacing only on the next restart.
- **Decision**: FIXED

### F5 — API-version verification evidence exists for only one of three resource types

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: infra/main.bicep:287, 441
- **Detail**: Plan change 3 requires verifying each new type's API version with `az provider show`
  "rather than copying one from memory". Only the vault records that reasoning (:395-400, and it is
  exemplary — it explains choosing an older version the pinned Bicep can type-check over a newer one
  that would compile without property validation). `Microsoft.Sql/*@2025-01-01` and
  `roleAssignments@2022-04-01` carry no recorded evidence; 2022-04-01 is the canonical from-memory
  value. All three were in fact verified during implementation, but the evidence was not written
  down. Mitigating: all are stable, non-preview, and type-check cleanly.
- **Fix**: Add one line to the SQL server and role-assignment comments noting the version was
  confirmed against `az provider show` and is the newest stable.
- **Decision**: FIXED

### F6 — What-if characterisation contract only partially met

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: infra/main.bicep:316, 402, 421
- **Detail**: Plan change 5 requires a block per new resource type, dated, naming noise vs real, AND
  stating the characterisation was taken against an empty database. Verified: the "EMPTY database"
  statement appears on 2 of 4 blocks (:272, :330) and is absent from the vault (:402) and role
  assignment (:421) blocks. `Microsoft.Sql/servers/firewallRules` has no characterisation block at
  all — its comment covers only 0.0.0.0 semantics. Criterion 1.11's "three resource types" reading
  passes, but the contract's own wording does not.
- **Fix**: Add the empty-database clause to the vault and role-assignment blocks, and a one-line
  characterisation to the firewall rule.
- **Decision**: FIXED

### F7 — Criterion 1.6's command is unrunnable as written

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/persistence-spine/plan.md:377
- **Detail**: `az resource show --resource-type Microsoft.Web/sites/config --name
  tenexcards-ka/configreferences/appsettings` returns `Operation returned an invalid status 'Not
  Found'` — re-confirmed during this review. The endpoint returns a collection, so `az resource show`
  cannot address it. The criterion's INTENT was met via a corrected `az rest` call. Already recorded
  in change.md and generalised into lessons.md.
- **Fix**: None this phase — already queued for Phase 4 edit (e).
- **Decision**: SKIPPED

### F8 — Nothing enforces the "no parameters file in this repo" invariant

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: .gitignore
- **Detail**: main.bicep:68 states the password is "never written to a .parameters.json file inside
  this repo", but neither .gitignore nor TenExCards/.gitignore has a rule for `*.parameters.json`.
  Verified: infra/ contains only main.bicep. The invariant rests entirely on operator discipline.
- **Fix**: Add `infra/*.parameters.json` and `infra/*.parameters.local.json` to .gitignore —
  converts a comment into a mechanism.
- **Decision**: FIXED

### F9 — Unplanned additions to the template

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: infra/main.bicep:62-85, 451-455
- **Detail**: Three outputs (`sqlServerFqdn`, `vaultUri`, `sitePrincipalId`) and five naming params
  the plan did not ask for, plus explicit defaults (minimalTlsVersion, publicNetworkAccess,
  collation, zoneRedundant). All benign: no output exposes secret material (verified in compiled ARM
  — the secure parameter is referenced by no output), the params match the file's existing
  appName/planName convention, and collation is immutable after creation so pinning it is arguably
  load-bearing. Noted for the record, not for removal.
- **Fix**: None — accept as benign additions.
- **Decision**: ACCEPTED

### F10 — New resources omit the file's "deliberately not declared" convention

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: infra/main.bicep:287, 406
- **Detail**: Pre-existing blocks document deliberate omissions as first-class content (:209-213
  lists three not-declared properties with a reason each; :102-104 explains why tier is absent). The
  new resources have no equivalent. Absent and unexplained: Entra-ID-only auth on SQL (declined in
  plan.md, but the template is the declared source of truth), networkAcls on the vault, backup
  redundancy. Reasoning that lives only in the plan dies when the change is archived. Also the
  vault's `publicNetworkAccess: 'Enabled'` (:417) is the only security-relevant property in the
  change with no comment, while the SQL server's identical setting is explained at :267-268.
- **Fix**: Add a short "deliberately not declared" list to the SQL server and vault blocks, mirroring
  the style at :209-213.
- **Decision**: FIXED
