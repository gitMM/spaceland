using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        public AuthorityConfig? Authority { get; private set; }
        private readonly SortedDictionary<long, KnownPersonnel> registrations = new SortedDictionary<long, KnownPersonnel>();
        private readonly SortedDictionary<long, ReportExpectationSnapshot> expectations = new SortedDictionary<long, ReportExpectationSnapshot>();
        private readonly SortedDictionary<long, AcknowledgmentWatchSnapshot> acknowledgmentWatches = new SortedDictionary<long, AcknowledgmentWatchSnapshot>();
        private readonly SortedDictionary<long, MissingReportIncident> missingReports = new SortedDictionary<long, MissingReportIncident>();
        private readonly SortedDictionary<(string Role, string Task), LearnedDurationSnapshot> learnedDurations = new SortedDictionary<(string, string), LearnedDurationSnapshot>(
            Comparer<(string Role, string Task)>.Create((a, b) => { int c = StringComparer.Ordinal.Compare(a.Role, b.Role); return c == 0 ? StringComparer.Ordinal.Compare(a.Task, b.Task) : c; }));
        private long nextMissingReport = 1;
        private void InitializeAuthority(AuthorityConfig policy)
        {
            if (!JobsEnabled || Information == null || publicKnowledge?.Work == null) throw new ArgumentException("Authority report tracking requires jobs, information and a published work catalog.");
            Authority = policy;
            foreach (var actor in actors.Values) registrations.Add(actor.Id.Value, new KnownPersonnel(actor.Id.Value, actor.Account.Value, actor.Role));
            Schedule(new ScheduledEvent { Tick = 0, Phase = EventPhase.Decision, Kind = ScheduledKind.OpenAdmissionBucket });
        }
        public IReadOnlyList<ReportExpectationSnapshot> InspectExpectations() => expectations.Values.Select(e => e.Copy()).ToArray();
        public IReadOnlyList<AcknowledgmentWatchSnapshot> InspectAcknowledgmentWatches() => acknowledgmentWatches.Values.Select(e => e.Copy()).ToArray();
        public IReadOnlyList<MissingReportIncident> InspectMissingReports() => missingReports.Values.Select(e => e.Copy()).ToArray();
        public IReadOnlyList<LearnedDurationSnapshot> InspectLearnedDurations() => learnedDurations.Values.Select(e => e.Copy()).ToArray();
        public IAuthorityView AuthorityView()
        {
            if (Authority == null) throw new InvalidOperationException("Authority report tracking is disabled.");
            var published = new SortedDictionary<long, RosterPayload>();
            foreach (var roster in PublishedRosters()) published.Add(roster.ActorId, roster);
            return new AuthorityViewSnapshot(processingTick < 0 ? LastProcessedTick : processingTick, Config.AuthorityNominalTicks, publicKnowledge!,
                registrations.Values.Select(p => new KnownPersonnel(p.ActorId, p.AccountId, p.Role, published.TryGetValue(p.ActorId, out var roster) ? roster : null)).ToArray(),
                Information!.LoginTicks, Information.PostTicks, Information.LatencyTicks, Routines?.CheckInWindow ?? 0);
        }
        private sealed class AuthorityViewSnapshot : IAuthorityView
        {
            public long Tick { get; }
            public long NominalTravelTicks { get; }
            public PublicKnowledge Publication { get; }
            public IReadOnlyList<KnownPersonnel> Personnel { get; }
            public long LoginTicks { get; }
            public long PostTicks { get; }
            public long LatencyTicks { get; }
            public long CheckInWindow { get; }
            internal AuthorityViewSnapshot(long tick, long nominal, PublicKnowledge publication, KnownPersonnel[] people, long login, long post, long latency, long window)
            { Tick = tick; NominalTravelTicks = nominal; Publication = publication; Personnel = Array.AsReadOnly(people); LoginTicks = login; PostTicks = post; LatencyTicks = latency; CheckInWindow = window; }
        }
        private void WatchAcknowledgment(WorkAttemptSnapshot attempt)
        {
            if (Authority == null || acknowledgmentWatches.ContainsKey(attempt.Id)) return;
            var view = AuthorityView(); var person = view.Personnel.Single(p => p.ActorId == attempt.ActorId);
            long baseTick = checked(processingTick + Authority.UnacknowledgedTicks);
            long extension = AuthorityEstimates.OffDutyExtension(processingTick, baseTick, person.Roster, view.CheckInWindow);
            var watch = new AcknowledgmentWatchSnapshot { AttemptId = attempt.Id, OrderMessageId = attempt.OrderMessageId, PostedTick = processingTick,
                Deadline = checked(baseTick + extension), OffDutyExtension = extension, RosterVersion = person.Roster?.Version ?? 0 };
            acknowledgmentWatches.Add(attempt.Id, watch); RecordAcknowledgmentWatch(watch);
            Schedule(new ScheduledEvent { Tick = watch.Deadline, Phase = EventPhase.Decision, Kind = ScheduledKind.AcknowledgmentDue, Value = attempt.Id });
            Emit("AcknowledgmentExpected", attemptId: attempt.Id, messageId: attempt.OrderMessageId, detail: "deadline=" + watch.Deadline);
        }
        private void AcknowledgmentDue(long attempt)
        {
            var watch = acknowledgmentWatches[attempt]; if (watch.Status != AcknowledgmentStatus.Awaiting) return;
            watch.Status = AcknowledgmentStatus.Overdue; RecordAcknowledgmentWatch(watch);
            Emit("AcknowledgmentOverdue", attemptId: attempt, messageId: watch.OrderMessageId, detail: "No acknowledgment received; read state unknown");
        }
        private void ReceiveAuthorityReport(MessageSnapshot message, WorkAttemptSnapshot attempt)
        {
            if (Authority == null) return;
            var kind = message.Draft.Kind;
            if (kind is MessageKind.OrderAccepted or MessageKind.OrderRejected)
            {
                if (acknowledgmentWatches.TryGetValue(attempt.Id, out var watch))
                {
                    watch.ResponseMessageId = message.Id;
                    if (watch.Status != AcknowledgmentStatus.Cancelled) watch.Status = kind == MessageKind.OrderAccepted ? AcknowledgmentStatus.Acknowledged : AcknowledgmentStatus.Rejected;
                    RemoveAuthorityEvent(ScheduledKind.AcknowledgmentDue, attempt.Id); RecordAcknowledgmentWatch(watch);
                }
                if (kind == MessageKind.OrderAccepted) CreateExpectation(message, attempt);
                return;
            }
            if (!(kind is MessageKind.WorkCompleted or MessageKind.WorkBlocked or MessageKind.UnableToContinue)) return;
            if (!expectations.TryGetValue(attempt.Id, out var expectation))
            { Emit("ResultBeforeAcknowledgment", attemptId: attempt.Id, messageId: message.Id, detail: "Recorded claim; no acknowledged expectation or duration sample"); return; }
            if (expectation.ResultMessageId != 0) return;
            var key = (expectation.Role, expectation.TaskKind);
            learnedDurations.TryGetValue(key, out var learned);
            long ratio = expectation.UnscaledEstimate > 0 ? AuthorityEstimates.Learn(learned?.Ratio ?? 1000, expectation.AcknowledgmentTick, processingTick, expectation.UnscaledEstimate, Authority) : 0;
            expectation.ResultMessageId = message.Id; expectation.ResultTick = processingTick;
            var parentIncident = missingReports.Values.SingleOrDefault(i => i.ReplacementAttemptId == attempt.Id);
            if (parentIncident != null)
            {
                parentIncident.ReplacementResultMessageId = message.Id;
                parentIncident.WorkResolution = kind == MessageKind.WorkCompleted ? "ReportedComplete" : kind == MessageKind.WorkBlocked ? "ReportedBlocked" : "ReportedUnableToContinue";
                RecordIncidentResponse(parentIncident);
            }
            if (expectation.Status != ExpectationStatus.Cancelled) expectation.Status = ExpectationStatus.Satisfied;
            RemoveAuthorityEvent(ScheduledKind.ReportExpectationDue, attempt.Id);
            if (expectation.UnscaledEstimate > 0)
            {
                if (learned == null) { learned = new LearnedDurationSnapshot { Role = key.Role, TaskKind = key.TaskKind }; learnedDurations.Add(key, learned); }
                learned.Ratio = ratio; learned.Samples = checked(learned.Samples + 1);
                Emit("AuthorityDurationLearned", attemptId: attempt.Id, messageId: message.Id, detail: "ratio=" + ratio + "; role=" + key.Role + "; task=" + key.TaskKind);
            }
            if (missingReports.TryGetValue(attempt.Id, out var incident)) { incident.ResponseMessageId = message.Id; RecordMissingReport(incident); }
            RecordExpectation(expectation);
            Emit("ReportExpectationSatisfied", attemptId: attempt.Id, messageId: message.Id, detail: kind + "; late=" + (expectation.Deadline >= 0 && processingTick > expectation.Deadline));
        }
        private void CreateExpectation(MessageSnapshot acknowledgment, WorkAttemptSnapshot attempt)
        {
            if (expectations.ContainsKey(attempt.Id)) return;
            var view = AuthorityView(); var person = view.Personnel.Single(p => p.ActorId == attempt.ActorId);
            var job = jobs[attempt.JobId];
            var estimate = job.Kind == WorkKind.ConstructHabitat ? AuthorityEstimates.Construction(view, attempt.ActorId, ConstructionSpecification(job.ProjectId), acknowledgment.TerminalId) : job.Kind != WorkKind.CleanTile ? AuthorityEstimates.Repair(view, attempt.ActorId, job.MachineId, acknowledgment.TerminalId, job.Kind == WorkKind.VerifyMachine) :
                AuthorityEstimates.Cleanup(view, attempt.ActorId, new Id<TileTag>(job.TileId), job.ReportedUnits, Information!.InspectTicks, acknowledgment.TerminalId);
            learnedDurations.TryGetValue((person.Role, job.Kind.ToString()), out var learned);
            var e = new ReportExpectationSnapshot { AttemptId = attempt.Id, JobId = attempt.JobId, ActorId = attempt.ActorId, Role = person.Role,
                TaskKind = job.Kind.ToString(),
                AcknowledgmentMessageId = acknowledgment.Id, AcknowledgmentTick = processingTick, OriginTerminalId = acknowledgment.TerminalId,
                RatioAtCreation = learned?.Ratio ?? 1000, RosterVersion = person.Roster?.Version ?? 0 };
            if (estimate == null) { e.Status = ExpectationStatus.Unestimated; e.Reason = "MissingPublishedJobOrAcknowledgmentTerminal"; }
            else
            {
                var deadline = AuthorityEstimates.Deadline(processingTick, estimate.Total, e.RatioAtCreation, Authority!, person.Roster, view.CheckInWindow);
                e.UnscaledEstimate = estimate.Total; e.ReturnTerminalId = estimate.ReturnTerminalId; e.Slack = deadline.Slack;
                e.BaseDeadline = deadline.BaseTick; e.OffDutyExtension = deadline.OffDutyExtension; e.Deadline = deadline.Tick;
            }
            if (attempt.Assignment != AssignmentStatus.Active) { e.Status = ExpectationStatus.Cancelled; e.Reason = "Assignment" + attempt.Assignment; }
            expectations.Add(attempt.Id, e); RecordExpectation(e);
            if (e.Status == ExpectationStatus.AwaitingResult) Schedule(new ScheduledEvent { Tick = e.Deadline, Phase = EventPhase.Decision, Kind = ScheduledKind.ReportExpectationDue, Value = e.AttemptId });
            Emit("ReportExpectationCreated", attemptId: e.AttemptId, messageId: acknowledgment.Id, terminalId: acknowledgment.TerminalId,
                detail: "estimate=" + e.UnscaledEstimate + "; deadline=" + e.Deadline + "; offDuty=" + e.OffDutyExtension + "; " + e.Status);
        }
        private void ReportExpectationDue(long attempt, long sequence)
        {
            var e = expectations[attempt]; if (e.Status != ExpectationStatus.AwaitingResult) return;
            var person = AuthorityView().Personnel.Single(p => p.ActorId == e.ActorId);
            var incident = new MissingReportIncident { Id = nextMissingReport, AttemptId = attempt, JobId = e.JobId, ActorId = e.ActorId,
                Cohort = person.Roster?.Cohort ?? -1, OpenedTick = processingTick, OpeningSequence = sequence,
                ReplacementDepth = missingReports.Values.SingleOrDefault(i => i.ReplacementAttemptId == attempt) is MissingReportIncident parent ? parent.ReplacementDepth + 1 : 0 };
            _ = checked(nextMissingReport + 1);
            e.Status = ExpectationStatus.Overdue; missingReports.Add(attempt, incident); nextMissingReport++;
            RecordExpectation(e); RecordMissingReport(incident);
            incident.OpeningEventId = Emit("MissingWorkReportOpened", attemptId: attempt, messageId: e.AcknowledgmentMessageId, detail: "incident=" + incident.Id + "; overdue; status unknown");
            RecordIncidentResponse(incident);
            RequestIncidentReview(processingTick);
        }
        private void CancelAuthorityExpectation(long attempt, string reason)
        {
            if (Authority == null) return;
            if (missingReports.TryGetValue(attempt, out var incident) && incident.ReplacementAttemptId == 0 && NeedsWorkResponse(incident))
                SetIncidentResponse(incident, IncidentWorkResponse.Cancelled, "ParentAssignment" + reason);
            RequestIncidentReview(processingTick);
            if (acknowledgmentWatches.TryGetValue(attempt, out var watch) && watch.Status is AcknowledgmentStatus.Awaiting or AcknowledgmentStatus.Overdue)
            { watch.Status = AcknowledgmentStatus.Cancelled; RemoveAuthorityEvent(ScheduledKind.AcknowledgmentDue, attempt); RecordAcknowledgmentWatch(watch); }
            if (expectations.TryGetValue(attempt, out var e) && e.Status is ExpectationStatus.AwaitingResult or ExpectationStatus.Overdue or ExpectationStatus.Unestimated)
            {
                e.Status = ExpectationStatus.Cancelled; e.Reason = reason; RemoveAuthorityEvent(ScheduledKind.ReportExpectationDue, attempt); RecordExpectation(e);
                Emit("ReportExpectationCancelled", attemptId: attempt, detail: reason);
            }
        }
        private void RemoveAuthorityEvent(ScheduledKind kind, long attempt)
        { foreach (var e in scheduled.Where(e => e.Kind == kind && e.Value == attempt).ToArray()) scheduled.Remove(e); }
        private void RecordExpectation(ReportExpectationSnapshot e)
        {
            records.Append(RecordKind.ReportExpectation, e.AttemptId, workAttempts[e.AttemptId].AccountId, processingTick,
                "expectation/" + e.AttemptId + "/" + e.Status + "/" + e.ResultMessageId, new[] {
                    Number(AttributeKind.ActorId, e.ActorId), Number(AttributeKind.TaskId, e.JobId), Number(AttributeKind.AttemptId, e.AttemptId), Text(AttributeKind.Role, e.Role), Text(AttributeKind.TaskKind, e.TaskKind),
                    Number(AttributeKind.MessageId, e.AcknowledgmentMessageId), Number(AttributeKind.AcknowledgedTick, e.AcknowledgmentTick), Number(AttributeKind.TerminalId, e.OriginTerminalId), Number(AttributeKind.ReturnTerminalId, e.ReturnTerminalId),
                    Number(AttributeKind.EstimateTicks, e.UnscaledEstimate), Number(AttributeKind.LearnedRatio, e.RatioAtCreation), Number(AttributeKind.SlackPermille, e.Slack), Number(AttributeKind.Deadline, e.Deadline),
                    Number(AttributeKind.OffDutyTicks, e.OffDutyExtension), Number(AttributeKind.RosterVersion, e.RosterVersion), Text(AttributeKind.WorkStatus, e.Status.ToString()), Number(AttributeKind.ResultMessageId, e.ResultMessageId) });
        }
        private void RecordAcknowledgmentWatch(AcknowledgmentWatchSnapshot e)
        {
            records.Append(RecordKind.AcknowledgmentWatch, e.AttemptId, workAttempts[e.AttemptId].AccountId, processingTick,
                "ack-watch/" + e.AttemptId + "/" + e.Status + "/" + e.ResponseMessageId, new[] { Number(AttributeKind.AttemptId, e.AttemptId), Number(AttributeKind.MessageId, e.OrderMessageId),
                    Number(AttributeKind.SubmittedTick, e.PostedTick), Number(AttributeKind.Deadline, e.Deadline), Number(AttributeKind.OffDutyTicks, e.OffDutyExtension), Number(AttributeKind.RosterVersion, e.RosterVersion),
                    Text(AttributeKind.WorkStatus, e.Status.ToString()), Number(AttributeKind.ResultMessageId, e.ResponseMessageId) });
        }
        private void RecordMissingReport(MissingReportIncident e)
        {
            records.Append(RecordKind.MissingWorkReport, e.Id, workAttempts[e.AttemptId].AccountId, processingTick,
                "missing-report/" + e.Id + "/" + e.ResponseMessageId, new[] { Number(AttributeKind.ActorId, e.ActorId), Number(AttributeKind.AttemptId, e.AttemptId), Number(AttributeKind.TaskId, e.JobId),
                    Number(AttributeKind.Cohort, e.Cohort), Number(AttributeKind.ReceivedTick, e.OpenedTick), Text(AttributeKind.WorkStatus, e.ResponseMessageId == 0 ? "OverdueStatusUnknown" : "ResponseReceivedTaskUnverified"), Number(AttributeKind.ResultMessageId, e.ResponseMessageId) });
        }
    }
}
