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
