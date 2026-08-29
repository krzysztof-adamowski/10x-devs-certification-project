---
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
---

## Why this stack

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
