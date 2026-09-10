---
change_id: persistence-spine
title: Persistence spine
status: implemented
created: 2026-09-08
updated: 2026-09-11
archived_at: null
---

## Notes

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->

### Phase 1 deviation: four vault secrets, not two

Plan change 6 specifies two secrets. **Four exist.** Approved during implementation on 2026-09-10.

| Secret | Purpose | Recoverable elsewhere? |
| --- | --- | --- |
| `sql-admin-password` | SQL server admin; the `@secure()` template parameter for every future deployment | no |
| `sql-connection-string` | What the deployed app consumes via the Key Vault reference | derivable from the admin password |
| `sql-dev-user-password` | The contained `tenexdev` database user created in change 8 | **no — only copy** |
| `sql-dev-connection-string` | What local development puts in user-secrets (Phase 2 change 4) | derivable from the above |

**Why the dev pair exists.** Change 8 mints the contained dev user in Phase 1; Phase 2 change 4
consumes it. The plan gave that credential no home in between, while criterion 1.10 requires no
scratch secret file to remain on disk. Deleting the file would have destroyed the only copy — a
contained user has no server-level login, so it cannot be recovered, only dropped and recreated.
The vault is the durable, outside-the-repo store already provisioned.

**Phase 4 obligations this creates:**

- `deploy-plan.md` and `AGENTS.md` must name **all four** secrets, not the plan's two.
- The teardown note must record that `sql-dev-user-password` is the only copy of a credential
  belonging to a data-plane object (the contained user) that no template recreates.
- Criterion 1.5 reads "Both vault secrets exist" and is under-specified against reality. Its phase
  block is read-only, so this note is the correction.
- Review finding **F3** is a consequence: the site's identity holds `Key Vault Secrets User` at
  *vault* scope, so the app can read the dev credentials too.

### Phase 3 note for `S-02`: progressive disclosure depends on async, it does not substitute for it

Raised while reviewing the Phase 3 measurements. `S-02` will build the passage-submission flow
against the PRD's 2 s acknowledgement budget and its "continuous visible progress" requirement, and
the obvious shape is a component that escalates — a spinner at once, "this is taking longer than
usual" after a delay, a cancel option as the 30 s generation bound approaches. That is a reasonable
design and is close to what the PRD already asks for. Three things about it are easy to get wrong:

- **It is not protection against a frozen UI.** The component can only render if the render loop is
  free. Sync-over-async (`.Result`, `.Wait()`) or CPU-bound work on the circuit stops *every* render
  including the progress message, so the affordance works precisely when things were already fine
  and fails precisely when it was needed. Awaiting genuinely async work is what prevents the freeze;
  the message is layered on top of that, never a substitute.
- **It requires interactivity.** A statically rendered page has no circuit and cannot push an update
  mid-request — the POST is one blocking round-trip. `/db-check` is deliberately static (see the
  comment in `DbCheck.razor`), which is why it shows nothing of the kind. `S-02`'s submission
  surface needs `@rendermode InteractiveServer` for this to be possible at all.
- **Cancellation must be plumbed end to end.** A cancel button cancels nothing unless a
  `CancellationToken` reaches the LLM call and EF Core. Abandoning a generation is otherwise just
  hiding it. Untriaged candidates are never persisted, so an abandoned generation leaves no state
  behind — that part is already safe by the product rules.

Acknowledge at *t=0* rather than at the escalation point: a first signal at 1.5 s technically fits
inside 2 s but spends nearly the whole budget before the user sees anything.

Measured room available to `S-02`: see `baseline.md` — ~1.86 s warm, ~1.55 s worst observed.

### Phase 2 finding: the Data Protection key ring persists UNENCRYPTED

`PersistKeysToDbContext<AppDbContext>()` stores the key ring in `DataProtectionKeys.Xml` as
**plaintext XML** (verified 2026-09-10: one row, `xml_len` 884, readable as-is). ASP.NET Core says
so at startup, but only on the boot that mints a key — never again:

```
warn: Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager[35]
      No XML encryptor configured. Key {effa75a4-...} may be persisted to storage in unencrypted form.
```

This warning is easy to miss and easy to confuse with the *keys-not-persisted* warning that
criterion 2.11 asks about. They are different: the persistence warning is gone (that was the goal),
and this encryption-at-rest warning took its place.

**Why it matters more in `S-01` than here.** Today the key ring only signs antiforgery tokens, so
database read access buys token forgery. Once `S-01` adds Identity, the *same* ring signs auth
cookies — the same access becomes session forgery for any account. Who holds that access:
the app's managed identity, the SQL admin, and (per review finding **F3**) anything holding the dev
credentials, since the role assignment is vault-scoped.

**Not fixed in F-02** — the mitigation is `ProtectKeysWithAzureKeyVault(...)` (or a certificate),
which means a key-vault *key* rather than a secret, another role assignment, and its own failure
mode on the boot path. That is real work and belongs with the change that makes the exposure
material.

**`S-01` obligation.** `AGENTS.md` will shortly say key persistence is handled and that `S-01`
verifies rather than implements it. That remains true for *persistence*. Encryption at rest is a
separate, open question that `S-01` owns — do not read the persistence bullet as covering it.

