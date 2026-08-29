---
bootstrapped_at: 2026-08-29T09:42:02Z
starter_id: dotnet
starter_name: ".NET (ASP.NET Core webapi)"
project_name: TenExCards
language_family: dotnet
package_manager: dotnet
cwd_strategy: subdir-then-move
project_root: TenExCards/
bootstrapper_confidence: verified
phase_3_status: ok
audit_command: "dotnet list package --vulnerable --include-transitive"
---

## Hand-off

Verbatim from `context/foundation/tech-stack.md`.

```yaml
starter_id: dotnet
package_manager: dotnet
project_name: TenExCards
hints:
  language_family: dotnet
  team_size: solo
  deployment_target: azure-app-service
  ci_provider: github-actions
  ci_default_flow: auto-deploy-on-merge
  bootstrapper_confidence: verified
  path_taken: standard
  quality_override: false
  self_check_answers: null
  has_auth: true
  has_payments: false
  has_realtime: false
  has_ai: true
  has_background_jobs: false
```

### Why this stack

A solo author shipping 10xCards after hours against a 3-week hard deadline needs
scaffolding that does not eat the budget. ASP.NET Core is the recommended default
for a .NET web app and the only vetted card in that family; it clears all four
agent-friendly gates — statically typed end to end, strongly convention-based
through DI, routing, and EF Core, well represented in .NET training data, and
documented on version-pinned Microsoft Learn pages. Bootstrapper confidence is
verified, so scaffolding should be smooth. One gap was surfaced and resolved in
conversation: the registered template is API-only, shipping neither a browser UI
nor authentication, both of which the PRD requires. The resolution is Blazor
Server with ASP.NET Core Identity — one C# project, no second toolchain or
hand-maintained API contract, and a persistent circuit that carries the
sub-2-second acknowledgement and continuous generation progress the
Non-Functional Requirements demand. Auth and AI flags are set; payments,
realtime, and background jobs are out of scope per the PRD's non-goals. Deploy is
Azure App Service, the stack default, with GitHub Actions auto-deploying on merge
to main.

## Pre-scaffold verification

| Signal      | Value   | Severity | Notes                                                                                     |
| ----------- | ------- | -------- | ----------------------------------------------------------------------------------------- |
| npm package | not run | n/a      | non-JS starter (`language_family: dotnet`); `cmd_template` invokes no npm-distributed CLI |
| GitHub repo | not run | n/a      | card `docs_url` is `https://learn.microsoft.com/aspnet/core`, not a GitHub URL             |

No recency signal was available for this starter. Neither check applies to a
first-party Microsoft SDK template, so the absence is expected rather than a
network or configuration failure. Proceeded with no warning.

Local toolchain observed at run time (context only, not part of the recency
slot): .NET SDK 10.0.400.

## Scaffold log

**Resolved invocation**: `dotnet new webapi -n TenExCards -o .bootstrap-scaffold --no-restore`
**Strategy**: subdir-then-move
**Exit code**: 0
**Files moved**: 6 (into `TenExCards/`, see Project directory layout below)
**Conflicts (.scaffold siblings)**: none
**.gitignore handling**: absent in scaffold (the ASP.NET Core webapi template ships
none), so no append-merge was needed. A pre-existing .NET `.gitignore` in the working
directory was relocated into `TenExCards/` with the project — see Project directory
layout below.
**.bootstrap-scaffold cleanup**: deleted
**Project root**: `TenExCards/`

### Project directory layout

The skill scaffolds into the working directory on the assumption that the working
directory *is* the project directory. That did not hold here: the working directory
is the course repository, carrying `.claude/`, `.agents/`, `CLAUDE.md`,
`skills-lock.json`, `idea-notes.md`, and the `context/` chain, under a git history
that is course work rather than product work.

Rather than interleave the two at the repository root, the scaffolded project was
placed in a dedicated `TenExCards/` subdirectory, preserving the template's internal
structure exactly. The repository root keeps the course and chain files; `TenExCards/`
holds the product. One repository, two clearly separated concerns.

Resulting layout:

```
10xDevs/                        <- repository root (course + bootstrap chain)
  .agents/  .claude/  CLAUDE.md  skills-lock.json  idea-notes.md
  context/                      <- chain artifacts, incl. this log
  TenExCards/                   <- the scaffolded product
    .gitignore
    Program.cs
    Properties/launchSettings.json
    TenExCards.csproj
    TenExCards.http
    appsettings.json
    appsettings.Development.json
```

The conflict matrix was unaffected by this: no scaffolded path collided with an
existing file at either level, so no `.scaffold` siblings were produced, and
`context/` was never a move target.

A `.gitignore` present in the working directory was the standard Visual Studio /
.NET ignore file (build results, `[Bb]in/`, `[Oo]bj/`, IDE caches). Being
project-scoped rather than course-scoped, it moved into `TenExCards/` with the rest
of the project. Its patterns are path-relative-agnostic, so build output stays
ignored at the new depth; `git status` shows the project as a single untracked
`TenExCards/` entry.

Verified after the move: `dotnet build` succeeds from `TenExCards/` (0 warnings,
0 errors) and the vulnerability audit re-run from the new location reports the same
clean result.

### Deviation from the card's substitution rule

