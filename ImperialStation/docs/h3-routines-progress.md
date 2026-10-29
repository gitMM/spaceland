# H3 roster routines: verified increment

Implemented a configured station day, immutable cohort roster versions, institutional
publication through delayed messages, staggered shift-start check-ins, and physical
off-duty travel. Everything remains in the independent headless solution. This is
the first H3 scheduling increment; neither the H2 gate nor the complete release is
claimed finished. The full first-release goal remains active.

## Behavior and requirement coverage

| Requirement | Implemented clause and evidence |
| --- | --- |
| R-LIF-01..02 | Known roster version, cohort, duty availability, station day and scheduled boundaries; configured day length is reported in results. Needs and custody fields remain later work. |
| R-LIF-03 | Duty/off-duty boundaries, safe completion or movement-boundary cancellation, after-duty terminal visit and OffDuty placeholder at an explicitly known rest tile. Automatic role/facility/staffing-based template assignment and full windows remain outstanding. |
| R-LIF-04 | Versioned roster rows are institutional records. Initial knowledge is explicit; updates require completed terminal reads. Publication/delivery do not change private plans. |
| R-MSG-15..16 | Shift-start offset drawn from each actor's RNG stream; ordinary terminal capacity and finite login/read; periodic on-duty and after-duty visits. |
| R-KNW-08,12 / R-ARC-04 | Pinned known-version evidence and immutable known roster in IActorView; pure getters; no latest-record lookup in citizen decisions. |
| R-ACT / R-RES | Rest travel uses the bounded planner and ordinary executor. Cancellation stops at the next arrival; carried items, finite work and retained repair goals remain real. |
| R-SNP / R-OBS | Schema 9 stores payloads, publication history, private routine state, RNG and watchdogs. Traces distinguish publication, reading, duty and activity changes. |

The default day is 864,000 ticks, with three cohorts starting at 0, 288,000 and
576,000, duty lasting 288,000 ticks, a check-in window of 18,000 ticks and periodic
on-duty visits every 36,000 ticks. Fixtures explicitly shorten these windows.
No day-level update scans every citizen; each actor has boundary/check-in events.

At shift end, movement requests ordinary cancellation and stops on the next tile.
Finite local work may finish. The citizen recognizes its known completed result,
retains pending goals and starts no further repair steps until duty resumes. Pending
reports and the after-duty check-in precede rest. If the check-in planner lacks a
credential or known route, no read is invented; the actor can still reach known rest.
No rest destination produces MissingRestDestination and releases any idle session.

Roster publication is idempotent and cannot be forged by citizen posts. A superseded
scheduled version cannot roll current records backward. Initial unknown rosters use
the same delayed transport as updates. A read started before delivery cannot capture
that message; it stays unknown until a later read. Cancelled reads, terminal failure
and credential revocation leave the message unread and the old schedule intact.

## Verification performed

Windows, SDK 9.0.311, .NET 8.0.15, Release. Build has zero warnings/errors. Full suite:
**181 passed, zero failed/skipped**, using the approved VSTest local IPC exception.

```powershell
dotnet test ImperialStation.sln --no-restore -m:1 -p:UseSharedCompilation=false -c Release --logger 'trx;LogFileName=h3-routines.trx' --results-directory artifacts/test-results --verbosity minimal
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll validate scenarios/roster_routines.json
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll run scenarios/roster_routines.json --out artifacts/roster_routines --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll replay artifacts/roster_routines/replay.json --out artifacts/roster_routines-replay --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/roster_routines/checkpoint.json --view routines
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/roster_routines/checkpoint.json --view rosters
```

The canonical fixture has six autonomous citizens, three cohorts and one terminal.
It runs for 130 ticks with a **60-tick configured day**, covering more than two such
days; this is not a 24-hour-day test. No citizen actions are scripted. One roster
revision is published while its recipient is resting and is learned on a later visit.

Results: all four assertions pass, **84 completed actions, zero failed actions, 28
completed check-ins**, maximum terminal queue 3, total terminal wait 45 ticks. Seven
roster versions exist; one revision message was posted and read. The replay has the
same final digest and no assertion failures.

| Identity | Value |
| --- | --- |
| Binary schema | 9 (JSON envelope 1) |
| Build | `34e697f0cfcf34cc55d9eb406003e22e0dc0af3e1d3ca64b9959fd12ed01c6ce` |
| Content | `b6e020444ad6913abadc98c13f2ee37b4f442db0972fe9efb923d194d40a63c6` |
| Digest | `c465d4a205d2079c628c73b825d7ff891a76c713a5d8ec6e93f90434ba274955` |

Twenty generated three-citizen worlds vary seed, cohort and publication time. Nine
checkpoints per case give **180 continuations**, with byte-identical round trips,
matching final digests and identical remaining traces. Tick-by-tick advancement with
frequent getters and tracing disabled matches one large advance. Independent runs
agree at concurrency 2/4/8. Exact tests cover wrapped day arithmetic, real rest travel,
missing facilities/cards, stopped movement, a repair spanning shift end, stale versions,
forged citizen posts, and failed/cancelled roster reads with mid-read continuation.

## Remaining release work

Automatic staffing/template assignment, real berth assignment/capacity, pre-duty and
break/meal/sleep windows, needs, sleep interruption and conversation are not implemented
by this placeholder. An OffDuty actor is not a simulated sleeper and gains no need relief.

Authority project planning/construction, dispatcher selection, adaptive expectations
with off-duty deadline extension, overdue incidents/damping, credential recovery,
discipline/patrol/escort/detention, task generators, population validation, tuning and
integration remain outstanding. Complete H2 and H3 gate audits are also still required.
Historical reports retain their original build/schema evidence; prior development
checkpoints require their original builds.
