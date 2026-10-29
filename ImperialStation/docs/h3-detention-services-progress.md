# Detention services, finite release and completed-sentence history — H3 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment implements physical intake and release for the existing autonomous
audit → reprimand → authorization → armed escort chain. It advances R-WF-36 and
R-WF-38..42. It does not claim that T20/H3 or the full first release is complete.
The system remains a standalone .NET solution with no Unity or spaceland dependency.

## Physical service and custody

Detention policy can declare actual facilities with a completed tile, bed capacity,
service-operation capacity, finite intake/release durations, power and connectivity.
Both durations default to 300 ticks. A copied facility ID/destination alone creates
neither service nor capacity. Missing services leave an explained local wait; a service
at another tile cannot perform remote intake.

At the destination the leader plans RegisterIntake. Execution checks the full local
group, physical equipment, authentic copies, the actual facility, current issuer
status/expiry and capacity. Starting reserves a bed and operation slot. Occupancy and
Detained begin only after the full operation succeeds and its preconditions are checked
again. A full bed pool or busy service retains the group under guard and retries with
an explained local reason. Cancellation, expiry and service outages release unfinished
reservations without creating a detention receipt.

Successful intake transfers custody to the facility. Guards become locally free to
resume their own activities; the target remains at the facility. The facility schedules
release for intakeTick + the copied sentence. At that time a finite ReleaseDetention
operation claims service capacity. Occupancy remains until successful completion;
release then frees the bed, ends custody at the facility tile and posts a mailbox notice.
Ordinary citizen inputs cannot initiate release or cancel a sentence. Guard-order or
authorization expiry after a successful intake does not erase the sentence.

In this version both service operations require power and connectivity. An outage
cancels current progress: intake restarts from a new reservation, and release retains
the occupant and retries a full operation when the service becomes available. Capacity
checks distinguish beds from operations. Tests exercise two independently authorized
subjects with (beds, operations) of (1,1), (2,1) and (2,2).

## Information and institutional state

Physical intake and release issue formal service receipts. The authority updates its
reported Detained/Released state only when those messages arrive; it does not infer
completion from private guard progress, positions or inventories. Guards remain
institutionally committed until the intake receipt arrives. Facility identity has its
own record field, separate from terminal identity. Citizen posts and initial message
seeds cannot forge service receipt or release-notice kinds.

Work stays suspended through detention. On receipt of release, the authority sends
fresh copies of still-active unexpired assignments through ordinary mailboxes. A worker
must actually read current orders before executing retained work; manual ResumeWork
cannot bypass that requirement. Delivery alone never wakes the worker or resumes work.
An interrupted escort that never completed intake retains ordinary suspended-work rules.
Credentials are not revoked or consumed by detention.

Reprimands remain in history. Qualifying counts use received release records and include
only reprimands accepted after the latest known completed detention and within the time
window. Authority decisions retain the record-version cutoff used for their count;
case replies also use their existing validation-version cutoff. Later release receipts
cannot rewrite earlier decisions, including historical queries. A new accepted case
after release starts a new count; two new cases can authorize another detention without
reusing the earlier reprimands. Tests complete this second detention and restore both
historical authorizations, escorts and service stays.

## Persistence and interfaces

Snapshot schema 25 persists service definitions and current availability, bed reservations,
occupancy, finite operations, sentence/retry timers, receipt and notice identities,
received timestamps, intake event causes and post-release current-order messages.
Restore validates physical custody, operation ownership/duration, capacities, authenticated
transport, institutional receipt records and historical counting cutoffs. Completed
groups can coexist with a later escort of the same subject or reuse of the same guards.

Runner adds detention-facilities and detention-stays inspection views, a
DetentionServiceChange disturbance, DetentionStay/DetentionFacility/QualifyingReprimands
assertions, and occupancy/reservation/completed-detention metrics. The ordinary snapshot
export includes the new state. Binary checkpoints intentionally require their matching
schema and replay build.

## Verification evidence

Release build: zero warnings/errors. Full suite: 460 passed, zero failed, zero skipped.
Result: `artifacts/test-results/h3-detention-services.trx`.

The 29 new DetentionServiceTests cases cover finite reservation/intake/release,
co-location, missing and misplaced services, expiry before/during/after intake,
expiry exactly at completion, outage cancellation/retry, custody input filtering,
local guard release, delayed receipts/notices, competing subjects/capacity, work-read
requirements, closed request deduplication, new reprimands and a second detention,
invalid declarations, forged service messages, corrupt checkpoints and continuation.
Thirteen lifecycle checkpoints are resumed through completion; competing subjects are
round-tripped repeatedly while their bed and operation claims change.

`scenarios/detention_release.json` runs 20 milestone assertions through tick 2200.
It completes with 240 successful actions and zero failures. The canonical timeline is:

- 561: intake starts; one reservation, zero occupants.
- 861: intake completes; one occupant; authority still reports TeamForming.
- 862: intake receipt arrives; authority reports Detained.
- 1861: sentence expires and finite release starts; occupancy remains one.
- 2161: release completes; occupancy becomes zero; authority still reports Detained.
- 2162: release receipt and mailbox notice arrive; authority reports Released.
- 2200: two historical reprimands remain, with zero qualifying reprimands.

Ordinary run and strict replay both complete with matching identity and digest:

- Build: `ea6251137169f897696c9c21f89af991b90ec8b76f2d83c2ae7509d9e73c4387`
- Content: `317804d50ce4b2862f9f1c3b623bd7d0688af6bc36b1214985938e82879fe85f`
- State: `546d198f647c9bad4ff3447b470d55a3b6fed4b2ccf1c369640c84c5ac796d51`
- Artifacts: `artifacts/detention_release/` and `artifacts/detention_release-replay/`.

## Remaining release work

Learned revocation, negative search and interruption reports, unavailable-team recovery,
critical-need interruptions, and in-facility daily-life services remain pending. The
full T20 causal-purity and broader H1/H2/H3/H4 gates have not yet been audited as complete.
Credential recovery, general institutional recovery, authority projects, other work
domains, staffing/berths, full needs and daily life, population/tuning/benchmarks and
specialized viewer verification also remain. No full-release completion or shutdown
condition has been satisfied.
