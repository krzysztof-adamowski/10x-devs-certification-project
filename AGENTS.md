# Repository Guidelines

This repository holds a course workspace and the product built inside it. Read the file
that matches what you are doing.

- **Product code lives in `TenExCards/`.** Its rules are `@TenExCards/AGENTS.md` — the
  canonical project instructions.
- **`CLAUDE.md` at this root is mostly course curriculum.** Everything between the
  `<!-- BEGIN @przeprogramowani/10x-cli -->` and `<!-- END ... -->` markers is rewritten by
  the `10x` CLI on every lesson fetch — never edit inside it. Project rules can go anywhere
  outside the markers; the fetch preserves them and hoists them above the BEGIN marker.
- **`context/` holds the decision chain** the product was built from:
  `@context/foundation/prd.md`, `@context/foundation/tech-stack.md`, and
  `@context/changes/bootstrap-verification/verification.md`.
- **`context/archive/` is immutable.** Never write to it.

Keep agent sessions rooted here, not in `TenExCards/`, so the `context/` chain stays in scope.