### Phase 1 findings to carry into Phase 4

- **The plan's Key Vault reference check command does not work.** Criterion 1.6 prescribes
  `az resource show --resource-type Microsoft.Web/sites/config --name tenexcards-ka/configreferences/appsettings`,
  which returns `Operation returned an invalid status 'Not Found'` — a *wrong command*, not an
  unresolved reference. The endpoint returns a **collection** (`value: [...]`), so `az resource show`
  cannot address it. The form that works:

  ```
  az rest --method get --url "https://management.azure.com/subscriptions/<sub>/resourceGroups/rg-tenexcards-plc/providers/Microsoft.Web/sites/tenexcards-ka/config/configreferences/appsettings?api-version=2023-12-01"
  ```

  This is the false negative the plan said would cost more here than anywhere else, and it is the
  command AGENTS.md edit (e) must record.

- **`az role assignment` is broken on this subscription.** Every command in that group
  (`create`, and even `az role definition list`) fails with
  `(MissingSubscription) The request did not have a subscription or a valid tenant level resource provider.`
  ARM itself is fine — the same operations succeed through `az rest` against
  `providers/Microsoft.Authorization/roleAssignments`. Worth recording so the next agent does not
  read this as a permissions problem.

- **`what-if` under-reports on `Microsoft.Web/sites`.** It predicted only the two known phantoms and
  did **not** predict `identity.type: SystemAssigned`, which was the one intended change and did land.
  The resource type is therefore unreliable in *both* directions: it invents changes that never happen
  and omits ones that do.

- **RBAC propagation was not observed.** Both `az keyvault secret set` calls succeeded on the first
  attempt, and the reference resolved on the first read after restart. The plan's predicted
  `Forbidden` window did not materialise — recorded as an observation, not as a reason to drop the
  retry.

### Phase 1 what-if reconciliation (criterion 1.13), 2026-09-10

Deployment `persistence-spine-p1`, `Succeeded`, Incremental. **No `- Delete` was predicted on any
resource in either what-if run.** Snapshots taken before and after with `az appservice plan show`,
`az webapp config show`, `az webapp config appsettings list` and `az webapp show`.

| Predicted (what-if) | Post-deploy diff | Verdict |
| --- | --- | --- |
| `Modify` site: `Create siteConfig.localMySqlEnabled: null -> false` | absent from diff | **phantom** (3rd confirmation) |
| `Modify` site: `Create siteConfig.netFrameworkVersion: null -> 'v4.6'` | absent from diff | **phantom** (3rd confirmation) |
| `NoChange` plan `asp-tenexcards-linux` | 0 differing keys | correct |
| `NoChange` `config/web`, `config/logs` | 0 differing keys | correct |
| 6 × `Create` (server, 2 databases, firewall rule, vault, role assignment) | all six exist and are Online/enabled | correct |
| *(not predicted)* | site gained `identity.type/principalId/tenantId`, `managedServiceIdentityId: 2016` | **intended — but not predicted; see below** |

**The identity addition was deliberate and is load-bearing — it is not a defect.** This change
declares `identity: { type: 'SystemAssigned' }` on the site precisely so the app has a principal
that can read the vault. The chain is: identity -> principal
`a3558bdf-861d-464a-a071-e279dfe12ba9` -> role assignment `4633458b` (Key Vault Secrets User) on
that principal -> `configreferences` reports `"identityType": "SystemAssigned"` and
`"status": "Resolved"`. Without it there is no Key Vault reference and no change.

What is defective is the *prediction*. what-if reported the site `Modify` carrying only the two
phantom lines and never mentioned identity at all. So `Microsoft.Web/sites` is unreliable in **both**
directions: it invents changes that do not happen and omits changes that do. A clean what-if on this
resource type is not evidence that nothing will change; only the post-deploy diff is. Recorded in
`infra/main.bicep` above the `site` resource.

`properties.freeOfferExpirationTime` — the one prediction that was real on 2026-08-31 — was already
`null` before this deployment, so it could not be cleared again. It was neither predicted nor changed.

`WEBSITE_HTTPLOGGING_RETENTION_DAYS` survived the deployment unchanged (app settings diff: 0 keys).
That is the `appSettings` omission doing exactly the job its comment block claims.

**Phantoms discovered on the new resource types** (from a second what-if run made *after* the deploy,
which is the only run where a create-path resource can show a modify-path phantom):

- `Microsoft.Sql/servers/databases` — `Modify sku.name: 'Standard' -> 'S0'`. The GET returns the
  *tier* in `sku.name`; the template declares the *service objective*. Permanent, and the most
  alarming-looking line the template produces.
- `Microsoft.Authorization/roleAssignments` — `Modify properties.principalId` showing the resolved
  GUID against the literal unevaluated `[reference(...)]` expression. Permanent.
- `Microsoft.Sql/servers` — `NoEffect properties.version`, and `Ignore` on the `master` database.
  Both self-labelled.
- `Microsoft.KeyVault/vaults` — **clean**, no phantoms. Any future line here deserves attention.

All four are written into `infra/main.bicep` as dated comment blocks, and all were characterised
against an **empty** database.

