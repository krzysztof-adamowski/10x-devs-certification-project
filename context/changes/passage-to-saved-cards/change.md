---
change_id: passage-to-saved-cards
title: Passage to saved cards
status: implementing
created: 2026-09-12
updated: 2026-09-13
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

### Phase 2 implementation findings, measured 2026-09-13

**The free tier allows 20 requests per DAY for `gemini-3.8-flash`, and the quota is per model.**
Read from the `429` body rather than inferred:

```
quotaId:    GenerateRequestsPerDayPerProjectPerModel-FreeTier
quotaValue: 20
model:      gemini-3.8-flash
```

Confirmed per-model by calling `gemini-3.6-flash` and `gemini-3.5-flash` while `3.8` was exhausted:
both returned `200`. So changing `Gemini:Model` buys a fresh 20, and the plan's decision to keep the
model in configuration is what makes that a setting change.

**Every full Flash model carries the same 20/day; only Flash Lite is different.** Read from AI
Studio's rate-limit dashboard on 2026-09-13, which is the only place these numbers exist now — the
public rate-limits page no longer publishes a table and defers to the per-account dashboard.

| Model | RPM | TPM | RPD |
| --- | --- | --- | --- |
| `gemini-3.8-flash` (shipping) | 5 | 250K | **20** |
| `gemini-3.7-flash`, `gemini-3.6-flash`, `gemini-3.5-flash`, `gemini-3-flash` | 5 | 250K | **20** |
| `gemini-3.5-flash-lite` | 15 | 250K | **500** |
| `gemini-3.1-flash-lite` | 15 | 250K | **500** |
| `gemini-2.5-flash-lite` | 10 | 250K | 20 |

So switching between full Flash models buys another bucket of 20 and nothing more; only the Lite
family is materially more generous. `RPD` is the sole binding constraint — 250K TPM is far beyond
what a 12,000-character passage can spend, and 5 RPM is ample for one learner at a time. Model
strings confirmed against `GET /v1beta/openai/models`, which does not consume generation quota.

**The dashboard is trustworthy here, unlike the model picker, and that distinction was checked
rather than assumed.** Two reasons. The account holds exactly one AI Studio project
(`gen-lang-client-0965947024`, "Default Gemini Project"), so there is no second project whose
numbers could be showing instead. And the one figure that can be cross-checked agrees from both
directions: the dashboard reads 20 RPD for Gemini 3.8 Flash, and the API's own `429` body
independently reported `quotaValue: 20`. Google also names that page as the API's reporting surface
in the error itself. This is a different surface from the playground model picker recorded above,
which *did* disagree with the API — feature gating in the UI is not key entitlement.

Note the dashboard's usage column **lags**: it read `0 / 20` for `gemini-3.8-flash` minutes after
that quota was exhausted. Its header says peak usage over 28 days. Trust it for limits, not for
what is left today.

**Two things about this quota mislead, and both cost time here.**

- **The error tells you to retry in one second.** The body carries `"Please retry in 1.124106005s"`
  and `"retryDelay": "1s"` while the violated quota is per *day*. Following that advice produces an
  endless retry loop against a limit that will not move until tomorrow.
- **It looks exactly like a broken client.** `ProviderError`, zero chunks, sub-second failure. What
  made it legible was noticing that every *successful* run happened to follow a rebuild — the build
  was spending the seconds that made the earlier calls look spaced out. The first reading of the
  evidence, recorded here because it was wrong, was "a per-minute limit that resets after a minute
  or two of idling". It is not; waiting 75 seconds changed nothing.

**Consequences for the remaining phases.** Twenty generations a day is tight for Phase 4, which needs
a maximum-length measurement, a durability check and a forced failure, all against the deployed
instance and all repeatable. Options, none of them taken here because the model string is a plan
decision: spend the quota carefully, point development at `gemini-3.6-flash` while production keeps
`3.8`, or enable billing. Phase 4's criterion 4.9 — force a live generation failure — is now free:
exhausting the daily quota does it.

**The generator maps `429` to `ProviderError`, which retains the passage — correct, but the learner
sees "could not be reached".** For an exhausted daily quota the honest message is closer to "the
card generator is unavailable until tomorrow". A fifth `GenerationFailure` value would be the clean
fix and is beyond what the plan specifies, so it is **flagged, not taken**.

