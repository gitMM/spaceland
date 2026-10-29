# Imperial Station — Suggested Infiltration Roadmap

**Status:** non-normative companion to `imperial_station_simulation_build_spec_v1.2.md`, document revision 1.2.1 (formerly §23 of v1.1)
**Date:** 2026-10-28

Nothing here is a requirement for H1–H4. Implementing agents MUST NOT build these features unless a later spec revision promotes them with requirement IDs and tests.

## Implementation status — 2026-10-29

Scope update: the user has deferred station expansion. First release now has a
fixed layout and population; construction acceptance is historical, not a release
blocker. The active milestone combines recovery and concurrent institutions under
sustained pressure; see [the pressure workload](../../../ImperialStation/docs/pressure-station.md).

The required simulation foundation is being implemented independently in
[`ImperialStation/`](../../../ImperialStation/README.md), outside Unity's `Assets`
tree. The first release is **not yet complete**. This roadmap's proposed infiltration
features have not been promoted to first-release requirements.

Delivered foundation systems include deterministic simulation and replay, bounded
planning and private knowledge, physical terminals and credentials, autonomous work
and reporting, rosters and overdue responses, maintenance/QA/audits, detention and
escorts, needs and daily routines, credential recovery, and autonomous habitat
construction. Later local construction surveys now correct authority capacity
records through delayed terminal reports while preserving physical state.

The latest complete suite passed **816 tests**, and the capacity-correction scenario
replays with identical state, trace and causal proofs. This does not establish full
release readiness: initial capacity identification, construction follow-up, remaining
institutional variants, remaining daily/population acceptance variants and the complete
acceptance audit still need work. The new integrated station proves two configured
days of autonomous daily participation and sustained work, including shortage,
recovery and checkpoint controls. A 2,048-citizen population baseline now measures
real work, needs and terminal use. See the [integration report](../../../ImperialStation/docs/integration-baseline.md)
and [current acceptance matrix](../../../ImperialStation/docs/acceptance-current.md).
The subsequent [finite disruption/recovery goal](../../../ImperialStation/docs/recovery-station.md)
also passes at 32 and 2,048 citizens: all lost credentials and faulted machines
recover, overdue work and patrol responses arrive, finite backlogs drain, and
daily meals/sleep continue across five shortened days. Checkpoint continuation
matches. The specialized institutional follow-up is recorded below; full calibration and release gates stay open.
The subsequent performance pass reduces the same population simulation from
14m21s to 5m20s with identical final state; the small recovery/replay check takes
about four seconds. Reproduction and timing scope are in the recovery report.

The [specialized institutional follow-up](../../../ImperialStation/docs/institutional-station.md)
now passes at 32 and 2,048 citizens. Eight small controls cover correct work, the
reprimand threshold, unavailable guards, terminal/intake outages, full capacity and
an interrupted escort followed by a different guard team. Daily life, release,
resumed work and checkpoint continuation are measured. Wider concurrent institutional
load and complete first-release acceptance remain open.
The complete small institutional command takes **3.309 seconds**; the population
command takes **213.815 seconds** including checkpoint continuation. Compilation is
excluded. These are two-day institutional measurements, not a direct comparison
with the five-day recovery benchmark above.

