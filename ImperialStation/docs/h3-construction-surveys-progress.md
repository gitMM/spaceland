# H3 construction capacity correction

Verified 2026-10-29. This increment implements the later-contradictory-evidence path
for identified growth projects in R-AUT-06. It does not complete the first release.

## Behavior

`InspectConstruction` is a finite, interruptible local action. The actor must stand
at the managed project site or an adjacent completed tile. At completion it counts
the project's actual enabled ordinary beds, or observes zero when its output tile
does not exist. Inspection records a private `ConstructionProject/UsableBerths`
fact and a local observation with action, actor, project, time and causal identities.
It queues an ordinary report goal. Travel, login, posting and latency use the existing
bounded reporting planner and terminal protocol. Inspection itself changes no authority
capacity record. Cancellation produces neither an observation nor a report.

The authority accepts authenticated `ConstructionSurvey` claims for projects whose
original completion evidence it has already accepted. The claimed count must be within
the project's declared capacity, with an observation time between commitment and posting.
Unknown projects, surveys before original credit, invalid counts and malformed messages
are rejected. The receiver uses claims and transport metadata; it does not inspect beds,
tiles, materials or the sender's private observations. False surveys can deceive records.

Each project retains its original accepted completion and current believed berth count,
plus the message supporting that current count. Last received admissible evidence wins,
consistent with the existing authority belief policy, even if its observation is older.
Corrections update total believed capacity by the project's delta and request a growth
review. Pending capacity stays retired and the original material budget stays spent.
Neither physical capacity nor layout is changed. Zero usable berths does not establish
that a tile is absent. Different keys on duplicate original completions cannot recredit
a corrected project. Survey keys are deduplicated per authenticated account; conflicting
reuse is rejected and exact reuse changes nothing.

Snapshot schema **42** persists local observations, accepted survey identities and current
project evidence. Restore verifies evidence, deduplication, values, timing and active
inspection duration/locality. The schema intentionally rejects older checkpoints; replay
bundles must match their recorded build. Diagnostics include `inspect --view
construction-surveys`, project fields in `--view growth`, and the exported world snapshot.

## Verification

Release build: zero warnings or errors. Full suite: **785 passed, zero failures**, including
16 new survey cases. Result: `artifacts/test-results/h3-construction-surveys.trx`.

Tests cover absent falsely completed construction, real disabled beds, completion-time
sampling, delayed authority receipt, autonomous terminal return, cancellation, remote
inspection, invalid claims, duplicates and conflicting keys, false claims, older
observations received later, old completion reports after corrections, corrupted capacity
evidence, checkpoint boundaries and exact trace/state equivalence. Frequent getters,
incremental advancement and tracing on/off do not change state.

The runnable `scenarios/construction_capacity_correction.json` retains the default
autonomous builder and D-35 recipe. A controlled inspector posts an incorrect zero-capacity
claim, travels to the site and starts an inspection. Those four setup commands are explicit
trigger allowances. All reporting actions after the local inspection are autonomous.
The fixture is not a claim of autonomous audit dispatch.

- Tick 18525: physical habitat completes; actual capacity becomes 16.
- Tick 18538: original completion report is accepted; believed capacity becomes 16.
- Tick 19009: incorrect survey arrives; believed capacity becomes 8, actual remains 16.
- Tick 19200: local inspection observes eight usable new beds; believed total remains 8.
- Tick 19210: autonomous return report arrives; believed total becomes 16.

The builder and survey-return causal proofs pass. The survey proof starts at event 292,
checks seven actions, and follows events 294, 314 and 319. Replay reproduces state, trace
and proof output exactly.

```text
dotnet run --project src/Station.Runner -c Release --no-build -- run scenarios/construction_capacity_correction.json --trace detailed --out artifacts/construction-capacity-correction
dotnet run --project src/Station.Runner -c Release --no-build -- replay artifacts/construction-capacity-correction/replay.json --trace detailed --out artifacts/construction-capacity-correction-replay
dotnet run --project src/Station.Runner -c Release --no-build -- inspect artifacts/construction-capacity-correction/checkpoint.json --view construction-surveys
```

Build: `849208d99972597199a179cc4d1a6c5d321080a5905b524df29b8c3e09442cdc`.
Content: `9e5380ed7ab90c4b62a61eafe5e221204a223b1c40ad9e495b2ebea4063c4c87`.
State: `fb175ad72001dbcdd133c089f77cddc7e43e6a1c2cb081a6a4865504c94a009d`.
Trace SHA-256: `8400F9A5E832220BFF0152F7936A33F899E5B2985E8DBB6BADC09A8900BEE0D5`.
Proof SHA-256: `C3FF10F61433CBCA7746F7028E0F1C7D8AC783F996021A00F562940C682A05C7`.

## Remaining scope

Initial completed capacity still starts as an aggregate seed rather than identified
initial berth/tile records. This prevents claiming full R-AUT-01/R-AUT-06 closure.
Survey dispatch is not yet autonomous; surveys do not themselves settle uncredited
projects. Blocked/rejected construction follow-up and communication of new layout to
other workers remain open. Other H2/H3/H4 priority, institutional recovery, multiday,
performance and observability release gates remain open as recorded in earlier reports.
On 2026-10-29 the user revoked the shutdown instruction. Keep the computer running;
see `completion-instruction.md` for the current instruction.
