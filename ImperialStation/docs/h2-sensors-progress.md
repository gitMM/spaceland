# H2 perception and sensors: verified increment

Implemented content-configured passive/inspection-only observations and registered
wired machine-status sensors inside the independent `ImperialStation` solution.
This increment supplies evidence channels; H2 gate verification and H3–H4 authority,
institutional and daily-life systems remain unfinished. The first-release goal is active.

## Behavior and requirement coverage

| Requirement | Implemented evidence |
| --- | --- |
| R-KNW-04 | Resolved rules for supported local tile/item/machine facts; finite tile/item/machine inspections; cancellation reveals nothing; inspection provenance retained. |
| R-KNW-10 | Human Reported versus registered Sensor sources, observed/received times, immutable history, contradictions based on previous evidence, latest delivery/sequence projection. |
| R-KNW-11 | Explicit machine scope, sampling schedule, enable/disable state and wired authority channel; each payload contains only Operational. |
| R-KNW-12 | Getters preserve serialized bytes and do not trigger observations, messages or RNG consumption. |
| R-SNP/R-OBS | Schema 8 stores resolved rules, readings, schedules, in-flight delivery and provenance; typed trace IDs and CLI inspection expose the evidence. |

Sensors measure after physical completion in phase 3 and deliver their captured value
after positive latency in phase 4. Changes to truth in transit do not rewrite the sample.
Phase-1 disable prevents that tick's measurement; re-enable resumes the original interval
lattice. A packet already in flight still delivers. Citizen private knowledge and mailboxes
are untouched. A sensor cannot reveal required/installed resources, inventory or people.

`InspectTile` observes local location and traversal cost at finite completion. Existing
item inspection now consistently uses Inspection provenance for the complete description.
Own action effects/failures can still reveal action-specific facts. Rich item descriptions
currently require work-enabled content; unsupported perception types are rejected.

## Verification performed

Windows, SDK 9.0.311, .NET 8.0.15, Release. Build has zero warnings/errors. Full suite:
**165 passed, zero failed/skipped**. VSTest used the approved local IPC exception.

```powershell
dotnet test ImperialStation.sln --no-restore -m:1 -p:UseSharedCompilation=false -c Release --logger 'trx;LogFileName=h2-sensors.trx' --results-directory artifacts/test-results --verbosity minimal
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll validate scenarios/sensor_status.json
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll run scenarios/sensor_status.json --out artifacts/sensor_status --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll replay artifacts/sensor_status/replay.json --out artifacts/sensor_status-replay --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/sensor_status/checkpoint.json --view sensors
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/sensor_status/checkpoint.json --view perception
```

The canonical scenario passes all 15 assertions, completes three inspections with
zero failed actions, and delivers samples taken at ticks 0, 10 and 30 at ticks 5, 15
and 35. At tick 5 the authority receives a true reading although the machine has
already stopped. A human false claim arriving that same tick remains a separate
Reported history entry before the sensor delivery. A disabled second sensor discloses
nothing. The local inspector never learns the remote machine's status.

Run and replay complete with identical digests and no assertion failures:

| Identity | Value |
| --- | --- |
| Binary schema | 8 (JSON envelope 1) |
| Build | `223dc02575bce3cd09e61879bf6e37366ae11be13340559943fdd7895783dec0` |
| Content | `b03c99d473132448b86bc81f7169c63d07d1f9dd37b67c5f99eb46034e637eaa` |
| Digest | `9183b955f13b75c452485eb6f8e1fe0ff028414059dc057dcdb07476af2a2dec` |

Twenty generated configurations pass nine checkpoint continuations each (180 total),
with byte-identical round trips, matching final digests and identical remaining traces.
Separate runs agree at concurrency 2/4/8. Exact tests cover finite observation boundaries,
cancellation, scoped passive overrides, delivery ties, disabled sensors, in-flight packets,
invalid schedules/scopes, human source forgery rejection and pure getters. Overflow
and mid-tick event-cap failures remain serializable diagnostic worlds and cannot resume.

## Remaining work

This does not implement autonomous authority response to evidence, additional sensor
capabilities, construction/housing projects, rosters, adaptive deadlines, incidents,
credential recovery, discipline/escort/detention, daily needs or population benchmarks.
The full H2 requirement/gate audit and remaining planning/work domains are still needed.
Historical progress reports retain their original builds and snapshot versions; their
development checkpoints require those original builds.
