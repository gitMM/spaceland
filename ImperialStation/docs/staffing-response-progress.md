# Staffing, daily timing and response times — progress

Implemented 2026-10-29 for the [next step](acceptance-current.md#next-step--staffing-daily-timing-and-response-times)
requested after the 32-person log review. Layout and population stay fixed; expansion
and physical emergency alarms remain deferred. Snapshot schema is **47**.

Status: all six items are implemented with targeted controls and pass the 32-person
pressure gate, including exact state and continuation-event replay. A follow-up adds
[repair priority](#follow-up--repair-priority-and-spill-measurement) and measures
spills by their own units. The population check now uses **256 citizens** instead of
2,048 (user decision, 2026-10-29: 2,048 was overkill); its result is recorded in
[pressure-station.md](pressure-station.md#current-evidence).

## Item 6 — response-time measurement

`Station.Runner/ResponseTimes.cs` streams the trace and final inspections (measurement
only; it changes no simulation state, and the baseline digest was unchanged when it
was added). Each incident is divided into stages:

| Stage | From → to |
| --- | --- |
| Detection | physical incident → first local report goal by an authorized observer |
| Reporting | discovery → authority receipt of a report observed after the incident |
| Ordering | receipt → order posted (or the order already active at receipt) |
| Acknowledgment | order → authority receipt of the acknowledgment |
| Arrival | acknowledgment → responder physically at the target |
| Resolution | arrival → physical outcome (machine operational, tile clean, detained, recovered card) |

Detection and reporting are detection delay; ordering and acknowledgment are
institutional delay; arrival and resolution are physical. Posting, delivery, first read
and acknowledgment receipt are recorded separately for each order.
Categories: machine faults, spills, card losses, custody (authorization → detained)
and missing work reports (opened → patrol/replacement result).

Limits were declared before assessing the fixes, in configured hours (one hour =
Day / 24 = 1,500 ticks):

| Category | Detection | Reporting | Ordering | Ack | Arrival | Resolution | Total |
| --- | --- | --- | --- | --- | --- | --- | --- |
| MachineFault | 8 | 2 | 2 | 4 | 2 | 4 | 16 |
| Spill | 8 | 2 | 2 | 4 | 2 | 8 | 24 |
| Custody | 1 | 1 | 1 | 2 | 2 | 1 | 6 |
| CardLoss | 4 | 2 | 2 | 2 | 2 | 2 | 8 |
| MissingReport | 0 | 1 | 1 | 2 | 2 | 4 | 8 |

`response-times.json` reports median, 95th percentile and maximum per stage, unresolved
counts and ages, per-shift statistics (incident shift and whether within half an hour
of a boundary), guard coverage samples, and the cause of every missed target. Causes
come from the dispatch's own recorded reason (for example
`AwaitingKnownDetentionCapacity`) or from the responders' published duty and actual
sleep intervals (`ResponderOffDuty`, `ResponderAsleep`, `ReadButNotAcknowledged`),
plus `ReplacementTeam/<received outcome>` and `AcknowledgmentOverdue`.

Overnight control: the 32-person workload's second authorization arrives at tick
12,415, just after the misconduct cohort leaves duty. `PressureStationTests` now
requires zero guard team-gap samples, no unresolved incident, a named cause for every
miss, no custody acknowledgment miss, a resolved custody incident outside shift 0, and
that custody ordering is delayed only by known detention capacity.

## Item 1 — staggered guards

Declared redistribution in `PressureStation.Generator.cs`: each 64-person group converts
five former cleaners (`b+8..b+12`) into guards. Nine guards are staggered three per
cohort; duty length and population are unchanged. The 32-person station now has nine
guards and 20 cleaners (previously four guards on cohort 0 and 25 cleaners).
Escort teams require one configured hour of published duty remaining
(`MinimumRemainingDutyTicks`); ordinary repair, cleanup and patrol responders use the
same one-hour rule, so orders are not posted to a citizen about to go off duty.

Coverage is sampled every 1,000 ticks: a gap is an interval with no escort commitment
in which on-duty guards not patrolling cannot form a team. Gaps fell from 216 of 325
samples to **0**.

Consequences exposed and fixed:
- **Known capacity hold.** With prompt dispatch, a second team arrived while the
  single 32-person facility slot was occupied and waited at intake until a guard's
  need became critical. `AwaitKnownCapacity` (enabled for the pressure station) holds a
  team while the authority's own dispatches and receipts fill the declared capacity;
  a release receipt triggers the next review.
- **Handoff hold.** A subject could walk away during the 100-tick custody handoff.
  While co-located guards perform it, the subject's own autonomous movement is
  refused (external commands still move it). A derived index, rebuilt on restore,
  keeps the check cheap.
- **Stale repair belief.** A worker with an older "operational" belief completed a new
  repair order without inspecting it. An ordered repair now confirms the target
  locally when the belief predates the order.

## Item 3 — sleep and meal timing

- A daily sleep ends at the known wake time (the roster sleep window's close), so a
  late start or a restart after interruption cannot consume the pre-duty hour. Relief
  stays pro rata; the planned end is persisted per activity.
- Sleep inside an open sleep window is that window's sleep, even when the urgent-need
  planner chose it.
- Before sleeping, the citizen predicts hunger and bladder at the wake time from its
  own sleeping rates and relieves a need that would reach critical first.
- `routine-windows.json` classifies each miss (late eligibility, committed action,
  travel across closure, custody, higher priority) and counts retention violations:
  an activity admitted while its window was open but recorded as missed. The pressure
  gate requires zero.

32-person results: missed ordinary windows **189 → 12** (two late eligibilities, six
travel-across-closure meals, one custody, three sleeps begun only after a committed
move or meal ended); critical need interruptions **168 → 1**; 288 of 288 scheduled
rests complete; every citizen eats and sleeps each day.

## Item 5 — remembered terminal outages

A citizen at a terminal that is disabled or disconnected remembers the outage
(`TerminalOutageSnapshot`): first and last observation, count, backoff and retry tick.
Travel never clears it; only a later local observation of a working terminal does
(`TerminalRestorationObserved`). Reporting, check-ins and recovery avoid outages still
in their wait and try an eligible alternative at once after a first observation.
Audits stay bound to the terminal that grants their records permission. A failed
recheck doubles that outage's delay. With no eligible terminal, the agent waits for
its own backoff and the earliest due recheck; only the least-confirmed due outage is
rechecked, so citizens do not alternate between terminals known to be down.

`TerminalOutageTests` covers the alternative terminal, travel retention, local
restoration discovery, bounded rechecks and records permission. In the pressure
fixture both public terminals fail together; failed attempts at a missing service
(nearly all terminal attempts) fell from 472 to 428.

## Item 4 — cafeteria estimates

Citizens observe a cafeteria's visible meals and queue on arrival and when queueing,
and learn the wait they actually experienced. These are ordinary knowledge facts with
an observation tick and source; conversations may pass on a witnessed queue or an
empty counter with its age and provenance. Meal planning uses only fresh estimates
(queue: one configured hour; empty counter: six) and never reads live remote stock or
queues. Hysteresis prevents oscillation: an observed queue is not left for an unknown
alternative, and a known alternative must beat it by a whole meal. Each
reconsideration is traced; the pressure gate allows at most one per meal.

`CafeteriaEstimateTests` covers knowledge boundaries, a useful alternative, staleness,
the empty-counter rule, hysteresis and shareable provenance. The 32-person run makes no
reconsideration in the final build (an intermediate build made 8, all after district
2's counter ran out near tick 322,500). Cafeteria failures fell from 33 to 0 because
empty counters are now seen on arrival.

The same observation covers identical services sharing a tile. Beds, rest spots and
toilets on one tile were all chosen by lowest ID, so detainees queued at one bed beside
free ones and could miss a day's sleep at 2,048 people. A citizen now records the wait
it sees at each identical service it may use there, and a visibly occupied one loses
to a free neighbour (`VisiblyOccupiedIdenticalServiceOnTheSameTileIsNotChosen`).
Pre-existing and unchanged: nearly every citizen eats at district 2's cafeteria
(bedrooms sit beside it), which exhausts its 960 meals on day 9 in the baseline too.

## Item 2 — acknowledgment deadlines

`EscortDispatchPolicy.AcknowledgmentTicks` (two configured hours at the pressure
station) bounds the wait for every selected guard's received acknowledgment. At the
deadline the authority withdraws the team's orders with individual
`AcknowledgmentOverdue` notices and re-dispatches. It knows only posting and
received acknowledgments, never private reads. Guards who acknowledged stay eligible;
silent guards are excluded. Withdrawn orders no longer count as commitments. A late
acknowledgment is rejected without state change, and a guard who reads the withdrawal
drops its unsent acknowledgment. Inbox messages still never wake sleepers; overnight
coverage relies on awake staff.

`EscortAcknowledgmentTests` covers a silent team, reassignment to reserves, late
replies, reuse of an acknowledging guard, a disabled deadline and checkpoint
continuation. The 32-person run triggers no deadline: every team acknowledges within
1,212 ticks.

Known limitation: if both old guards accepted locally but one acknowledgment was never
delivered, the old team could still form before reading the withdrawal. The
facility's intake receipt then settles the authorization, and the newer team's search
ends without custody. No duplicate custody is possible, but the work is wasted.

## Follow-up — repair priority and spill measurement

**Repair priority.** `FacilityConfig.RepairPreemptsCleanup` (enabled at the pressure
station; default off). With no uncommitted qualified responder on duty, a reported
repair is ordered to the nearest responder whose only known commitment is cleanup
work (`DispatchCandidate.CleanupOnly`; repair, verification, construction, patrol and
escort commitments still exclude). The trace records `RepairPreemptedCleanup`. On the
worker side, a citizen holding an accepted, unblocked repair order does it before its
retained cleanup goal; the repair's completion, block or removal wakes the cleanup,
which resumes from the tile's observed state.

**Evidence hold.** Pre-emption first sent the misconduct subjects to repair the
machines they had sabotaged before the supervisor's quality verification, which then
found no discrepancy: no reprimands, no custody. A fault first observed within one
configured hour of a claimed repair whose verification is still open is evidence about
that claim, so the authority holds the repair (`AwaitingQualityVerification`) until
the verification result arrives. A later fault is a new fault and is dispatched at
once. (Holding on every open verification instead delayed a wave-2 repair for 30,000
ticks behind the single supervisor's queue.)

**Spill measurement.** A spill now resolves when its tile is back to its dirt level
just before the spill (its own units removed), not when the whole tile is clean. The
tile's older backlog remains covered by the recovery gate.

The population baseline's pinned digest changed only through the schema bump; every
counted result of the 128-citizen baseline is identical.

## 32-person evidence (this build)

All pressure gates pass, with exact state and continuation-event replay from tick 12,000
(digest `68251211…`). The backlog clears at tick 198,000 (previously 222,000 in this
step, 258,000 before it). Four reprimands, two releases; both released subjects resume
work. Missed ordinary windows: 10. Critical interruptions: 1. Maximum individual failed
actions: 26.

| Category | Total median | p95 | max | Before follow-up (max) | Original max | Misses (cause) |
| --- | --- | --- | --- | --- | --- | --- |
| Custody | 820 | 10,000 | 10,000 | 10,001 | 28,719 | 1 (AwaitingKnownDetentionCapacity) |
| MachineFault | 31,505 | 31,635 | 31,635 | 33,266 | 62,952 | 4 (order read only after the terminal outage and shift end) |
| MissingReport | 1,793 | 2,551 | 2,551 | 2,610 | 25,843 | 1 stage (responder off duty) |
| CardLoss | 12 | 6,761 | 6,761 | 6,761 | 6,498 | 0 |
| Spill | 20,181 | 115,085 | 115,085 | 138,537 | 159,203 | 1 total, 6 with a stage miss (reporting during the outage; responder asleep or off duty) |

Machine-fault ordering is now immediate (maximum wait 30,126 → 0 ticks). What remains:
- **Machine-fault acknowledgment.** Each fault wave's order is posted about 500 ticks
  before both public terminals fail for the rest of the shift (the declared outage), so
  the responder reads it only after duty ends and acknowledges it at the next shift.
  Ordinary work orders have no acknowledgment-deadline reassignment (escorts do, item
  2); adding one would move these orders to the next shift's responder.
- **Spills** still miss stage limits when the responder's shift ends mid-cleanup or
  the report waits for a terminal.

Commands from `ImperialStation/`:

```powershell
dotnet run --project src/Station.Runner -c Release -- benchmark pressure --count 32 --out artifacts/pressure-32-response
dotnet run --project src/Station.Runner -c Release --no-build -- benchmark pressure --count 256 --out artifacts/pressure-256
dotnet test ImperialStation.sln --no-restore -c Release -m:1 --filter "FullyQualifiedName~TerminalOutageTests|FullyQualifiedName~CafeteriaEstimateTests|FullyQualifiedName~EscortAcknowledgmentTests"
```
