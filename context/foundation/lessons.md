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

## Parallel sessions share one index: commit by path, verify the staged diff

- **Context**: Two `/10x-implement` sessions running concurrently in one worktree —
  `deploy-pipeline` (F-03) and `persistence-spine` (F-02) — on 2026-09-10. Expected to be the
  normal working mode, not an accident.
- **Problem**: Four distinct near-misses in one afternoon, none of which git reports as a conflict.
  (1) `infra/main.bicep` held ~150 uncommitted lines of F-02 infrastructure while `deploy-pipeline`'s
  plan permits only a header-comment edit there; `git add infra` would have folded an entire other
  change into a `(p5)` commit that looked correct — right file, right phase, plausible message.
  (2) `context/foundation/roadmap.md` carried both changes' status flips with the two table rows
  *adjacent in one hunk*, so the diff could not be split along change boundaries at all.
  (3) A file appeared in the index between one session's `git add` and its `git commit`: **`git add`
  followed by `git commit` is not atomic across sessions, because the index is a single shared
  file.** Unstaging the intruder would have been the obvious move and the wrong one — the other
  session was mid-ritual and its own commit would then have silently dropped that file.
  (4) The other session's commit swept up a `lessons.md` entry this session had appended seconds
  earlier, landing it under an unrelated scope. Nothing failed; the traceability just quietly went.
- **Rule**: Verify `git diff --cached -- <path>` against what the plan actually constrains. A path in
  the touched-file set authorises the file, never its current contents, and a pre-flight clean-tree
  check is worthless if performed only once at the start — re-check at commit time. Whenever another
  session may be live, prefer `git commit --only <paths>` over `git add` + `git commit`: it commits
  exactly the named paths and leaves the rest of the index untouched, so it can neither capture
  another session's staged work nor silently drop it. Never `git add -A` or `git add .`. Never
  `git restore --staged` a path you did not stage yourself. A file that legitimately carries both
  changes goes in its own **unscoped** `chore:` commit — committing it under either change's scope
  claims the other's work.
- **Applies to**: plan, implement, impl-review

## Verify a restart from the log, never from the first 200

- **Context**: Any check whose meaning depends on a process having actually restarted — restart-survival
  verification of Data Protection keys, auth cookies or caches — on App Service or any platform where a
  load balancer can keep serving the old instance.
- **Problem**: On 2026-09-12 (`accounts-and-sessions` phase 1) `az webapp restart` was followed by a poll
  returning `200` within one second — the old container still serving. The genuine `Application started`
  line appeared roughly two minutes later. Acting on that first `200` would have exercised a key ring that
  was never reloaded: the check passes while proving nothing, and "confirms" a persistence guarantee that
  was never actually tested.
- **Rule**: Never treat a successful HTTP response as evidence that a restart happened. Confirm it from the
  platform's own startup evidence — a fresh `Application started` line timestamped after the restart was
  issued — and only then perform the step whose validity depends on it.
- **Applies to**: plan, implement, impl-review
