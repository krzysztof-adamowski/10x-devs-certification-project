# Repository Guidelines

10xCards is a `net10.0` C# web app that turns a passage the learner pastes into flashcard
candidates they triage one at a time. Product spec: `@../context/foundation/prd.md`.
Stack rationale: `@../context/foundation/tech-stack.md`.

## The scaffold is not the target

**Delete this whole section once Blazor Server and Identity are in place** — it describes a
transient state and goes stale the moment it is acted on.

This directory is still unmodified `dotnet new webapi` output — minimal APIs and a
`WeatherForecast` sample in `@Program.cs`. It does not reflect the chosen architecture.
Delete the sample when you first touch it, and build toward:

- **Blazor Server** for the UI — not minimal APIs, not a separate SPA. One project, no
  hand-maintained API contract.
- **ASP.NET Core Identity** for email + password accounts. Not scaffolded.
- An **LLM client** for generation. Not scaffolded; no package or configuration exists.

Adding the above is expected work, not scope creep. **Persistence is undecided** — EF Core
is named in the stack rationale only as an ASP.NET Core ecosystem strength, and no database
provider was ever chosen. Ask before picking one.

## Never do these

- **Never persist a submitted passage.** It is unrecoverable once its candidates exist.
- **Never persist untriaged candidates, or add batch resume.** They are discarded when the
  session ends.
- **Never add password recovery.** A forgotten password is a dead account in v1.
- **Never add roles, sharing, admin views, decks, tags, or export.** The user model is flat;
  scope every query to the owning account.
- **Never let the form freeze.** Acknowledge a submission within 2s with continuous visible
  progress; generation is bounded at 30s.

Each is a recorded decision, not an omission — see `## Non-Goals` and `## Non-Functional
Requirements` in `@../context/foundation/prd.md`.

## Working in this directory

Agent sessions are rooted at the repo root, so `dotnet` needs `TenExCards/` — `cd` here
or pass `--project TenExCards/TenExCards.csproj`. There is no test project; add one
before the first feature that touches generation, triage, or persistence.

## Conventions

Card quality is the product, and it is specified rather than delegated: read
`## Business Logic` in `@../context/foundation/prd.md` before touching generation or triage.
A candidate must test one load-bearing claim, be reformulated rather than copied, admit one
defensible answer, and not duplicate another card in the set.

Commits so far track course milestones (`Completed M1L3`). Ask which convention to use for
product commits rather than extending that style.
