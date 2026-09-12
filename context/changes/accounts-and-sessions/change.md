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
