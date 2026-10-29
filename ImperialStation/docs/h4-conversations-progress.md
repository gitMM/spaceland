# Finite local conversation — H4 progress

> Instruction update (2026-10-29): the user revoked the earlier computer-shutdown permission. Any shutdown wording below is historical and superseded by [the current instruction](completion-instruction.md). Keep the computer running.

This increment implements conversation and bounded partner discovery for R-LIF-06,
R-LIF-13..14, D-54..55 and D-86, including participant-only knowledge under R-KNW-05.
It does not complete the first release or its full daily-life/population gates.

## Consent, claims and activity

Converse requires needs-enabled participants physically co-located on one tile.
The initiator's command expresses their consent; the recipient independently applies
its local policy. Default autonomous consent requires social need at seek, a known
off-duty period, availability, and no eligible higher-priority duty/need. A scenario
may declare explicitConsentActors as advance willingness for physical tests or
configured behavior, but this does not bypass custody, availability, critical needs,
known urgent orders, due goals or ordinary duty work. No card is required.

Successful admission gives both participants finite Converse actions and paired
coordination claims. A single completion event owns the pair. Competing invitations
or incompatible actions cannot double-book either actor. Either participant can
cancel; a critical need also interrupts the pair through the normal cancellation
path. Cancellation releases both claims without social relief or fact disclosure.

After the full duration (default 1800 ticks), both participants receive 300 points
of social relief. Accumulation continues throughout the action; there is no partial
conversation benefit. Completion records two physical participant actions and one
conversation. Autonomous selection uses bounded GOAP at optional priority, after
eligible higher goals. Additional strain rest wins the local optional tie when both
are sought. Incoming messages do not interrupt conversation or imply a read.

## Local discovery and knowledge

An ordered tile-to-actor index is maintained on actor creation and every ordinary
or escorted movement arrival. Search uses ordered range views, visits at most four
candidates (D-86), and rotates a private cursor across retries. It does not scan all
actors or evaluate pairs across the population. Transit and unavailable candidates
are rejected locally. A failed search explains NoLocalConsentingPartner and backs
off; remote actors never appear as partners.

Typed topics have an explicit restrictive sharing policy:

- Routine: the speaker's known own cohort or rest-tile fact.
- WorkStatus: a known job/attempt work-status fact.
- WitnessedProblem: a directly observed/inspected broken machine or positive dirt.
- AllowedRequest: a known machine's required-resource description for an informal request.

At completion, each speaker selects at most four permitted facts from their own
bounded knowledge, ordered by key. Both payloads are captured before either is
learned, preventing immediate same-conversation echo. Recipients store accepted
facts as Heard with sourceActorId (speaker), sourceId (conversation ID), original
observedTick and actual learnedTick. Pin/unpin operations preserve this provenance.
Pinned recipient evidence cannot be overwritten by conversation. Capped knowledge
may reject a fact without invalidating the completed physical activity.

Private credentials, formal records and orders are not disclosed by these policies.
Heard information is not a direct witness. Conversation does not post messages,
create authority beliefs, dispatch work, or replace formal reporting channels.
Bystanders receive no conversation facts.

## Persistence and diagnostics

Snapshot 34 persists conversation configuration, history, exchanges and search
counters, private candidate cursors, action topics and speaker provenance. Claim
and action lookup indexes are reconstructed from validated history; the local
spatial index is reconstructed from actor positions. Restore verifies paired action
ownership, locality, timer/duration, terminal status, bounded disclosures and current
Heard facts against the completed conversation's speaker/recipient exchange.

Runner adds conversations inspection/export, Conversations status/count assertions
(optionally per actor), conversationsCompleted and conversationPartnerCandidatesExamined
metrics. Need-agent completion counts include a participant's completed conversation;
the separate needActivities history continues to represent physical service use.

## Verification

Release build: zero warnings/errors. Full suite: **607 passed, zero failed, zero
skipped**, 23 seconds. Evidence: artifacts/test-results/h4-conversations.trx.
Fifteen new cases cover finite paired claims, exact social relief, refused/remote
partners, competing invitations, both cancellation directions, critical interruption,
permitted known facts, private/pinned evidence, typed sharing policy, bounded search,
movement-index updates, capped disclosure/knowledge, and forged claim/timer/speaker
rejection. Twelve checkpoints include mid-conversation, an unread roster update and
later routine boundaries, with identical continuation state and trace.

scenarios/conversation.json has no scripted commands or cards. Two co-located,
off-duty citizens converse at ticks 0–5. The third citizen has no seek-level social
need and does not participate or receive relief. All eleven assertions pass with
two completed participant actions, zero failed actions and no messages or authority
beliefs. The deliberately short physical block is not a full-day gate.

Run/replay artifacts: artifacts/conversation and artifacts/conversation-replay.
Final state and detailed trace match:

- Build: bb4a0d83ba9ea612e174008be74501f3d98d00a11b78a6a5f76cda05dee4cc1b
- Content: b6e020444ad6913abadc98c13f2ee37b4f442db0972fe9efb923d194d40a63c6
- State: 6a6b8fdfedf8da37912bdd39935bb957ae919ea9548477d6aaf097d0511b6f5f
- Trace SHA-256: 1C18A09B0713F23FB0F5726108E4250A8737364C8854FAA6392E02834DD437AB

Remaining release work includes credential recovery, full priority/institutional
recovery integration, normal-day/population gates and coverage metrics, authority
capacity/construction projects, further work domains, performance gates, presentation
validation and the requirement-by-requirement release audit. Shutdown remains pending
until the entire release is implemented and verified.
