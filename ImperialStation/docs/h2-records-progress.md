# H2 scoped records: verified increment

The standalone system now has versioned indexed records, mutation idempotency keys,
finite permission-scoped record queries and shared query planning. It remains partial
H2; ordinary work beyond repair, remaining resource operations and H3/H4 are outstanding.

## Implemented behavior

- Credential, terminal-session, work-attempt and received-work-report revisions have
  typed fields and indexed current projections behind a scope-bound `IRecordStore`.
- Own-account queries work only through a local Records Terminal session. Cross-account
  work audit requires a matching actor/role/terminal/record-kind grant.
- Queries capture up to 32 rows at start and disclose only after finite completion,
  revalidating connection, session and credential. Cancellation, departure after logout,
  expiry, revocation and disconnection cannot disclose a pending result.
- Learned rows use the ordinary capped knowledge store with query provenance. One
  private receipt retains the cursor/version; it is not an unbounded row cache.
- Work records include administrative actions and received claims. Neither physical
  execution status nor private mailbox read markers enter the audit projection.
- GOAP terminal planning accepts typed record-query goals; autonomous institutional
  audit policy is not claimed. The audit fixture scripts the supervisor's diagnostic
  queries while the worker continues its autonomous order/repair/report cycle.

## Commands and evidence

Windows, SDK 9.0.311, .NET 8.0.15, Release. Build: zero warnings/errors. Full suite:
**130 passed, zero failed/skipped**. VSTest used the approved local IPC exception.

```powershell
dotnet test ImperialStation.sln --no-restore -m:1 -p:UseSharedCompilation=false -c Release --logger 'trx;LogFileName=h2-records.trx' --results-directory artifacts/test-results --verbosity minimal
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll validate scenarios/record_audit.json
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll run scenarios/record_audit.json --out artifacts/record-audit --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll replay artifacts/record-audit/replay.json --out artifacts/record-audit-replay --trace detailed
dotnet src/Station.Runner/bin/Release/net8.0/Station.Runner.dll inspect artifacts/record-audit/checkpoint.json --view query --actor 2
```

Run and replay completed with zero assertion failures, identical digest, 17 completed
actions and zero failed actions. Four finite queries completed at ticks 4, 16, 20 and 24.
The machine physically completes at tick 11; a query started at tick 13 still returns
the earlier Accepted record. Completion enters authority records at tick 16 and is
learned by the supervisor's next completed query at tick 20. The final query reveals
two received reports with their authentic account/terminal/time provenance.

| Identity | Value |
| --- | --- |
| Binary schema | 6 (JSON envelope 1) |
| Build | `174946ed5b05c240129945319e848fb72b112be4aa2dc62f8fddeffe4cb502e3` |
| Content | `86af7d426c9cb03b454ab51cb0a4f8e5ef933e5ebb8bfcf640e8c8ddf91f1ee0` |
| Run/replay digest | `4ce1674215592988f46a53ec4c0e84ae20ebb5d8dda239b677d46f8b31b6d424` |

Tests cover authorization/locality, capture timing, current/history pagination, account
separation, bounded eviction, false completion claims, duplicate administrative inputs,
strict query/grant loading and common planner/executor actions. Twenty timing variants
each pass seven checkpoint-continuation checks, including before tick zero and pending
queries. Prior job/repair/information/foundation tests continue to pass.

R-INS-01..05 now applies to the implemented H2 record types; report expectations,
incidents, disciplinary cases, detention and escort records belong to H3. No population
performance or browser visual verification is claimed. Historical job/repair reports
retain their original build evidence; their replay bundles need those original builds.
