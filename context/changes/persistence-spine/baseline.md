# Persistence spine — measured baseline

Measured 2026-09-10 against `https://tenexcards-ka.azurewebsites.net`, immediately after the F-02
deploy (deployment `10dd25fc-9cac-4c10-9f90-4f5c7b818786`). Companion to
`context/changes/blazor-server-shell/baseline.md`, which stays the single home for the F-01 figures
and deliberately excluded persistence.

## Conditions

- App Service B1 Linux, single worker, `polandcentral`. Always On enabled.
- Azure SQL **S0** (`sqldb-tenexcards`), same region, same resource group. Provisioned tier — not
  auto-pausing, so no resume latency is included or hidden here.
- **Warm instance**: three discarded requests to `/db-check` before timing began.
- Client is the development machine in Poland over domestic broadband, so every figure includes
  real network RTT. These are *not* server-side timings.
- `curl -w %{time_starttransfer}` — time to first byte, seven samples per row.

## Results

| Request | What it does | Min | Median | Max |
| --- | --- | --- | --- | --- |
| `GET /` | no database access at all | 0.077 s | **0.085 s** | 0.094 s |
| `GET /db-check` | 2 reads (`COUNT`, `MAX` by timestamp) | 0.107 s | **0.114 s** | 0.148 s |
| `POST /db-check` | 1 write + 2 reads, then re-render | 0.109 s | **0.137 s** | 0.452 s |

### Derived cost of the database

Read against the **same-session** `GET /` control, not against the F-01 figure — see the caveat
below, which is the point of measuring a control at all.

- **Two reads: ~29 ms** (0.114 − 0.085)
- **One write plus two reads: ~52 ms** (0.137 − 0.085)

### The 0.452 s outlier is the cold connection pool

The first `POST` of the run took 0.452 s; the following four averaged 0.130 s. That first request
paid for establishing the SQL connection and its TLS handshake. It is reported rather than discarded
because it is what a real user meets after an idle period, and it is the single largest
database-attributable number on this page. It still leaves ~1.5 s of the 2 s acknowledgement budget.

## Against the 2-second acknowledgement budget

The PRD requires a submission to be acknowledged within 2 s. On this evidence:

| | |
| --- | --- |
| Warm page with database round-trip | ~0.14 s |
| Worst observed, cold connection pool | ~0.45 s |
| **Remaining budget for an LLM call (`S-02`)** | **~1.55 s worst case, ~1.86 s warm** |

The database is not the constraint. `S-02`'s LLM call is, and it inherits the numbers above rather
than an assumption.

## What a blown budget actually looks like

Reporting a 0.45 s figure "against the acknowledgement budget" invites the wrong mental picture, so
be precise about the failure mode the budget exists to prevent.

**It is a frozen form, not a "Reconnecting to server" modal.** The reconnect modal fires on circuit
*disconnection*; slowness does not disconnect a circuit. A slow server-side handler leaves the
circuit perfectly healthy and simply blocks that user's render loop, so the UI sits there looking
dead. What does produce the modal is the other hazard `TenExCards/AGENTS.md` records — OOM restarts
from circuit memory pressure, deploys, platform recycles — not query latency.

**`/db-check` cannot show that modal at all.** Verified 2026-09-10 by counting Blazor's interactive
component markers in the served HTML:

| Page | Interactive component markers | Circuit |
| --- | --- | --- |
| `/db-check` | 0 | none — statically rendered |
| `/circuit-check` | 2 (1 server) | yes |

`blazor.web.js` loads on every page and the modal markup lives in `MainLayout`, so both are present
in the source of both pages. Neither implies a circuit. With zero interactive components there is
nothing to reconnect to, and the 0.45 s is an ordinary slow page load.

**Not measured: the indirect path.** Under enough *concurrent* blocking database calls, thread-pool
starvation could delay SignalR keepalives far enough to trip the client's timeout — which would show
the modal, caused indirectly by query latency. Every figure on this page is a single sequential
client, so nothing here speaks to it. See "Concurrency" below.

## Caveat: do NOT subtract these from the F-01 baseline

`blazor-server-shell/baseline.md` records a **~0.152 s** median TTFB for `GET /` with no database.
This session measured **0.085 s** for the same request — *faster* than the recorded floor, despite
the app now doing strictly more work at startup.

So the two runs are not comparable, and the difference is a property of the measurement conditions
(network path, time of day, instance placement), not of the code. Subtracting 0.152 from 0.114 would
produce a *negative* database cost, which is the tell. That is exactly why the table above includes
a same-session `GET /` control: **the delta is only meaningful within one run.**

If a future change wants to compare against F-01, re-measure both in one session.

## What is not measured here

- **Cold start after a container restart.** Always On means this is paid on deploy or recycle rather
  than by a user, and `Database.Migrate()` runs on that path — the deploy took 93 s to report
  `Site started successfully`, of which the migration was ~0.8 s (`20:36:11.36` → `20:36:12.16` in
  the startup log). The rest is container start, not persistence.
- **Concurrency.** Single sequential client. Nothing here says what S0's 10 DTUs do under load, and
  ARR affinity is untested with cookies disabled (see `TenExCards/AGENTS.md`).
- **Anything with an LLM call in it.** `S-02` owns that measurement.
