using System;
using System.IO;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteAuthority(BinaryWriter w)
        {
            w.Write(Authority != null); if (Authority == null) return;
            w.Write(Authority.SlackMinimum); w.Write(Authority.SlackMargin); w.Write(Authority.SlackMaximum); w.Write(Authority.GraceTicks);
            w.Write(Authority.EmaNumerator); w.Write(Authority.EmaDenominator); w.Write(Authority.UnacknowledgedTicks);
            w.Write(Authority.IncidentAdmissionPermille); w.Write(Authority.MaximumReplacementDepth); w.Write(Authority.ResponseOrderTicks); w.Write(Authority.PatrolRole);
            w.Write(nextMissingReport); w.Write(registrations.Count);
            foreach (var p in registrations.Values) { w.Write(p.ActorId); w.Write(p.AccountId); w.Write(p.Role); }
            w.Write(acknowledgmentWatches.Count);
            foreach (var a in acknowledgmentWatches.Values)
            { w.Write(a.AttemptId); w.Write(a.OrderMessageId); w.Write(a.PostedTick); w.Write(a.Deadline); w.Write(a.RosterVersion); w.Write(a.OffDutyExtension); w.Write((int)a.Status); w.Write(a.ResponseMessageId); }
            w.Write(expectations.Count);
            foreach (var e in expectations.Values)
            {
                w.Write(e.AttemptId); w.Write(e.JobId); w.Write(e.ActorId); w.Write(e.Role); w.Write(e.TaskKind); w.Write(e.AcknowledgmentMessageId); w.Write(e.AcknowledgmentTick);
                w.Write(e.OriginTerminalId); w.Write(e.ReturnTerminalId); w.Write(e.UnscaledEstimate); w.Write(e.RatioAtCreation); w.Write(e.Slack); w.Write(e.BaseDeadline);
                w.Write(e.OffDutyExtension); w.Write(e.Deadline); w.Write(e.RosterVersion); w.Write((int)e.Status); w.Write(e.Reason); w.Write(e.ResultMessageId); w.Write(e.ResultTick);
            }
            w.Write(missingReports.Count);
            foreach (var i in missingReports.Values) { w.Write(i.Id); w.Write(i.AttemptId); w.Write(i.JobId); w.Write(i.ActorId); w.Write(i.Cohort); w.Write(i.OpenedTick); w.Write(i.OpeningSequence); w.Write(i.Kind); w.Write(i.ResponseMessageId); }
            w.Write(learnedDurations.Count);
            foreach (var l in learnedDurations.Values) { w.Write(l.Role); w.Write(l.TaskKind); w.Write(l.Ratio); w.Write(l.Samples); }
            WritePatrols(w); WriteIncidentResponses(w);
        }
        private RosterPayload? ExpectedRoster(long version, long actor)
        {
            if (version == 0) return null;
            if (!rosters.TryGetValue(version, out var entry) || !entry.Published || entry.Roster.ActorId != actor) throw new ArgumentException("Expectation refers to an unknown published roster.");
            return entry.Roster;
        }
        private void ReadAuthority(BinaryReader r)
        {
            if (!r.ReadBoolean()) return;
            if (!JobsEnabled || Information == null || publicKnowledge?.Work == null) throw new ArgumentException("Authority snapshot lacks dependencies.");
            Authority = new AuthorityConfig(r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64(), r.ReadInt32(), r.ReadInt64(), r.ReadString());
            nextMissingReport = r.ReadInt64(); int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var p = new KnownPersonnel(r.ReadInt64(), r.ReadInt64(), r.ReadString());
                if (!actors.TryGetValue(new Id<ActorTag>(p.ActorId), out var actor) || actor.Account.Value != p.AccountId) throw new ArgumentException("Unknown authority personnel.");
                registrations.Add(p.ActorId, p);
            }
            if (registrations.Count != actors.Count) throw new ArgumentException("Incomplete personnel registry.");
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var a = new AcknowledgmentWatchSnapshot { AttemptId = r.ReadInt64(), OrderMessageId = r.ReadInt64(), PostedTick = r.ReadInt64(), Deadline = r.ReadInt64(), RosterVersion = r.ReadInt64(), OffDutyExtension = r.ReadInt64(), Status = ReadEnum<AcknowledgmentStatus>(r), ResponseMessageId = r.ReadInt64() };
                if (!workAttempts.TryGetValue(a.AttemptId, out var attempt) || attempt.OrderMessageId != a.OrderMessageId || !messages.TryGetValue(a.OrderMessageId, out var order) || a.PostedTick != order.SubmittedTick || a.Deadline <= a.PostedTick) throw new ArgumentException("Invalid acknowledgment watch.");
                long baseTick = checked(a.PostedTick + Authority.UnacknowledgedTicks);
                if (a.OffDutyExtension != AuthorityEstimates.OffDutyExtension(a.PostedTick, baseTick, ExpectedRoster(a.RosterVersion, attempt.ActorId), Routines?.CheckInWindow ?? 0) || a.Deadline != checked(baseTick + a.OffDutyExtension)) throw new ArgumentException("Invalid acknowledgment deadline.");
                if (a.ResponseMessageId != 0 && (!messages.TryGetValue(a.ResponseMessageId, out var response) || response.Draft.AttemptId != a.AttemptId || !(response.Draft.Kind is MessageKind.OrderAccepted or MessageKind.OrderRejected))) throw new ArgumentException("Invalid acknowledgment response.");
                acknowledgmentWatches.Add(a.AttemptId, a);
                ValidateAuthorityEvent(ScheduledKind.AcknowledgmentDue, a.AttemptId, a.Deadline, a.Status == AcknowledgmentStatus.Awaiting);
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var e = new ReportExpectationSnapshot { AttemptId = r.ReadInt64(), JobId = r.ReadInt64(), ActorId = r.ReadInt64(), Role = r.ReadString(), TaskKind = r.ReadString(), AcknowledgmentMessageId = r.ReadInt64(), AcknowledgmentTick = r.ReadInt64(),
                    OriginTerminalId = r.ReadInt64(), ReturnTerminalId = r.ReadInt64(), UnscaledEstimate = r.ReadInt64(), RatioAtCreation = r.ReadInt64(), Slack = r.ReadInt64(), BaseDeadline = r.ReadInt64(), OffDutyExtension = r.ReadInt64(),
                    Deadline = r.ReadInt64(), RosterVersion = r.ReadInt64(), Status = ReadEnum<ExpectationStatus>(r), Reason = r.ReadString(), ResultMessageId = r.ReadInt64(), ResultTick = r.ReadInt64() };
                if (!workAttempts.TryGetValue(e.AttemptId, out var attempt) || attempt.JobId != e.JobId || attempt.ActorId != e.ActorId || e.TaskKind != jobs[e.JobId].Kind.ToString() || e.Role != registrations[e.ActorId].Role || e.UnscaledEstimate < 0 || e.RatioAtCreation < 0 ||
                    !messages.TryGetValue(e.AcknowledgmentMessageId, out var ack) || ack.Draft.Kind != MessageKind.OrderAccepted || ack.Draft.AttemptId != e.AttemptId || ack.SenderAccount.Value != attempt.AccountId || ack.DeliveryTick != e.AcknowledgmentTick || ack.TerminalId != e.OriginTerminalId)
                    throw new ArgumentException("Invalid expectation evidence.");
                if (e.UnscaledEstimate > 0)
                {
                    var deadline = AuthorityEstimates.Deadline(e.AcknowledgmentTick, e.UnscaledEstimate, e.RatioAtCreation, Authority, ExpectedRoster(e.RosterVersion, e.ActorId), Routines?.CheckInWindow ?? 0);
                    if (deadline.Tick != e.Deadline || deadline.BaseTick != e.BaseDeadline || deadline.Slack != e.Slack || deadline.OffDutyExtension != e.OffDutyExtension || !publicKnowledge.Services.Any(t => t.Id == e.ReturnTerminalId)) throw new ArgumentException("Invalid estimated deadline.");
                }
                else if (e.Deadline != -1 || e.BaseDeadline != -1 || e.Status is ExpectationStatus.AwaitingResult or ExpectationStatus.Overdue) throw new ArgumentException("Unestimated expectation has a watchdog.");
                if (e.ResultMessageId != 0)
                {
                    if (!messages.TryGetValue(e.ResultMessageId, out var result) || result.Draft.AttemptId != e.AttemptId || result.SenderAccount.Value != attempt.AccountId || !(result.Draft.Kind is MessageKind.WorkCompleted or MessageKind.WorkBlocked or MessageKind.UnableToContinue) || result.DeliveryTick != e.ResultTick || e.ResultTick < e.AcknowledgmentTick || !(e.Status is ExpectationStatus.Satisfied or ExpectationStatus.Cancelled)) throw new ArgumentException("Invalid satisfying report.");
                }
                else if (e.ResultTick != -1 || e.Status == ExpectationStatus.Satisfied) throw new ArgumentException("Missing satisfying report.");
                expectations.Add(e.AttemptId, e); ValidateAuthorityEvent(ScheduledKind.ReportExpectationDue, e.AttemptId, e.Deadline, e.Status == ExpectationStatus.AwaitingResult);
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var incident = new MissingReportIncident { Id = r.ReadInt64(), AttemptId = r.ReadInt64(), JobId = r.ReadInt64(), ActorId = r.ReadInt64(), Cohort = r.ReadInt32(), OpenedTick = r.ReadInt64(), OpeningSequence = r.ReadInt64(), Kind = r.ReadString(), ResponseMessageId = r.ReadInt64() };
                if (incident.Id < 1 || incident.Id >= nextMissingReport || !expectations.TryGetValue(incident.AttemptId, out var e) || e.JobId != incident.JobId || e.ActorId != incident.ActorId || e.Deadline != incident.OpenedTick || incident.OpeningSequence < 1 || incident.OpeningSequence >= nextSequence || incident.Kind != "MissingWorkReport" || incident.Cohort < -1 || incident.Cohort >= (Routines?.Cohorts ?? 0) || incident.ResponseMessageId != e.ResultMessageId) throw new ArgumentException("Invalid missing-report incident.");
                missingReports.Add(incident.AttemptId, incident);
            }
            if (nextMissingReport != missingReports.Count + 1L || missingReports.Values.Select(i => i.Id).Distinct().Count() != missingReports.Count) throw new ArgumentException("Invalid incident sequence.");
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var learned = new LearnedDurationSnapshot { Role = r.ReadString(), TaskKind = r.ReadString(), Ratio = r.ReadInt64(), Samples = r.ReadInt64() };
                var samples = expectations.Values.Where(e => e.Role == learned.Role && e.TaskKind == learned.TaskKind && e.ResultMessageId > 0 && e.UnscaledEstimate > 0).OrderBy(e => e.ResultTick).ThenBy(e => e.ResultMessageId).ToArray();
                long ratio = 1000; foreach (var sample in samples) ratio = AuthorityEstimates.Learn(ratio, sample.AcknowledgmentTick, sample.ResultTick, sample.UnscaledEstimate, Authority);
                if (samples.Length == 0 || learned.Samples != samples.Length || learned.Ratio != ratio) throw new ArgumentException("Learned ratio does not match received samples.");
                learnedDurations.Add((learned.Role, learned.TaskKind), learned);
            }
            if (expectations.Values.Any(e => e.ResultMessageId > 0 && e.UnscaledEstimate > 0 && !learnedDurations.ContainsKey((e.Role, e.TaskKind)))) throw new ArgumentException("Missing learned ratio.");
            foreach (var e in scheduled.Where(e => e.Kind is ScheduledKind.ReportExpectationDue or ScheduledKind.AcknowledgmentDue))
                if (e.Kind == ScheduledKind.ReportExpectationDue ? !expectations.ContainsKey(e.Value) : !acknowledgmentWatches.ContainsKey(e.Value)) throw new ArgumentException("Unknown authority watchdog owner.");
            ReadPatrols(r); ReadIncidentResponses(r);
        }
        private void ValidateAuthorityEvent(ScheduledKind kind, long attempt, long deadline, bool pending)
        {
            var events = scheduled.Where(e => e.Kind == kind && e.Value == attempt).ToArray();
            if (RuntimeError == null && (events.Length != (pending ? 1 : 0) || pending && deadline <= LastProcessedTick) || events.Length > 1 || events.Any(e => e.Tick != deadline || e.Phase != EventPhase.Decision)) throw new ArgumentException("Invalid authority deadline watchdog.");
        }
    }
}
