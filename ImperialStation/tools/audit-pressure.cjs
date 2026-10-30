// Post-run evidence audit. Export work-history with Station.Runner inspect first.
const fs = require('fs');
const path = require('path');
for (const dir of process.argv.slice(2)) {
  const read = name => JSON.parse(fs.readFileSync(path.join(dir, name), 'utf8'));
  const r = read('pressure-result.json'), s = read('scenario.json');
  const h = read('work-history.json'), i = read('institutions.json');
  const p = r.participation, people = p.citizens, days = p.horizon / p.dayTicks;
  const subjects = new Set(s.scriptedActors.map(a => a.actorId));
  const failures = [], require = (ok, message) => { if (!ok) failures.push(message); };
  require(r.status === 'completed' && !r.failures.length && !p.failures.length, 'Harness did not complete all gates');
  require(p.population === s.initialState.actors.length && people.length === p.population, 'Population differs');
  require(days >= 2 && Number.isInteger(days), 'Not a multiday run');
  require(people.every(c => c.meals.length === days && c.sleepTicks.length === days && c.meals.every(x => x > 0) && c.sleepTicks.every(x => x > 0)), 'Daily meals/sleep missing');
  require(people.every(c => c.rests > 0 && c.toilets > 0 && c.failedActions < 100), 'Routine participation or individual retry bound failed');
  require(r.citizensWithContinuingShifts === p.population && p.scheduledRestsOverdue === 0, 'Shift/rest continuation failed');
  require(r.overlappingPressure && r.sharedGuardDuties && p.maxTerminalQueue > 0, 'Shared overlapping workload unproven');
  require(r.events.CardLost === s.disturbances.filter(d => d.type === 'CardLoss').length && r.events.CredentialRecoveryCompleted === r.events.CardLost, 'Credential recovery incomplete');
  require(r.events.MachineFaulted === s.disturbances.filter(d => d.type === 'MachineFault').length, 'Fault waves incomplete');
  const claims = s.scriptedActors.flatMap(a => a.commands.filter(c => c.kind === 'PostMessage' && c.message?.kind === 'WorkCompleted').map(c => ({actor: a.actorId, attempt: c.message.attemptId})));
  const outcomes = r.caseOutcomes || [];
  require(claims.length === subjects.size * 2 && outcomes.length === claims.length && new Set(outcomes.map(c => c.repairAttemptId)).size === claims.length, 'Missing or duplicate case outcomes');
  const evidenceReviewReasons = new Set(['AmbiguousHandoff', 'NoAttributablePartMismatch', 'StaleWorkRecords', 'AmbiguousCompletion', 'IncompleteHistory', 'IncompleteDisclosure', 'HistoryPageLimitExceeded', 'EvidenceCapacityExceeded', 'AssignmentAmended', 'ConflictingAmendment', 'MissingAssignment', 'IncompleteAssignment', 'MissingSubmission']);
  const reviewReason = reason => evidenceReviewReasons.has(reason) || reason?.startsWith('AccountabilityPolicyRejected:') && evidenceReviewReasons.has(reason.slice('AccountabilityPolicyRejected:'.length));
  const qualifying = new Map();
  for (const c of outcomes) {
    const claim = claims.find(x => x.attempt === c.repairAttemptId);
    const attempt = i.attempts.find(a => a.id === c.repairAttemptId);
    const q = i.quality?.find(q => q.repairAttemptId === c.repairAttemptId);
    require(claim?.actor === c.actorId && attempt?.actorId === c.actorId && q?.machineId === c.machineId && q?.resultMessageId > 0, 'Case outcome lacks declared claim or verification');
    if (c.outcome === 'Accepted') {
      const accepted = i.cases?.find(x => x.id === c.caseId);
      const filing = i.filings?.find(f => f.caseId === c.caseId && f.verificationAttemptId === c.verificationAttemptId && f.status === 'Accepted' && f.readTick >= 0);
      require(accepted?.submission.attemptId === c.repairAttemptId && accepted?.submission.accountId === attempt?.accountId && accepted?.submission.machineId === c.machineId &&
        accepted?.submission.inspectionReportId === c.inspectionReportId && filing?.replyMessageId === c.decisionMessageId && i.reprimands.filter(x => x.caseId === c.caseId).length === 1, 'Accepted outcome lacks read, unique case evidence');
      qualifying.set(c.actorId, (qualifying.get(c.actorId) || 0) + 1);
    } else if (c.outcome === 'VerifiedClear') {
      require(q?.status === 'ReportedClear' && q?.verificationAttemptId === c.verificationAttemptId && q?.resultMessageId === c.inspectionReportId && c.caseId === 0, 'Clear outcome lacks matching received verification');
    } else if (c.outcome === 'NeedsReview') {
      const audit = i.audits.find(a => a.verificationAttemptId === c.verificationAttemptId);
      const filing = i.filings?.find(f => f.verificationAttemptId === c.verificationAttemptId);
      require(q?.status === 'ReportedDiscrepancy' && q?.verificationAttemptId === c.verificationAttemptId && q?.resultMessageId === c.inspectionReportId && c.caseId === 0 && reviewReason(c.reason) &&
        (audit?.status === 'NeedsReview' && audit.reason === c.reason || filing?.status === 'NeedsReview' && filing.reason === c.reason && filing.readTick >= 0 && filing.replyMessageId === c.decisionMessageId && c.decisionMessageId > 0), 'Review outcome lacks explicit evidence-based resolution');
    } else require(false, 'Pending or unknown disciplinary outcome');
  }
  const custodySubjects = new Set([...qualifying].filter(([actor, count]) => count >= (s.initialState.personnel?.detentionThreshold ?? 2)).map(([actor]) => actor));
  require(custodySubjects.size >= 2 && r.subjectsRequiringCustody === custodySubjects.size, 'Multiple qualifying subjects unproven');
  require(r.reprimands === outcomes.filter(c => c.outcome === 'Accepted').length && i.cases.length === r.reprimands && i.reprimands.length === r.reprimands, 'Unexpected or duplicate reprimands');
  require(r.released === custodySubjects.size && i.authorizations.length === custodySubjects.size && i.authorizations.every(a => custodySubjects.has(a.actorId)) && new Set(i.authorizations.map(a => a.actorId)).size === custodySubjects.size, 'Authorization/release counts differ');
  require(i.stays.length === custodySubjects.size && i.stays.every(st => custodySubjects.has(st.actorId) && st.status === 'Released') && new Set(i.stays.map(st => st.actorId)).size === custodySubjects.size, 'Wrong or unreleased custody subjects');
  require(r.releasedSubjectsResumingWork > 0 && r.releasedSubjectsResumingWork + r.releasedSubjectsReturningIdle === custodySubjects.size, 'Released subjects did not return to work/duty');
  const drained = phase => ['dirt', 'brokenMachines', 'openFacilityTasks', 'unfinishedJobs', 'missingResponses', 'activeDispatches', 'occupants', 'pendingCases'].every(key => phase[key] === 0);
  require(r.recoveryTick >= Math.max(...s.disturbances.map(d => d.tick)) && p.horizon - r.recoveryTick >= p.dayTicks && r.phases.filter(x => x.tick >= r.recoveryTick).every(drained), 'Sustained recovery unproven');
  require(r.terminalQueuesRecovered && r.needQueuesRecovered, 'Service queues did not recover');
  require(i.jobs.every(j => j.physicalStatus === 'CompletedPhysically' && j.authorityStatus === 'WorkCompleted'), 'Final work backlog remains');
  require(i.repairs.every(a => a.pendingMachines.length === 0), 'Pending repairs remain');
  require(r.accountingChecks === p.horizon / 1000 + 1, 'Missing primary conservation checks');
  require(r.checkpointMatches && r.continuationTraceMatches && /^[a-f0-9]{64}$/.test(r.continuationTraceHash), 'Exact replay unproven');
  require(r.traceGaps === 0 && r.invalidCauses === 0 && p.externalActions === s.scriptedActors.reduce((n, a) => n + a.commands.length, 0), 'Autonomous response provenance failed');
  const active = new Map(), assigned = new Set();
  let lastVersion = 0, assignmentCount = 0, duplicateHistory = 0;
  for (const e of h) {
    require(e.version === ++lastVersion, 'Work history version gap');
    if (e.kind === 'WorkAssigned') {
      assignmentCount++;
      if (active.has(e.jobId) || assigned.has(e.attemptId)) duplicateHistory++;
      active.set(e.jobId, e.attemptId); assigned.add(e.attemptId);
    } else if (e.kind === 'AssignmentSuperseded' || e.kind === 'AssignmentCancelled') {
      if (active.get(e.jobId) === e.attemptId) active.delete(e.jobId);
    }
  }
  require(duplicateHistory === 0 && r.duplicateActiveAssignments === 0, 'Duplicate active assignment in full history or samples');
  const finalActive = i.attempts.filter(a => a.assignment === 'Active');
  require(active.size === finalActive.length && finalActive.every(a => active.get(a.jobId) === a.id), 'Reconstructed assignment history disagrees with final state');
  const audit = {
    status: failures.length ? 'failed' : 'passed', population: p.population, days,
    primaryAccountingChecks: r.accountingChecks, historyRows: h.length, assignments: assignmentCount,
    duplicateActiveAssignmentsInHistory: duplicateHistory, maxIndividualFailures: Math.max(...people.map(c => c.failedActions)),
    recoveryTick: r.recoveryTick, clearObservationTicks: p.horizon - r.recoveryTick,
    reprimands: r.reprimands, releases: r.released, stateDigest: p.digest,
    continuationTraceHash: r.continuationTraceHash, missedOrdinaryWindows: p.missedDailyGoals,
    failures
  };
  fs.writeFileSync(path.join(dir, 'completion-audit.json'), JSON.stringify(audit, null, 2) + '\n');
  console.log(JSON.stringify({directory: dir, ...audit}));
  if (failures.length) process.exitCode = 1;
}