`scaffold-merge.md` § Substitution rules replaces `{name}` with `.bootstrap-scaffold`
and states that `project_name` is not a substitution input, on the assumption that
`{name}` names only a directory. That assumption does not hold for `dotnet new`,
where `-n` sets the assembly, csproj, and root-namespace name. Executed literally,
`dotnet new webapi -n .bootstrap-scaffold` exits 0 — so the HARD-STOP path would not
catch it — while producing `.bootstrap-scaffold.csproj` with
`<RootNamespace>_bootstrap_scaffold</RootNamespace>`, silently discarding the
hand-off's `project_name`. This was verified by running the literal command in a
scratch directory before scaffolding.

The run therefore split the two concerns using the flags `dotnet new` provides for
exactly this: `-n TenExCards` for the project identity, `-o .bootstrap-scaffold` for
the temp directory. The temp-directory-then-move-up mechanic is unchanged; only the
naming is corrected. Result: `TenExCards.csproj` with the default `TenExCards` root
namespace. The user was asked and chose this over the literal invocation.

Registry maintainers may want to reflect this in the card's `cmd_template`
(`dotnet new webapi -n {project_name} -o {name} --no-restore`) or add a `dotnet`
entry to `bootstrapper-config.yaml`.

### Files moved

| File                                         | Resolution     |
| -------------------------------------------- | -------------- |
| `TenExCards/Program.cs`                      | moved silently |
| `TenExCards/Properties/launchSettings.json`  | moved silently |
| `TenExCards/TenExCards.csproj`               | moved silently |
| `TenExCards/TenExCards.http`                 | moved silently |
| `TenExCards/appsettings.Development.json`    | moved silently |
| `TenExCards/appsettings.json`                | moved silently |

No path under `context/` appeared in the scaffold, so the drop rule did not fire.
`context/` in the working directory was untouched. An empty `Properties/` directory
remained in `.bootstrap-scaffold/` after the move (its only file had already moved);
it was logged as leftover and removed with the temp directory.

## Post-scaffold audit

**Tool**: `dotnet list package --vulnerable --include-transitive`
**Summary**: 0 CRITICAL, 0 HIGH, 0 MODERATE, 0 LOW
**Direct vs transitive**: not distinguished by this tool in the no-findings case; `--include-transitive` was passed, so the transitive graph was in scope
**Exit code**: 0

Tool output:

```
The given project `TenExCards` has no vulnerable packages given the current sources.
Source consulted: https://api.nuget.org/v3/index.json
```

Clean tree. The scaffolded project carries a single direct package reference
(`Microsoft.AspNetCore.OpenApi` 10.0.11) on `net10.0`.

### Restore note

The card's `cmd_template` ends with `--no-restore`, so no restored assets existed
after the scaffold step and `dotnet list package --vulnerable` had nothing to read.
A `dotnet restore TenExCards.csproj` was run before the audit (exit 0). This is a
non-destructive step that materializes `obj/project.assets.json`; it installs no
project code and modifies no scaffolded file. Without it the audit would have
reported "failed to run" rather than a result.

Because a restore bakes absolute paths into `obj/`, the generated `obj/` was
discarded rather than relocated when the project moved into `TenExCards/`, then
regenerated in place. The audit reported above was re-run from `TenExCards/` after
the move and returned the same clean result; `bin/` and `obj/` at the new depth are
covered by the relocated `.gitignore`.

## Hints recorded but not acted on

| Hint                    | Value                |
| ----------------------- | -------------------- |
| bootstrapper_confidence | verified             |
| quality_override        | false                |
| path_taken              | standard             |
| self_check_answers      | null                 |
| team_size               | solo                 |
| deployment_target       | azure-app-service    |
| ci_provider             | github-actions       |
| ci_default_flow         | auto-deploy-on-merge |
| has_auth                | true                 |
| has_payments            | false                |
| has_realtime            | false                |
| has_ai                  | true                 |
| has_background_jobs     | false                |

v1 surfaces these but takes no action on them. Two carry unmet product
requirements that no scaffolded file addresses yet:

- `has_auth: true` — the webapi template ships no authentication. The hand-off
  rationale names ASP.NET Core Identity as the intended resolution.
- `has_ai: true` — no LLM client or configuration is scaffolded.

The hand-off body also records a UI decision (Blazor Server) that the API-only
template does not provide. None of this is a bootstrap failure; it is scope the
template never claimed to cover.

## Next steps

Next: a future skill will set up agent context (CLAUDE.md, AGENTS.md). For now, your project is scaffolded and verified — happy hacking.

Useful manual steps in the meantime:
- The repository already has a git history (course work). The project lives at `TenExCards/` and shows up as a single untracked entry, so it can be committed on its own.
- Run `dotnet` commands from `TenExCards/` (or pass `--project TenExCards/TenExCards.csproj` from the root) — the repository root is not a project directory.
- Keep agent sessions rooted at the repository root, not at `TenExCards/`. Every skill in the bootstrap chain addresses its files by relative path (`context/foundation/prd.md`, `context/foundation/tech-stack.md`), which resolve against the session's working directory. A session rooted at `TenExCards/` cannot see `context/`, so the PRD and this hand-off drop out of scope and the downstream skills refuse on a missing precondition. From the root, both the chain artifacts and the project source are in view.
- Review any `.scaffold` siblings the conflict policy created and decide which version of each file to keep. (This run created none.)
- Address audit findings per your project's risk tolerance — the full breakdown is in this log. (This run found none.)
- The template is API-only: authentication (`has_auth`) and the LLM client (`has_ai`) are unmet, and the Blazor Server decision recorded in the hand-off is not yet reflected in any scaffolded file.
