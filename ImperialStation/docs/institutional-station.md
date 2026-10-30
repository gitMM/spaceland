# Integrated institutional station

> **2026-10-30:** population runs now use **256 citizens**; 2,048-citizen runs are no
> longer used. 2,048 results below are historical evidence, not a current gate.

Milestone requested and planned on 2026-10-29: a station with specialized workers,
a supervisor and guards must complete a genuine institutional response while daily
life continues. This extends the earlier finite recovery workload. The first
release and the full T15/T20/T22 gates remain open.

## Plan recorded before implementation

1. Build a 32-citizen station using existing needs, rosters, work and institutions.
2. Perform the declared bad-installation/false-report trigger through shared actions;
   verify, audit, reprimand, authorize detention, prepare guards, search, escort,
   admit, serve the sentence, release and resume work through autonomous policies.
3. Exercise unavailable guards, full intake, terminal outages and an interrupted
   escort; require explained waiting and legitimate recovery or reassignment.
4. Check daily participation, finite resources, causal origins and checkpoint replay;
   fix exposed failures and add regressions.
5. Run the same generator at 256 citizens (2,048 before 2026-10-30) and report its actual scope and timing.

## Workload and evidence boundaries

`InstitutionalStation.Create` extends the daily generator. Actor 1 is a worker,
actor 2 a supervisor, actors 3–6 guards, and the remaining citizens cleaners. The
full-capacity control also makes actor 7 a worker. Professional roles must plan,
travel, read and satisfy daily needs; cleaners must perform physical work and post
reports. Idle guards are not required to fabricate reports or do cleaning.

The normal run has two initially correct machines. Eight declared commands, ending
at tick 12, log in, install two fitting but incorrectly rated cartridges, and post
accepted-assignment/completion claims. These are the only external action commands.
The first independently audited case creates the prior reprimand; the second adds
one reprimand and triggers the threshold-based detention request. No reprimands,
findings, guard responses, occupancy or release are seeded. The worker has autonomous
needs enabled; its subsequent needs, routines and work are autonomous.
The full-capacity control uses 16 trigger commands for two workers; single-case uses
four. Correct installation uses the same commands with the correct cartridge.

Days are explicitly shortened to 36,000 ticks at 100 ms/tick; physiological rates
are multiplied by 24 and routine service durations are scaled accordingly. Most
controls run two configured days. The interruption control runs three, because the
worker's next duty period begins after the second-day release. This is accelerated
fixture content, not a real-time wait or a claim of real-world calibration.
Login/post take one tick, record queries ten ticks, installation three ticks,
inspection/testing two ticks, and intake/release 100 ticks. A sentence lasts 15,000
ticks. Cafeteria/toilet/rest/bed services and meals are finite, including custody
services. All resources, durations and disturbances are exported in `scenario.json`
and `catalog.json`; there is no second simulation or canned response in the runner.

## Failures fixed

- **Audit starvation:** unrelated work exhausted the 64-page audit limit. Queries
  now filter by the inspected machine using recorded assignment metadata. Work
  reports carry that administrative machine ID. Account scope, assigned-terminal
  permissions, finite query actions, disclosure and retained-evidence limits remain
  enforced. The integrated audits take ten pages each even with population load.
- **Repeated completed work:** a late inbox read restarted a repair after the worker
  had already posted completion, potentially erasing the discrepancy. The local
  order now remembers that actor's authenticated submission. This changes neither
  physical machine state nor the authority's assessment of the claim.
- **Stranded interrupted escorts:** terminal reports previously ended at
  `NeedsAttention`. After receiving terminal outcomes from every assigned guard,
  the authority can issue a replacement to different available guards under the
  same authorization and expiry. Dispatch identities preserve both teams and their
  physical histories. No failed guard is reused for that authorization; unavailable
  replacements leave an explained wait. Late acknowledgments cannot undo that wait.
- **Social starvation:** a lonely citizen could find no consenting partner because
  everyone else had already relieved their own social need. An otherwise available
  autonomous off-duty citizen can now accept an invitation without also being at
  the social-seek threshold. Consent, locality, commitments and urgent needs still
  apply.

Snapshot format is **43**, adding machine query scope and escort dispatch identity.
Format-42 checkpoints must be replayed from their original scenarios with the new
build, or opened with their archived build. Restore validates the new evidence and
rejects a captured query whose machine filter has been changed. Existing tests and
golden scenario boundaries were retimed to the earlier genuine audit completion;
no artificial delay was added to preserve old timestamps.

## Acceptance checks

| Control | Required observed result |
| --- | --- |
| normal | Two audits/reprimands, one authorization, physical detention and release, then worker cleaning |
| correct | Correct physical installation; no reprimand or detention |
| single-case | One reprimand; no detention below the threshold |
| guards-unavailable | Explained staffing wait until the guards' next duty period; then release |
| terminal-outage | No records disclosed before connectivity returns at tick 8,000; then audited release |
| intake-outage | No intake before service restoration at tick 8,000; then a complete finite intake and release |
| full-capacity | Two actual institutional chains; second escort waits behind a real occupant; two non-overlapping admissions/releases |
| interrupted-escort | A temporary slow edge keeps the first team in transit until critical need interrupts it at the shared boundary; received reports lead to a different team, release, and work on the next shift |

Every control requires every citizen to eat and sleep each configured day and to
rest, use toilets and converse. Cleaners work each day. Scheduled rest goals must
have no overdue entries. Accounting and finite service capacities are checked.
All action origins and trace continuity are checked; protocol actions require the
appropriate custody, release or consent cause. The small runs additionally retain
and audit complete causal graphs for the worker, supervisor and four guards (seven
roles for full-capacity). Institutional records, inspections, assignments, terminal
receipts and physical custody history establish the specific chain.

