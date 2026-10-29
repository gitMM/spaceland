# Response-chain verification — release progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

R-VER-02 now has an executable runner audit, applied to the canonical autonomous
credential recovery fixture and its 140 generated variants. This is progress on
the release verification framework, not completion of all institutional gates.

## Declared proof and command origins

initialState.responseChainRoles binds a named role to an actor, an initial condition
or an exact triggering event selector, and an ordered list of required transitions.
Keeping this under initialState preserves the specification's top-level scenario
sections. Exact trigger command allowances identify a declared actor/tick/action;
they must precede an explicitly timed trigger, match one scenario command and match
exactly one executed external command. A duplicated or missing execution fails.
Allowances are intended for permitted initial player triggers. Post-release player
inputs still need a separate release-boundary contract before broader T20 adoption.

World.Submit schedules ExternalCommand independently of autonomous decisions. The
shared executor emits ActionRequested.origin as ExternalCommand, Autonomous or
SystemProtocol. Ordinary execution and contention still use the same action API.
Custody movement, timed detention release and a consenting conversation partner
have explicit protocol origins. The audit checks their immediate protocol causes;
labeling an arbitrary installation as SystemProtocol cannot bypass it. Routine
boundary cancellation now has an explicit policy event as its cause.

The runner builds a directed graph from explicit cause IDs plus action and message
lifecycle links. It validates references, propagates unauthorized scripted ancestors,
checks every declared actor's requested actions, and requires the declared transitions
to be ordered descendants of the selected trigger. Missing requests, unknown origins,
broken links, ambiguous roots and incomplete history fail. An injected response
command fails even if the final machine and recovery assertions still succeed.
An initial-condition declaration permits policy roots; it does not silently allow
external commands. Scripted helpers are rejected when they occur in a response's
recorded causal ancestry, even if the helper is outside the declared actor role.

The result contains per-role root/transition IDs, action count and failures, also
written to response-chains.json. Audit failures become ordinary run assertion failures
and participate in replay status/assertion comparison. Compact audited runs collect
the trace internally but do not serialize detailed events. Replay from the original
initial checkpoint reproduces the proof. A partial continuation without prior trace
history explicitly fails the full-chain audit; it can still simulate and produce its
final state, but cannot claim historical proof.

## Recovery causal links and persistence

Physical CardLoss stores a privileged event reference separately from actor knowledge.
Only subsequent local discovery links CredentialRecoveryNeeded to that loss. This
bookkeeping cannot select a goal or expose a hidden location through ActorView.
Recovery plans reference discovery; revocation references the received report;
completion references the normal inbox read. Credential-dependent repair planning
references completed recovery before resuming retained work. The 12 required T18
transitions now form an inspected path from loss through discovery, travel, restricted
login, posting, revocation/acceptance, explicit recovery read, issuance, pickup,
normal login, current-order read and resumed installation.

Snapshot 38 persists discovery/completion references and per-card loss references,
and distinguishes queued external commands. Restore validates their numeric bounds,
ownership and command origin invariants. Existing checkpoints need their original
build. The conversation partner request is also explicit, with its consent event
linked to the initiating request; this preserves honest ancestry for both participants.

## Verification evidence

Release compilation succeeds without warnings or errors. All 699 tests pass in
the full suite (26 seconds). TRX: artifacts/test-results/response-chain-purity.trx.

The dedicated response-chain suite has 21 passing cases: compact/detailed parity,
initial-checkpoint replay, runtime command injection before/after serialization,
exact trigger allowances and duplicate rejection, broken/forward/missing links,
foreign scripted ancestors, forged/unknown/missing origins, invalid declarations,
partial-history refusal and a real consenting conversation protocol. Existing
recovery generation and checkpoint tests exercise the new audit and causal state.

artifacts/recovery-chain and artifacts/recovery-chain-replay match. The canonical
fixture retains 13 passing state assertions, 18 checked actions and all 12 required
causal transitions rooted at event 46. Transition IDs are
93, 95, 121, 126, 128, 129, 137, 145, 164, 170, 175 and 206.

- Build: dba2d289447cca6bb17458681ec9030f42ec3bde2b464dd606cf37644f63d022
- Content: f102a5dc4e50c079942ebf3dbacea2f256338e02040933ffe3571c098f75a1ea
- State: a6bc4361eb3c552243ed4325217fb3225fae30d4b9cd7ae30154459b2105c640
- Detailed trace SHA-256: A3978AEBE9F8C03A41490C3C5B3EE6D2E9F9138F6A4A53A6F006E56A64172C4A
- Proof JSON SHA-256: 88226FA1345C2AAC8840D49C35288A6CA0132B71B4504544A7D4C2354852353F

## Remaining scope

Extend the contracts and complete causal links for T19–T22 and T26, including the
permitted post-release player boundary. R-OBS-03 still needs the full structured
policy/version and decision-evidence inventory across policies; this audit does not
prove omitted data-dependency links. Streaming/bounded audit storage and population
costs remain part of performance validation. Recovery still needs the remaining
contention, priority and metamorphic checks with needs enabled. Authority projects,
construction, other work domains, institutional recovery, population/tuning gates,
presentation and the requirement-by-requirement release audit remain unfinished.
The goal stays active; computer shutdown remains pending until the full release is
implemented and verified.
