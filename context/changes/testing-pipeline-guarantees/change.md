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