See the [build specification's implementation status](<imperial_station_simulation_build_spec_v1.2 (2).md#implementation-status--2026-10-29>)
for the delivered-system inventory and remaining gates, and the
[latest verification report](../../../ImperialStation/docs/institutional-station.md)
for reproducible evidence. Visible-role challenges, credential impersonation,
information-theft/extraction missions, sabotage dependencies and authoritative FPS
stealth remain roadmap proposals; the implemented foundation is not a playable
infiltration release.

The user revoked the computer-shutdown instruction on 2026-10-29. Keep the computer running.

Stabilization, the acceptance matrix, the integrated daily station, the population
baseline and repairs exposed by the declared institutional controls are complete
for their measured scope. The recommended next increment is to classify missed
ordinary routine windows and close the remaining daily-life priority/unread-roster
checks, using the small station for iteration. Then broaden concurrent institutional
load and seed coverage. The build specification's **Recommended next increment —
close daily-life acceptance gaps** records this guidance; the acceptance matrix
retains all other open gates. Roadmap features remain deferred.

## 0. Added in v1.2: expected-presence tags

v1 makes every tile traversable and has no spatial access control, so "being somewhere you shouldn't" has no meaning yet. A cheap first step before doors and locks:

- Tag tiles or services with the roles expected to be present, optionally per shift. Example: Records Terminal tile → supervisors; Armory → guards.
- A co-located guard or supervisor who observes an actor whose *visible* role is not expected there gets a typed local discrepancy fact.
- The observer's local response (challenge, demand credentials or order, report at the next terminal) uses existing policy, knowledge and terminal rules. It does not use a station-wide suspicion meter.
- This gives T20-style evidence a spatial source and makes visible role/uniform (§1, layered identity) matter, without changing traversal.

This section proposes features for the later game. They are not additional first-release completion gates. The required institutional scenarios, terminal communication, and daily life should work before these ideas are expanded.

The intended experience is to appear to belong, learn how the station operates, redirect its people, accomplish a physical objective, and recover when a deception fails.

## 1. High-value additions

| Suggestion | Gameplay purpose | Small initial implementation |
| --- | --- | --- |
| Layered identity | A uniform, credential, and work order answer different questions | Separate visible role/appearance, physical actor, authenticated account, and task authorization |
| Contextual challenges | Guards can notice something wrong without instantly arresting everyone | A few observed discrepancy types and responses: show credential/order, comply, refuse, leave |
| Discoverable operational information | Learning the station is part of the mission | Terminal directories, rosters, equipment labels, and records expose real entity IDs and dependencies |
| Work as cover | Ordinary jobs provide equipment, reasons for presence, and useful destinations | Target-specific work permissions and expected tools/actions |
| Persistent evidence | Deception has consequences that can be investigated | Physical items, terminal-use records, signed claims, and local witnesses remain separate |
| Real distractions | Diverting people changes actual workload and staffing | A spill, missing part, or failed machine creates ordinary jobs competing for real personnel |
| Local security and delayed escalation | Guards respond to what they see while wider coordination remains imperfect | Local challenge/search/restraint; terminal reporting; searches based on last known information |
| Physical objectives and extraction | Mission progress requires moving people, objects, and data | One portable data medium, one escort/follow relationship, and a real departure/handoff condition |
| Consequences short of immediate failure | Exposure changes the problem and allows recovery | Challenge, restricted privileges, reprimand, search, detention order, and physical custody as distinct states |
| Readable detection and institutional feedback | The player can understand causes and make informed choices | Visible challenges, service queues, report/read receipts, and later authoritative visibility/occlusion |

**Layered identity and challenges.** Looking like maintenance may explain presence in a corridor. A terminal demands credentials; a supervisor may demand a relevant work order. None of these alone should imply every permission. Initial contextual checks can use typed rules rather than generated dialogue or a station-wide suspicion number.

A guard can react immediately to a suspicious act observed locally. Reporting it to headquarters requires a terminal. The guard's local knowledge and the institution's search orders should remain distinct.

Sparse familiarity can add another check later: a uniform may satisfy a stranger while a regular coworker recognizes that the face is wrong. Store a few meaningful acquaintances per person rather than a relationship entry for every pair.

**Credentials and stale authorization.** Later, allow stolen credentials to authenticate as an account different from the physical actor. The current holder-bound rule must change explicitly and gain tests; do not quietly introduce impersonation through a debug action.

A useful resulting sequence is: take a card while its owner is occupied; use the account before the loss is reported; leave a terminal record; then respond to revocation or investigation. The owner follows the existing recovery workflow. Printed permits or carried order copies could make stale authorization visible in the world: an offline guard may accept an order the authority has since cancelled.

**Work and daily life as opportunities.** A real repair gives a credible reason to carry tools and visit equipment. Cafeteria queues, toilet trips, sleeping shifts, conversation, and headquarters detours move people for understandable reasons. The player can observe those routines, but interruptions should change them.

Consider limited work-order manipulation through actual permissions: request a repair, redirect a delivery, or persuade an authorized colleague to submit a request. A casual worker should not be able to issue an armed escort order by typing one.

**Intelligence and evidence.** Let the player discover which cartridge fits a machine, where a supervisor's computer is, who works the next shift, and which records are checked. Knowledge should come from inspectable sources. Keep unknown facts unknown in the normal player interface.

Removing a physical trace should not erase a terminal record; editing a record should not move an object. A false completion can implicate an account without proving physical guilt. Such imperfect accountability can create framing and counter-investigation opportunities once the base case model is sound.

**Sabotage with operational consequences.** Start with two or three fictional equipment dependencies. Removing a component, delaying maintenance, and inducing an authorized shutdown can all satisfy the same subsystem-failure objective while producing different evidence and responses.

Distractions should create real work. A fault can pull a supervisor away, but a well-stocked nearby workshop may let the response finish sooner than the player expected. Avoid a generic distraction that immobilizes an NPC for an arbitrary narrative duration.

**Physical missions and recovery.** Restricted information can be downloaded at an authorized terminal onto a portable data tape/cartridge. The item then has ownership, location, carrying consequences, and an extraction route. A rescued prisoner must physically travel with someone; issuing a transfer order alone does not rescue them.

Being challenged or exposed need not immediately end the mission. A detention order waiting unread in a guard's inbox creates a different situation from guards already beside the player. Escape should depend on a usable route, transport/handoff, permissions, and carried objectives.

**FPS visibility and feedback.** Tile-level observation proves the institutional systems but is not the final stealth model. Before production FPS stealth, add authoritative visibility/occlusion and readable cues. Unity camera/rendering alone must not decide detection; the headless core needs equivalent rules or explicitly tested data inputs.

Preserve the all-traversable prototype while developing social/action permissions. Physical locked routes, vents, doors, and detailed interiors should be a deliberate later spatial-model revision.

## 2. A useful first playable mission

**Obtain restricted station information and physically leave while maintaining a worker cover.**

Give the player a legitimate assignment near the objective. Provide at least two viable approaches, such as acquiring a relevant authorization or exploiting a maintenance disruption. Require a terminal interaction, a real data item, and arrival at a usable extraction/handoff service.

Let the required systems generate consequences: a missing card sends its owner to headquarters; an overdue job draws a patrol and replacement; a bad repair produces an audit; a second reprimand creates an escort order; a routine meal or shift change delays a message.

Specify the objective and initial circumstances. Do not script the player's deception, the supervisor's discovery, or the guard response. Use the resulting trace to ask whether the player could understand and influence what happened.

This small mission can test the infiltration loop before a full station-destruction campaign. Thousands-of-citizens workloads separately test scale using the same rules.

## 3. Suggested development order after the required foundation

1. Add work-order permissions, visible role appearance, and a few local challenges.
2. Add discoverable intelligence and a small set of persistent evidence sources.
3. Add a physical information-theft/extraction mission with several approaches.
4. Add credential misuse, richer security responses, and recovery after exposure.
5. Expand rescue, sabotage dependencies, and authoritative FPS perception in response to playable testing.

A few understandable interacting systems are more useful here than a large list of isolated mechanics. Each addition should supply ordinary NPC behavior as well as a way for the player to exploit or disrupt it.