**`gemini-3.8-flash` streams the whole JSON payload in 7-8 chunks** for a 3-candidate set, so the
chunk count the page renders moves visibly rather than jumping from 0 to done.

**Card quality on the hand-run was good.** Three candidates from a passage on contained database
users: each reformulated rather than copied, each admitting one defensible answer, no overlap
between them, and all far inside the column bounds (prompts 95-130 characters, answers 93-118).

### Plan deviation: a model rotation, decided 2026-09-13

**The plan specifies a single `Gemini:Model` and a four-value `GenerationFailure`. Both changed, on
the user's explicit instruction, after the 20/day ceiling was measured.**

`Gemini:Models` is now an ordered list, tried in turn, falling through on `HTTP 429`:

| Order | Model | RPD |
| --- | --- | --- |
| 1 | `gemini-3.8-flash` | 20 |
| 2 | `gemini-3.5-flash-lite` | 500 |
| 3 | `gemini-3.1-flash-lite` | 500 |

1,020 generations a day. Quality-first ordering means the preferred model serves until its quota is
spent and the app then degrades instead of failing, so no development/production split is needed —
one configuration serves both. `gemini-3.1-flash-lite-preview` also accepts the request shape but is
absent from the rate-limit dashboard, so its quota is unknown and it was left out: an unverified
quota is not worth a rotation slot.

Only a `429` falls through. Any other error returns `ProviderError` immediately, because a genuine
fault would fail on the next model too and retrying would just multiply the latency.

**`GenerationFailure.QuotaExhausted` is the fifth value**, returned only when every model is spent.
Its message is deliberately technical — it names the models tried, the status code and the fact that
this is a chosen free-tier ceiling rather than a defect — because the only people who will see it
are the MVP's reviewers, and for them a diagnosis is more useful than an apology.

**Verified against the live API rather than reasoned about**: with `gemini-3.8-flash` exhausted, a
real generation fell through to `gemini-3.5-flash-lite` and returned three candidates in 2.4s.

**One bug this caught, worth knowing.** `.NET`'s configuration binder **appends** to an array
property's existing value rather than replacing it, so the seeded default `["gemini-3.8-flash"]`
survived binding and produced a four-entry rotation with `3.8` duplicated — a slot that would fall
through instantly and buy nothing. Nothing would have surfaced it at runtime. The default is now
empty and `Program.cs` validates the list eagerly, since the generator is a singleton and would
otherwise fail at the first submission rather than at boot. `GeminiConfigurationTests` asserts
distinctness, and that assertion failed on its first run, which is what found this.

**Card quality holds up on the Lite models.** The verification passage was written with bait the
prompt forbids — a vendor name, a year, a retention figure, a version number, and a sentence saying
those numbers are conventions rather than derivations. `gemini-3.5-flash-lite` carded none of them,
and picked the three load-bearing claims instead: the definition nuance (end state, not work done),
the causal link (the acknowledgement itself can be lost), and the distinction (at-least-once plus
idempotent processing is observationally exactly-once).

### The plan is wrong about `@Assets` in a code-behind, corrected 2026-09-13

Phase 3 §3 says the C# side must resolve the fingerprinted module path via `<ImportMap />` or an
injected `ResourceAssetCollection`, because "the `@Assets[…]` helper `ReconnectModal.razor` uses is
a Razor markup helper and is not available from `.razor.cs`". **That is false on `net10.0`, and the
route it recommends is the one that breaks.**

Measured by reflection rather than argued:

```
ComponentBase.Assets : Microsoft.AspNetCore.Components.ResourceAssetCollection
                       public=False  inject=False
Generate.Assets      : declaredOn=ComponentBase  inject=False
```

`Assets` is a **protected property on `ComponentBase`** that the framework populates itself. It
carries no `[Inject]` attribute and `ResourceAssetCollection` is **not a registered service**, so
`[Inject] ResourceAssetCollection` compiles cleanly and then throws at the first render of
`/generate`:

```
InvalidOperationException: Cannot provide a value for property 'AssetPaths' on type
'TenExCards.Components.Pages.Generate'. There is no registered service of type
'Microsoft.AspNetCore.Components.ResourceAssetCollection'.
```

Because a code-behind is a `partial` of the component class, it inherits that protected member — so
`Assets["Components/Pages/Generate.razor.js"]` simply works there, with no injection and no import
map lookup. That is what the code does now.

