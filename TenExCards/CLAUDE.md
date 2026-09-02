@AGENTS.md

Claude Code import directives live here, not in `AGENTS.md`. `AGENTS.md` is read by other
agents too, so it refers to these files by plain relative path; the `@` form below is what
pre-loads them into a Claude Code session.

@../context/foundation/prd.md
@../context/foundation/tech-stack.md

`infrastructure.md` is deliberately **not** imported. It is mostly decision provenance — 90 lines
analysing platforms that were rejected — and an agent carrying that is likelier to reach for the
Docker-shaped solutions this platform was chosen to avoid. Its implementer-facing conclusions
live in `AGENTS.md`; open the file itself when the platform choice or the research is the
subject.
