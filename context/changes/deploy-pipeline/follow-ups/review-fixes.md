# Follow-ups from the Phase 1–2 implementation review

Source: `../reviews/impl-review-phase-1-2.md` (2026-09-10).
Each item names the phase that must act on it. Findings fixed during triage are not repeated here.

## Phase 5 §6 — stage `infra/main.bicep` only after checking the staged diff

From **F5** (Scope Discipline, WARNING). Recorded as a recurring rule in
`context/foundation/lessons.md` under "Two changes in flight against one file".

The plan permits a **header-comment edit only** to `infra/main.bicep` in Phase 5 §6, and its
success criterion 5.4 reads "diff is comment-only". At the time of this review the file held
~150 uncommitted lines belonging to `persistence-spine` (F-02) — a SQL logical server, two
databases, a Key Vault, a system-assigned identity, a role assignment and three outputs — being
implemented concurrently in another session.

`git add infra/main.bicep` would therefore stage F-02's entire infrastructure alongside the
comment fix, and the resulting `(p5)` commit would look correct: right file, right phase,
plausible message. Criterion 5.4 would be satisfied only in the sense that nobody checked it.

**Before committing Phase 5, run this and read it — do not skip on the assumption the tree is
clean:**

```powershell
git add infra/main.bicep
git diff --cached -- infra/main.bicep
```

Every line in that output must be a comment line (`//`) or whitespace. If a `resource`, `param`,
`output` or `var` line appears, F-02's work is in the staging area:

```powershell
git restore --staged infra/main.bicep
```

Then either wait for F-02 to land its own commit, or commit Phase 5 without touching
`infra/main.bicep` and correct the header comment in a later change.

**Do not** resolve this by committing F-02's work under a `deploy-pipeline` scope. The two changes
have separate plans, separate phases and separate commit trailers; folding them together destroys
the traceability the `(p<N>)` convention exists to provide.

## Phase 5 — carry these corrections into the record

- **F6.** `scripts/pack.py`'s module docstring says a `publish/`-nested tree "violates assertions 1
  and 2 at once". It violates **1, 2 and 4** — nested entries are `publish/wwwroot/…`, which fail
  the `wwwroot/` assertion too. `scripts/README.md` already has this right. The plan's Phase 2 §1
  text and its criterion 2.4 carry the same understatement; correct them in the Phase 5 record
  rather than editing the plan's read-only phase blocks.
- **F7.** `scripts/README.md:8-10` states "CI calls these same two scripts with these same
  defaults". True from Phase 4 onward; there was no `.github/` when it was written. Once `deploy.yml`
  exists the sentence becomes accurate on its own — confirm it rather than reword it, and note in
  the deployment record that the README's CI-parity claim is now backed by a workflow.

## Deferred by decision, not oversight

- **F4** (SKIPPED at triage). `verify_deploy.py` proves the deployed files are served; it does not
  prove a Blazor circuit can be established. `_framework/blazor.web.js` is a static file and returns
  200 whether or not interactive server rendering works, and the root page is static-rendered, so a
  deploy with every circuit dead passes green. `POST /_blazor/negotiate?negotiateVersion=1` is
  mapped and reachable (a GET returns 405) if this is ever revisited. Deliberately out of scope
  here: the plan's contract for this script is the root page and its assets, and circuits carry no
  product behaviour until `S-01`.
- **F8, F9, F10** (OBSERVATIONS, deferred at triage). Asset requests get no retries while the root
  gets 120s; `zf.write()` / `BadZipFile` surface as tracebacks rather than the script's own `error:`
  style; `pack.py`'s `.replace("\\", "/")` would silently rewrite a POSIX filename containing a
  literal backslash, the one input that could make assertion 3 report a falsehood. None are live
  defects against `dotnet publish` output.

## Phase 5 — carry the 2026-09-10 OIDC subject collision into the record

Raised during Phase 4, not by the Phase 1–2 review. Three separate artefacts need it.

**1. `TenExCards/AGENTS.md`, `## Deployment`.** Add a lived, dated rule. Draft text:

> **Never hand-type a GitHub OIDC federated-credential subject.** This repository has
> `use_immutable_subject: true`, so GitHub presents
> `repo:<owner>@<owner_id>/<repo>@<repo_id>:ref:refs/heads/main` — not the name-based form its own
> documentation shows. A credential built from the documented form matches nothing, on any ref, and
> fails only at the first workflow run as `AADSTS700213`. Build the subject from
> `gh api repos/<owner>/<repo>/actions/oidc/customization/sub --jq .sub_claim_prefix`. Measured
> 2026-09-10.

This is a *lived* rule in the sense of [[proposed-ops-failure-criterion]] — keep it dated and do not
file it among the doc-derived ones.

**2. `context/deployment/deploy-plan.md`.** Covered by the extended criterion 5.11: which credential
is live, why `gh-main` is retained rather than deleted, and that OIDC does not close the
push-to-`main` trust boundary.

**3. The plan's Phase 3 block was corrected in place** (2026-09-10, at the user's explicit
direction — the second sanctioned exception to the read-only-phase-block rule in this change). The
contract, the command block, and manual criterion 3.7 now carry the correction. Phase 3's rows stay
`[x]` against `765e2bc`; the work was done correctly against a specification that was wrong.

**The reviewable point, if this is ever written up:** every Phase 3 criterion passed against a
credential that could not authenticate, because each one checked conformance to the plan and none
exchanged a token. Criterion 3.7 even predicted the symptom — "fails only at the first workflow run,
with an opaque error." A phase that provisions an identity should end by *using* it, not by
describing it.

## Phase 5 — two smaller record corrections found in Phase 4

- **Criterion 4.9's "~500 KB" is stale.** It predates `F-02`. The archive is now ~27.5 MB
  (77 entries), because `Microsoft.Data.SqlClient` ships MSAL native broker binaries for every RID
  — `linux-x64` alone is 36 MB uncompressed, and osx/win variants add ~15 MB more. Legitimate, not a
  packaging fault. Publishing with a `linux-x64` RID would cut it dramatically; that is a future
  change, deliberately not this one.
- **Node 20 deprecation.** `actions/checkout@v4`, `actions/setup-dotnet@v4`,
  `actions/upload-artifact@v4` and `azure/login@v2` are being force-run on Node 24 with a warning
  annotation. Nothing is broken. Version bumps are outside this change's stated scope
  (`## What We're NOT Doing` excludes action pinning); note it and move on.

## Corrupted command block repaired in the plan (2026-09-10)

Phase 5 §2's emergency restore block contained `"$env:TEMP` + a literal newline + `estore"` in two
places — a `` escape collapsed when the plan was written. Repaired to `"$env:TEMPestore"`.
Worth noting because criterion **5.12** asks whether the manual restore is reconstructable from the
docs alone, and until this was fixed the honest answer was no. Verify the block runs before ticking
5.12; do not read it and assume.