The population run checks every action's origin/cause and protocol cause while
streaming the trace, rather than retaining a complete population graph. The focused
trace contains the institutional actors and institutional events; it is not a full
population event log. Checkpoints continue with a different chunk size and must
reach the same final digest. The interruption checkpoint includes interrupted
custody history before the replacement completes.

## Reproduce

From `ImperialStation/`:

```powershell
dotnet build src/Station.Runner -c Release -m:1
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll benchmark institutional --count 32 --out artifacts/institutional-32
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll benchmark institutional --count 32 --variant interrupted-escort --out artifacts/institutional-interrupted
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll benchmark institutional --count 256 --out artifacts/institutional-256
dotnet test tests/Station.Tests -c Release -m:1
```

Use the 32-citizen gate for ordinary iteration. Population checks and the complete
suite are separate verification steps; their times must not be confused with the
small gate. This two-day workload is also different from the earlier five-day,
large-disruption recovery benchmark, so their timings are not a speedup comparison.

## Remaining limits

This proves one institutional chain under 2,048-citizen background load; the small
full-capacity control proves two overlapping chains. It does not establish throughput
for hundreds of simultaneous audits or detentions. Replacement needs fresh qualified
guards and does not extend authorization or invent staffing. Only the declared
finite outage/slow-edge controls are covered. The population result is seed 42,
not a broad multi-seed campaign. Ordinary routine windows can still be missed even
when the per-day needs and scheduled-rest checks pass; that count is reported, not
silently treated as zero. Calibration, complete priority/window acceptance,
construction/growth follow-up, architecture separation and remaining release gates
stay on the acceptance matrix. No infiltration-roadmap feature is added here.

## Recorded verification

- **816 passed, zero failed, zero skipped:** [Release test result](../artifacts/institutional-tests/institutional-verified.trx). This includes all eight integration controls plus focused regression and corruption tests.
- All eight exported small controls pass on the verified build. Every checkpoint
  matches; all six causal role audits pass (seven for full capacity). Summaries are
  in [institutional-control-summary.json](../artifacts/institutional-control-summary.json).
- Normal 32-citizen execution: **1.963 seconds primary + 0.844 seconds continuation**.
  The worker completes 13 physical cleaning actions after release, two need
  services finish during custody, and all 64 scheduled rests complete. The runner
  still reports 48 missed ordinary routine windows; these are not overdue scheduled
  rests and are not erased from the evidence.
- The isolated executable and dependencies are preserved in
  [institutional-build-verified](../artifacts/institutional-build-verified).
  Build fingerprint: `39e4e51340a26ecaa18c08c36d72066b566e1c669884eb2d0f3c4c606f9627c4`.
- **2,048 citizens: completed, zero acceptance failures.** [Institutional result](../artifacts/institutional-2048-verified/institutional-result.json), [full participation](../artifacts/institutional-2048-verified/result.json), [institutional histories](../artifacts/institutional-2048-verified/institutions.json).
  The same two-day generator produces two reprimands, one authorization/release,
  two completed custody need services and 13 cleaning actions after release.
  All 4,096 scheduled rests complete, with zero overdue rests. Every citizen meets
  the declared per-day meals/sleep checks; 2,887 missed ordinary windows remain reported.
- Population primary execution: **124.528 s**;
  checkpoint continuation: **84.968 s**; complete command:
  **213.815 s** ([wall-time record](../artifacts/institutional-2048-verified-wall.json)).
  The run processes 9,153,104 events, has zero trace gaps or invalid action causes,
  and resumes its tick-6000 checkpoint to the identical final digest.
  Peak process working set is 465.2 MiB;
  cumulative allocated bytes are 51,505,941,176 (not live memory).
  Final population digest: `291edb7c11ac430c3ec61fee95d0543a01032edb7219ba88376e55ae0781ecd0`.
- The complete 32-citizen normal command, including full causal checks, checkpoint
  continuation and artifact export, takes **3.309 s**
  ([iteration timing](../artifacts/institutional-iteration-wall.json)); its digest
  matches the exported normal control. Build time is excluded.
- All eight small controls have identical digests across the final boundary-validation
  changes. The population digest also matches both earlier completed runs. Timing
  varies with concurrent activity; the final population measurement ran after the suite.

| Exported 32-citizen control | Reprimands | Authorizations / releases | Checkpoint |
| --- | ---: | ---: | --- |
| [normal](../artifacts/institutional-32-verified-normal/institutional-result.json) | 2 | 1 / 1 | match |
| [correct](../artifacts/institutional-32-verified-correct/institutional-result.json) | 0 | 0 / 0 | match |
| [single-case](../artifacts/institutional-32-verified-single-case/institutional-result.json) | 1 | 0 / 0 | match |
| [guards-unavailable](../artifacts/institutional-32-verified-guards-unavailable/institutional-result.json) | 2 | 1 / 1 | match |
| [terminal-outage](../artifacts/institutional-32-verified-terminal-outage/institutional-result.json) | 2 | 1 / 1 | match |
| [intake-outage](../artifacts/institutional-32-verified-intake-outage/institutional-result.json) | 2 | 1 / 1 | match |
| [full-capacity](../artifacts/institutional-32-verified-full-capacity/institutional-result.json) | 4 | 2 / 2 | match |
| [interrupted-escort](../artifacts/institutional-32-verified-interrupted-escort/institutional-result.json) | 2 | 1 / 1 | match |

Each artifact directory contains the exact scenario/catalog, participation metrics,
institutional result and histories, a focused trace and the institutional checkpoint.
The full small-run causal graph verdicts are in `response-chains.json`.
Timing excludes compilation; primary and continuation counters also exclude some
setup, serialization and final graph analysis. End-to-end wall time is measured
separately where reported.
