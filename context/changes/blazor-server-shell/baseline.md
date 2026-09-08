# Live baseline — Blazor Server shell on B1 Linux

Measured **2026-09-08** against `https://tenexcards-ka.azurewebsites.net/`, immediately after
the Phase 2 deploy of change `blazor-server-shell`.

This records what the *platform* costs before any application logic exists. It is a **floor**,
not a pass on the 2-second acknowledgement NFR — no generation call, no database, no auth and
no LLM round-trip exist yet. `S-02` inherits the remaining budget, not a verdict.

## Conditions

- App Service plan `asp-tenexcards-linux` — **B1 Linux**, region `polandcentral`, one worker.
- Always On is enabled, and the instance was **warm**: the smoke checks ran first, so no reading
  below includes a cold start. A cold start on B1 is materially slower and is not represented here.
- Measured from the local development machine over the public internet with `curl.exe`
  (the `.exe` is required — bare `curl` is an alias for `Invoke-WebRequest` in Windows
  PowerShell 5.1 and misparses `-o`/`-w`).
- The page under measurement is the static-rendered Home route. `/circuit-check` is the only
  interactive page in the app.

## HTTP timings for `/`

Seven consecutive warm requests, `curl.exe -o NUL -w "%{time_connect} %{time_starttransfer} %{time_total}"`:

| Metric | Min | Median | Max |
| --- | --- | --- | --- |
| TCP+TLS connect | 0.028 s | 0.044 s | 0.083 s |
| TTFB (`time_starttransfer`) | 0.109 s | **0.152 s** | 0.188 s |
| Total (`time_total`) | 0.110 s | **0.152 s** | 0.195 s |

TTFB and total are near-identical because the response body is a small static-rendered shell —
essentially all of the wall clock is time-to-first-byte, and none of it is transfer. Expect that
to stop being true as soon as a page returns real content.

Run 1 is the slowest of the seven (0.186 s) and its connect time is roughly double the rest;
subsequent runs benefit from connection reuse and a warmer path. The median is the honest figure.

## Circuit timings

These cannot be measured with `curl.exe` — establishing a `_blazor` WebSocket and timing a
server round-trip requires a real browser. Captured from browser devtools on the live site:

- **Navigation start → `_blazor` WebSocket open: 365 ms.** Read from the Firefox Network panel's
  `Start Time` column on the `_blazor?id=…` row, after a cache-bypassing reload (`Ctrl+Shift+R`)
  of `/circuit-check`. A cache-bypassing reload is the point: a warm-cache load makes this number
  look far better than a first visit, because the circuit cannot open until `blazor.web.js` has
  been fetched and run.
- **Round-trip latency of one `/circuit-check` click: 21 ms median** (min 20 ms, max 24 ms, five
  consecutive clicks). Measured in-page from `button.click()` to the DOM actually being patched,
  via a `MutationObserver` on the count element — so it covers the whole loop: SignalR frame out,
  server event handler, re-render, diff back, DOM patch. It is not visible in the Network panel,
  because frames inside an open WebSocket are not separate requests.

The gap between the two is the shape to remember: **establishing** the circuit costs ~365 ms of
the budget once, while **using** it costs ~21 ms per interaction. Interactivity on this platform
is cheap; getting to it is not.

## Reading these numbers later

The NFR that matters is "acknowledge a submission within 2s with continuous visible progress"
(`context/foundation/prd.md`, `## Non-Functional Requirements`). Against that budget, a ~0.15 s
TTFB for a trivial page is the cost of simply being on this platform at this size. What it does
**not** tell you: how long an LLM generation call takes, what a circuit holding a full passage
plus its candidates costs in memory, or how any of this behaves under more than one concurrent
user. Those are `S-02`'s measurements to take.

## Observed while measuring: blocked `disconnect` beacon

With uBlock Origin active, the `POST /disconnect` request Blazor fires on unload is blocked
(`Zablokowane przez: uBlock Origin` in the Network panel). That request exists to tell the server
a circuit is finished so it can be released immediately.

Nothing user-visible breaks. The consequence is that for any visitor running a content blocker,
circuits are **not** released proactively — they linger until the default 3-minute disconnected-
circuit retention expires. At today's size that is free: the app holds no application state and
the retained-circuit worst case is roughly 25 MB against B1's 1.75 GB.

It stops being free once a circuit holds a whole passage plus its candidates, which is exactly
what `S-02` builds and what `TenExCards/AGENTS.md` warns has no back-pressure. Recorded here as
an input to that slice's memory budgeting, not acted on now.

Referenced from `context/deployment/deploy-plan.md` rather than copied into it — this file is the
single home for these figures.
