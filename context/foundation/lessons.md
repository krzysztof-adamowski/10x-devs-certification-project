# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## Prove the check before trusting the result

- **Context**: Any phase that deploys `infra/main.bicep` or another ARM/Bicep template, and any
  success criterion expressed as a CLI verification command.
- **Problem**: On 2026-09-10 what-if predicted only the two known `Microsoft.Web/sites` phantoms and
  never mentioned the identity block that was the deployment's one intended change. Separately, the
  plan's own reference-resolution command returned `Not Found` because the endpoint is a collection
  — a wrong command is indistinguishable from a failed check.
- **Rule**: A clean what-if is not evidence that nothing changed — it omits real changes as readily
  as it invents phantom ones; snapshot, deploy and diff regardless. And never trust a verification
  command's output until the command itself is proven to run: a `Not Found` or empty result must be
  shown to succeed against a known-good case before it is read as a verdict.
- **Applies to**: plan, implement, impl-review

## Two changes in flight against one file: verify the staged diff, not the file

- **Context**: `infra/main.bicep`, during `deploy-pipeline` Phase 5 §6, while `persistence-spine`
  (F-02) was being implemented concurrently in another session.
- **Problem**: `deploy-pipeline`'s plan permits only a header-comment edit to `infra/main.bicep` and
  states the diff must be comment-only. At the same time the file held ~150 uncommitted lines of
  F-02 infrastructure — a SQL server, two databases, a Key Vault, an identity, a role assignment.
  Staging by path (`git add infra`) would have folded an entire other change's infrastructure into a
  `(p5)` commit, and the commit would have looked correct: right file, right phase, plausible
  message. Phase 1's own pre-flight asked for a clean tree, it was not clean, and nothing downstream
  re-checked.
- **Rule**: When a plan constrains a file's *diff* rather than merely naming the file, verify
  `git diff --cached -- <path>` against that constraint before committing. A path in the
  touched-file set authorises the file, never its current contents. The same applies to the
  pre-flight clean-tree check: it is worthless if only performed once, at the start.
- **Applies to**: implement, impl-review
