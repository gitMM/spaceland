# H2 jobs and check-ins: verified increment

The independent solution now supports job/attempt identity, administrative reassignment,
private offline order copies, finite read/acceptance posting, suspension/resumption,
actual physical completion, delayed outcome reports and autonomous own-clock check-ins.
This is partial H2, not the first-release gate. The parent Unity project is unchanged.

## Verification performed

On Windows, SDK 9.0.311, .NET 8.0.15, Release:

```powershell
dotnet test ImperialStation.sln --no-restore -m:1 -p:UseSharedCompilation=false -c Release --logger 'trx;LogFileName=h2-jobs.trx' --results-directory artifacts/test-results --verbosity minimal
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll run scenarios/work_orders.json --out artifacts/work-orders --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll replay artifacts/work-orders/replay.json --out artifacts/work-orders-replay --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/work-orders/checkpoint.json --view attempts
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/work-orders/checkpoint.json --view work-history
```

114 tests passed, zero failed/skipped. Testhost needed the same approved local IPC
exception as previous increments. The autonomous CLI run and replay completed with
no assertion failures, 11 completed actions, zero failed actions, one physically
completed job, two completed check-ins and identical canonical digests.

| Identity | Value |
| --- | --- |
| Binary schema | 5 (JSON envelope 1) |
| Build | `5dd100a02c50ba7f0e6680423c068494a646a054c552982cae4dcf007856e6fa` |
| Content | `86af7d426c9cb03b454ab51cb0a4f8e5ef933e5ebb8bfcf640e8c8ddf91f1ee0` |
| Run/replay digest | `c5bb1b02493cdd73007ad0dd6bcf50d635fc050900fd8fe476a45475c93a9796` |

Tick 2: worker has read the order. Tick 4: acknowledgment is physically posted and
work begins. Tick 5: authority receives acceptance. Tick 11: installation physically
completes. Tick 16: authority receives completion. The latter never substitutes for
the physical transition. Tests also demonstrate a false known completion producing a
completion claim while the machine stays broken.

Other coverage includes unread-mail isolation, cancellation during read/post, offline
reassignment and late reports, suspend/resume claims and inventory, known expiry at
movement boundaries, authenticated-account validation, duplicate operation keys,
non-regressing acknowledgment state and snapshot continuation. Twenty generated
autonomous work-order cases each pass five continuation checkpoints; separate
distance variants cover seven checkpoints and 24 seeds match at concurrency 2/4/8.

## Requirement boundary

| Requirement | Implemented clauses / limits |
| --- | --- |
| R-RES-04..08 | Separate job truth/belief, attempt identity, read acknowledgment, suspend/resume. Jobs currently target machine repair. |
| R-MSG-15,18 | Periodic/work-result visits, report goals and offline order lifecycle. Roster staggering and awaiting-response cadence remain later work. |
| R-INS-01..04 | Indexed work attempts, identified history and idempotent assignment/cancellation/report operations. Access-filtered gameplay record queries remain outstanding. |
| R-SNP/R-OBS | Attempts, copies, pending policy/actions and keys persist; traces carry task/attempt identity. Viewer has no dedicated job presentation yet. |

Remaining release work includes record queries, resource split/merge/stashes and ordinary
work domains beyond repair; H3 evidence-based authority decisions, expectations with
off-duty extension, incidents, construction, rosters and institutions; H4 needs,
population benchmarks and tuning. Historical earlier reports describe their own build
and schema; their old replay bundles require those builds. No population performance
or browser visual verification is claimed by this increment.