**The generalisable part**: this failed the way the plan predicted JS interop would fail, just one
layer earlier. A compile success said nothing, because the defect was a DI registration rather than
a type error. The check that settled it was reflection over the actual framework types, and it was
only trustworthy because the compile-only verification used to get there
(`dotnet msbuild -t:Compile`, needed because a running app locks the output exe) was first proven to
report a deliberately introduced error — `lessons.md`'s "Prove the check before trusting the result"
applied to a build target.

### The rotation was too narrow: Gemini returns 503, not only 429

Found on 2026-09-13 by the first real run of the page, which reported `ProviderError` after ~15
seconds with three models configured and two of them healthy.

```
gemini-3.8-flash       HTTP 503  2.0s  "This model is currently experiencing high demand."  UNAVAILABLE
gemini-3.5-flash-lite  OK        1.4s  9 frames, 5 candidates
gemini-3.1-flash-lite  OK        1.5s  10 frames, 5 candidates
```

The rotation fell through on `429` alone, so a `503` hit the broad `catch` and failed the whole
request **without trying the two models that would have answered**. A quota ceiling was the only
per-model failure anticipated when the rotation was written; an overloaded model is at least as
common and looks nothing like it.

Two fixes, both in `GeminiCardCandidateGenerator`:

- **Fall through on `429`, `404`, and any `5xx`** (`IsWorthTryingAnotherModel`). Those are faults in
  one model. A `400`, `401` or `403` is a fault in the request or the key and would fail identically
  on every model, so it stops the rotation rather than tripling the wait before saying so.
- **`RetryPolicy = new ClientRetryPolicy(maxRetries: 1)`**, down from the client's default of three.
  This is the other half of the ~15 seconds: the client was re-asking the *overloaded* model with
  backoff before giving up, which is backwards when a healthy model is one line down in the
  configuration. The rotation is the resilience strategy; the per-model retry only needs to absorb a
  single blip.

The `QuotaExhausted` message now distinguishes the two causes, since "your daily quota is spent"
and "every model is busy, try in a minute" call for different actions from the reader.

**What this says about the earlier diagnosis.** Some of the failures attributed to the daily quota
during Phase 2 were probably `503`s. The quota finding itself is unaffected — that one was read from
a `429` body naming `quotaValue: 20` — but "the generator is fine, the free tier is rate-limiting"
was too confident a conclusion from a `ProviderError` alone. The generator was in fact mapping two
different provider states onto one outcome, and it took the page's first real run to separate them.

### Blazor renders at an event handler's first yielding await, and that broke reject-last

Found on 2026-09-13 by manual testing: discarding the **last** candidate threw the generic Blazor
error UI. Accepting the last candidate did not, and discarding the first four then accepting the
fifth did not either.

`ComponentBase.HandleEventAsync` calls `StateHasChanged()` as soon as the handler's returned task is
found incomplete — that is, at the **first await that actually yields** — and once more when the
handler finishes. Awaits in between render nothing. So the two triage paths rendered at different
moments:

| Path | First yielding await | State when it rendered |
| --- | --- | --- |
| Accept | `ICardStore.SaveAsync` | candidate still in the list, stage `Triaging` — consistent |
| Reject | `SetUnloadWarningAsync` (JS interop) | list already emptied, stage still `Triaging` — **`Current` indexes `[0]` of an empty list** |

Reject performs no database write, so the interop call is its first yield. `AdvanceAsync` removed
the candidate, then awaited the interop, and only *afterwards* moved the stage to `Summary` — so the
render caught the one moment where the stage and the list disagreed.

The fix is ordering: move the stage **before** any await, so no intermediate render can observe an
inconsistent pair. The Triaging branch also now requires `_pending.Count > 0`, as a second line of
defence — an unhandled exception inside a circuit event handler takes the whole untriaged batch with
it, which is precisely what the return-don't-throw rule exists to avoid elsewhere.

**Two things worth carrying into `S-03`.** Any state a component mutates across an await must be
left consistent *before* that await, not after — and a path with no I/O is the dangerous one,
because its first yield lands somewhere unexpected. And the bug was evidence of something working:
it could only fire if the interop call genuinely yielded, which proves the `Generate.razor.js`
module import resolves. Had the import silently failed, `SetUnloadWarningAsync` would have returned
synchronously and the crash would never have appeared.

