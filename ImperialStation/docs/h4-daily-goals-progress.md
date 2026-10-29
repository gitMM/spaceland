# Retained daily opportunities and due-time ordering — H4 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment adds roster-derived meal, sleep and mandatory rest opportunities,
retained private goals, and due-time ordering with required reports/check-ins.
It advances R-LIF-02..05, R-LIF-08, R-LIF-11..12 and T22 continuation/starvation
coverage. It does not complete the first release or the full daily-life gate.

## Calendar and private knowledge

DailyRoutine derives five opportunities exclusively from the citizen's known
RosterPayload. The default day supplies a pre-duty meal in the hour before duty,
a staggered duty meal near midshift, a staggered rest break near three-quarters
of the shift, an after-duty meal, and an eight-hour sleep window ending before
pre-duty. The existing known check-in offset deterministically staggers breaks.
Window fractions scale with the configured day. Physical action durations retain
their declared values; short-day fixtures must declare their own activity durations.
Daily-life calendars require at least 24 day ticks and four duty ticks.

Only an initially known roster or a completed roster read creates/replaces this
private calendar. Publication and mailbox delivery do not change it. Reading a
new version supersedes pending goals from the old version and establishes the
currently open/new future opportunities. Pre-duty now makes a terminal visit due,
in addition to the existing post-duty and periodic visits.

Meals require the exact hunger seek threshold and an open opportunity. The goal
waits when hunger is below seek; crossing during the window can wake it. An unused
meal window resolves Unnecessary when the meal is not needed, or MissedWindow when
it is needed but could not start. A finite activity admitted while the opportunity
is open may finish afterward. Late travel alone grants no service result.

Each shift creates a finite RestSpot goal independently of strain D-53. It retains
its original due tick through higher-priority work and may finish after the shift.
Sleep is eligible from its known window without requiring urgent sleep pressure.
It still requires an own assigned berth or permitted custody service. Reaching a
calendar boundary grants no relief. Finite sleep is not cut short by a mailbox or
routine polling; interruption preserves only the actual pro-rata benefit.

## Priority, retention and execution

Required reports receive a due tick and stable goal sequence when queued. Check-in
goals retain the tick/sequence of their first due trigger across further triggers.
Routine goals retain their original opportunity due tick. Eligible priority-5
goals use earliest due tick, then that stable sequence. Report FIFO heads retain
their established protocol ordering. A blocked service, route or check-in permits
other useful activity during its bounded retry interval.

Repair, other work orders, audits and off-duty travel consult due goals before
issuing actions. Deferred goals are not discarded on ordinary work completion.
Current urgent escort/patrol/reporting obligations remain above routine goals;
critical/urgent needs retain their preceding priority behavior. A blocked reporting
protocol may yield to useful relief rather than prohibiting it indefinitely.

Priority is checked again immediately before autonomous physical execution. This
fixes a real same-tick race: a work plan could be produced just before a check-in
became due, and competing requests previously produced ActorBusy failures. Stale
plans now defer, with an explicit PlanDeferred trace. Duplicate same-tick report
and check-in plans are suppressed. Need and daily commands participate in the
same seeded claim arbitration as other phase-6 commands.

An active action/committed edge remains an execution constraint. Calendar events
record CommittedAction delay rather than teleporting or granting unfinished
results. Need travel is protected from ordinary shift-end cancellation, while
critical interruption still observes its safe boundary.

## Persistence and inspection

Snapshot schema 30 adds report/check-in due stamps and their shared sequence,
check-in retry clocks, daily calendars and future boundary/decision timers, and
routine goal lifecycle/action ownership. Restore validates the roster opportunity,
unique goal identity, timers, service compatibility, physical completion proof and
active action ownership. Prior binary schemas are not migrated.

The daily-goals inspection view and exported dailyGoals show due ticks, windows,
roster versions, pending/completed/unnecessary/missed/superseded status, retry reason,
selected service and physical action IDs. Traces include DailyGoalDue,
DailyGoalDeferred, DailyGoalBlocked and DailyGoalResolved. Metrics add
dailyGoalsCompleted, dailyGoalsMissed and dailyGoalsBlocked. DailyGoals assertions
count lifecycle status, optionally restricted to an actor.

## Verification

Release compilation has zero warnings/errors. The full suite passes **551 tests**,
with zero failures or skips, in 22 seconds. Evidence:
artifacts/test-results/h4-daily-goals.trx.

Fourteen new tests cover mandatory low-strain rest, seek/window eligibility,
threshold crossing, a sustained repair backlog, missing-service yielding, finite
sleep, unread mail/roster isolation, initially unknown rosters, report/check-in tie
ordering, interrupted sleep, staggered default windows, strict snapshot corruption,
and the autonomous golden scenario. Twenty-one checkpoints include calendar
boundaries, finite travel/service, mid-sleep and an unread roster. Chunked restore
matches state bytes and detailed trace suffixes.

The backlog test retains unfinished repair goals while the rest goal becomes due
at tick 60. The next safe work boundary is tick 72; daily travel is selected there,
and the finite rest completes at tick 82 before further ordinary work. There are
no failed actions. This supports the busy-worker routine clause; it is not the
complete population or multi-day T22 gate.

daily_opportunities.json has no scripted actors/commands and passes **14 assertions**.
It explicitly uses a **240-tick day**, 80-tick duty, five-tick meal/rest blocks and
a 30-tick sleep block for an ordering fixture. It is not a 24-hour simulation or
evidence that normal-day need coverage is tuned. At horizon 240 it completes
18 actions, five terminal visits, one meal, one scheduled rest and one sleep,
with zero failures. Two unnecessary meal opportunities resolve without eating.

Meal admission/completion: 46/51. Rest: 62/67. Sleep: 152/182.
Run and replay both reproduce:

- Build: e86865ed7c5fc02880cc0c74cf0b48943948a02c8c02e0102e4a8eabc8c18e16
- Catalog: b6e020444ad6913abadc98c13f2ee37b4f442db0972fe9efb923d194d40a63c6
- State: 1400840cf5535cc61ea90ed5133a4890aae809c3d09769e7080a693354f96c7b
- Trace SHA256: C408BF85709846DDCA8B48AA01874477367AFA4621C9D23BB90D64C3BA1DEB41
- Artifacts: artifacts/daily_opportunities/ and artifacts/daily_opportunities-replay/

## Remaining release work

Daily life still needs authorized berth allocation/fallback, consenting conversation,
seek-level toilet and optional strain/social policies, and fuller priority/credential
restoration integration. Urgent protocol reporting retains its FIFO predecessors;
there is no independent reprioritization of individual reports within that queue.
Normal-day/multi-day need coverage, shortages, per-citizen delay/queue analysis,
population scaling and tuning remain unproven. Other work domains, authority
projects, response recovery and the complete requirement/gate audit also remain.
The user-authorized shutdown stays conditional on verified full-release completion.
