---
change_id: deploy-pipeline
title: Merges deploy themselves
status: implementing
created: 2026-09-08
updated: 2026-09-10
archived_at: null
---

## Notes

Roadmap `F-03`. Prerequisite `F-01` (`blazor-server-shell`) is complete and deployed.

Carries forward finding `F4` from `../blazor-server-shell/reviews/impl-review.md` — the archive
shape rule exists only as prose in `TenExCards/AGENTS.md`, with a failure mode that deploys
successfully and then serves a page whose every asset 404s. This change makes it executable.
