---
change_id: passage-to-saved-cards
title: Passage to saved cards
status: plan_reviewed
created: 2026-09-12
updated: 2026-09-12
---

## Notes

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->

### Roadmap Open Question 2 is resolved here

`context/foundation/roadmap.md` carries **"Which model provider generates the candidates?"** as Open
Roadmap Question 2, blocking `S-02` and marked non-blocking by the user's explicit decision. It is
answered during planning on 2026-09-12: **Google Gemini (`gemini-3.8-flash`) on the free tier**,
called through Gemini's OpenAI-compatible endpoint with the official `OpenAI` .NET package. Phase 5
writes the resolution back into the roadmap.

### The 30-second ceiling is a target, not a hard bound

The PRD's `## Non-Functional Requirements` states that a submission at the maximum accepted length
"either yields candidates or reports failure within 30 seconds". During planning the user recorded
that **this is a target to aim for and may be loosened if it proves unattainable** against the
chosen provider. The plan implements 30s as a configured value rather than a constant so that
loosening it is a setting change, not a code change — but loosening it in production amends a
stated non-functional requirement and belongs in the PRD, not only here.

### Provider pre-flight, measured 2026-09-12 against the real key

Run before Phase 2 rather than inside it, because the model string is a plan decision and the
streaming question gated the component's progress design. Four results, all from live calls with the
project's own AI Studio key on the free tier.

**1. `gemini-3.8-flash` is real and reachable.** `GET /v1beta/models` returned `200` and 55 models,
`gemini-3.8-flash` among them, alongside `gemini-3.7-flash` and `gemini-3.6-flash`. The plan's model
string needs no change.

**2. AI Studio's playground is not evidence about the API, and they disagree for this account.** The
Studio model picker shows neither `3.8` nor `3.7`, and badges `gemini-3.6-flash`, `gemini-3.5-flash`
and `gemini-3-flash-preview` as **Paid** with an "upgrade to unlock" prompt — while the pricing docs
list those same models as free-tier and the API serves them to this key. **The badge describes
playground access, not key access.** Do not conclude anything about what the application can call
from what the web UI offers; ask the `models` endpoint.

**3. JSON-schema structured output and streaming DO work together** on the OpenAI-compatible
endpoint — this settles the plan's recorded open question, and the non-streaming fallback is **not**
needed. `POST /v1beta/openai/chat/completions` with `stream: true` and
`response_format: {type: json_schema, strict: true}` returned `200`, five SSE `data:` frames (four
carrying content, then `[DONE]`), and the accumulated 345-character payload parsed as JSON matching
the schema exactly, with no keys beyond `candidates`. Per-field `maxLength` in the schema was
**accepted** — but accepted is not enforced, which is why `CandidateBounds.WithinColumnLimits`
remains the guarantee and the schema only the promise.

**4. Card quality on a single unprompted trial was good**, on the throwaway passage: two candidates,
each reformulated rather than copied, each admitting one defensible answer, no overlap between them.
One trial is not evidence of a distribution — but it is not the failure mode the prompt work exists
to prevent, either. Observed lengths were far inside the column bounds (prompts 107 and 119
characters, answers 25 and 18), which supports Phase 1 §2's framing of 500/1,000 as generous.

**Key format — this one corrects the plan.** Keys AI Studio issues now carry an **`AQ.`** prefix and
are 53 characters, not the 39-character `AIza…` form the documentation and every example still show.
Phase 2's leak-check criterion originally grepped for `AIza` alone and **would have missed this
project's actual key entirely**; it now covers both prefixes.
