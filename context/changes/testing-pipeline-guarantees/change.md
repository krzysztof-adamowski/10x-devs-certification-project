---
change_id: testing-pipeline-guarantees
title: Pipeline guarantees under test
status: implementing
created: 2026-09-13
updated: 2026-09-14
archived_at: null
---

## Notes

Rollout Phase 1 of `context/foundation/test-plan.md`: "Pipeline guarantees under test".

**Risks covered:** #1 (a request-pipeline change silently removes an authorization or
transport guarantee — the site still answers `200` and the guarantee is gone) and #5 (a
configuration or schema change makes the deployed container fail to boot, on a tier with no
slot to roll back to).

**Test types planned:** integration over the real pipeline, plus one assertion added to the
existing deploy-verify script.

**Risk response intent:**

- **#1** — prove that a change removing an authorization or transport guarantee makes a test
  red rather than a deploy green: specifically, that an asset served to an anonymous request
  is still an asset and not a sign-in page. Challenge "it returned `200`, so it works": the
  deploy gate follows redirects and asserts status, so a gated asset resolves
  `302 → sign-in → 200` and is recorded as a pass. **Content type is the oracle here, not
  status.**
- **#5** — prove that a setting or migration the deployed boot path requires is present
  before the merge that reads it, by a check itself proven capable of failing. Challenge "it
  passed locally": the test host runs in Development and loads the developer's secret store;
  CI has none and the deployed container has neither.

## Verification (2026-09-14)

Every assertion this change adds was made to fail deliberately before being trusted. A check
that cannot fail is worse than no check, and `lessons.md` records two instances here where a
green result proved nothing. Each row below was reverted immediately after it was observed.

| Assertion | Deliberate break | Observed |
|---|---|---|
| `RoutableEndpoints_MatchTheRecordedAuthorizationSplit` | `.AllowAnonymous()` added to the `AddInteractiveServerRenderMode()` builder | Failed, naming the nine patterns that crossed the line — and `EnhancedNavigation_ToGatedRoute_StillRedirectsToLogin` failed with it, since `/generate` became anonymous too |
| `StaticAssets_AreAnonymous` | `.AllowAnonymous()` removed from `MapStaticAssets()` | Failed, naming `Components/Layout/ReconnectModal.…razor.js` as the first gated asset |
| `StaticAssets_AreAnonymous` **control** | the `StaticAsset` metadata predicate misspelled | Failed on *"Expected assets not to be empty"* — the control fired rather than the check passing vacuously |
| `KeyIdentifier_Missing_InDevelopment_BootsAnyway` | its environment flipped to Production | Failed — so the control is genuinely controlled by the environment, not passing for an unrelated reason |
| `MissingDemand_FailsBootNamingItself` | the connection-string case given `Gemini:ApiKey`'s expected message | Failed, printing the connection-string guard's own message — the assertion pins the cause, not the exception type |
| `asset_rejection` | `text/html` changed to a media type nothing sends | Self-test failed: *"expected reject, got accept"* |
| `redirect_rejection` **control** | tightened to reject every same-host redirect | Self-test failed on both acceptance cases, including the `http→https` upgrade the 2026-09-10 review deliberately preserved |

**No Key Vault traffic.** The full suite was run with the user-secrets store moved aside — the
CI-shaped run — at 140/140 green, with no `vault.azure.net`, `ForbiddenByRbac` or
`error occurred while reading the key ring` line in the output. That negative is readable
because the positive was measured during planning: a Production host *given* a key identifier
produces all three, loudly.

**The deploy gate, against the live site.** `scripts/verify_deploy.py` passes, now logging each
asset's media type (`text/css`, `text/javascript`) and the root's
`Strict-Transport-Security: max-age=2592000` — the header that confirms the container still runs
Production. `--base-url http://…` still passes, logging the `http→https` upgrade rather than
failing it.

**The CI step list, read rather than the colour.** The plan's method was `S-01`'s — commit a
deliberate failure and read which steps were skipped. It was run against the **branch** via
`workflow_dispatch` rather than against `main`, because the workflow's dispatch trigger carries no
ref restriction while its federated credential is exact-match on `refs/heads/main`: a dispatch on
any other ref structurally cannot deploy, and the new step precedes `Azure login` by four steps
regardless.

`asset_rejection`'s `text/html` case broken, [run
34787351089](https://github.com/krzysztof-adamowski/10x-devs-certification-project/actions/runs/34787351089):

```
success   Set up .NET
failure   Verify the deploy scripts
skipped   Test
skipped   Learner journey (end-to-end, non-gating)
skipped   Publish
skipped   Pack and verify archive shape
skipped   Retain the archive
skipped   Azure login (OIDC)
skipped   Deploy
skipped   Verify the deployed site
```

Reverted, then the control, [run
34787414411](https://github.com/krzysztof-adamowski/10x-devs-certification-project/actions/runs/34787414411)
— which proves three things at once and is why it was worth running:

1. `Verify the deploy scripts` **succeeded**, so the red above was caused by the break rather than
   by the step being broken from the start.
2. `Test` **succeeded in CI**, where there are no user-secrets — the environment `BootPathGuardTests`
   was written for, and a stronger reading of that than the local secrets-moved-aside run.
3. `Azure login (OIDC)` **failed** with `AADSTS700213: No matching federated identity record`,
   naming the branch subject, with `Deploy` and `Verify the deployed site` skipped. `TenExCards/AGENTS.md`'s
   exact-match claim, confirmed by measurement rather than inherited — and the proof that neither
   dispatch could have reached production.
