using System;
using System.IO;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed partial class World
    {
        private bool ValidJobTarget(JobSnapshot job) => job.Kind == WorkKind.ConstructHabitat ? job.MachineId == 0 && job.TileId == 0 && job.ReportedUnits == 0 && constructionProjects.ContainsKey(job.ProjectId) : job.ProjectId == 0 && (job.Kind != WorkKind.CleanTile ?
            job.TileId == 0 && job.ReportedUnits == 0 && machines.ContainsKey(job.MachineId) :
            job.MachineId == 0 && job.ReportedUnits >= 0 && Topology.ById.TryGetValue(new Id<TileTag>(job.TileId), out var tile) && tile.Completed && Topology.Definitions[tile.DefinitionId].Capabilities.Contains("Cleanable", StringComparer.Ordinal));
        private void WriteJobs(BinaryWriter w)
        {
            w.Write(JobsEnabled);
            w.Write(nextAttempt); w.Write(nextJob); w.Write(workVersion); w.Write(jobs.Count);
            foreach (var j in jobs.Values) { w.Write(j.Id); w.Write(j.ProjectId); w.Write(j.MachineId); w.Write((int)j.Kind); w.Write(j.TileId); w.Write(j.ReportedUnits); w.Write(j.CurrentAttemptId); w.Write((int)j.PhysicalStatus); w.Write(j.AuthorityStatus); }
            w.Write(workAttempts.Count);
            foreach (var a in workAttempts.Values)
            {
                w.Write(a.Id); w.Write(a.JobId); w.Write(a.ActorId); w.Write(a.AccountId); w.Write(a.DispatchKey); w.Write(a.DispatchTick); w.Write(a.Expires); w.Write(a.OrderMessageId);
                w.Write((int)a.Assignment); w.Write((int)a.Execution); w.Write(a.AcknowledgmentReceivedTick.HasValue); if (a.AcknowledgmentReceivedTick.HasValue) w.Write(a.AcknowledgmentReceivedTick.Value);
                w.Write(a.AuthorityStatus); w.Write(a.ReportMessageIds.Count); foreach (long id in a.ReportMessageIds) w.Write(id);
            }
            w.Write(cancellationKeys.Count); foreach (var p in cancellationKeys) { w.Write(p.Key); w.Write(p.Value); }
            w.Write(workMessageKeys.Count); foreach (var p in workMessageKeys) { w.Write(p.Key); w.Write(p.Value); }
            w.Write(localOrders.Count);
            foreach (var o in localOrders.Values)
            {
                w.Write(o.AttemptId); w.Write(o.JobId); w.Write(o.ActorId); w.Write(o.ProjectId); w.Write(o.MachineId); w.Write((int)o.Kind); w.Write(o.TileId); w.Write(o.CleanupChecked); w.Write(o.CleanedUnits); w.Write(o.OrderMessageId); w.Write(o.ReadTick); w.Write(o.Expires);
                w.Write((int)o.Status); w.Write(o.Reason); w.Write(o.NextDecision); w.Write(o.BlockedReported); w.Write(o.Backoff); w.Write(o.CompletionCauseId); w.Write(o.CompletionObservedTick);
            }
            w.Write(workHistory.Count);
            foreach (var h in workHistory) { w.Write(h.Version); w.Write(h.Tick); w.Write(h.JobId); w.Write(h.AttemptId); w.Write(h.MessageId); w.Write(h.Kind); }
            w.Write(checkIns.Count);
            foreach (var c in checkIns.Values) { w.Write(c.Actor.Value); w.Write(c.Interval); w.Write(c.NextDecision); w.Write(c.DueTick); w.Write(c.Due); w.Write(c.Completed); w.Write(c.Backoff); w.Write(c.Stamp.Due); w.Write(c.Stamp.Sequence); w.Write(c.BlockedUntil); }
            w.Write(ownCompletionPosts.Count); foreach (var p in ownCompletionPosts) { w.Write(p.Actor); w.Write(p.Job); w.Write(p.Attempt); }
        }
        private void ReadJobs(BinaryReader r)
        {
            JobsEnabled = r.ReadBoolean();
            if (JobsEnabled && Information == null) throw new ArgumentException("Saved jobs require work and information.");
            nextAttempt = r.ReadInt64(); nextJob = r.ReadInt64(); workVersion = r.ReadInt64(); int count = Count(r);
            for (int i = 0; i < count; i++) { var j = new JobSnapshot { Id = r.ReadInt64(), ProjectId = r.ReadInt64(), MachineId = r.ReadInt64(), Kind = ReadEnum<WorkKind>(r), TileId = r.ReadInt64(), ReportedUnits = r.ReadInt64(), CurrentAttemptId = r.ReadInt64(), PhysicalStatus = ReadEnum<WorkExecution>(r), AuthorityStatus = r.ReadString() }; jobs.Add(j.Id, j); }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var a = new WorkAttemptSnapshot { Id = r.ReadInt64(), JobId = r.ReadInt64(), ActorId = r.ReadInt64(), AccountId = r.ReadInt64(), DispatchKey = r.ReadString(), DispatchTick = r.ReadInt64(), Expires = r.ReadInt64(), OrderMessageId = r.ReadInt64(), Assignment = ReadEnum<AssignmentStatus>(r), Execution = ReadEnum<WorkExecution>(r) };
                a.AcknowledgmentReceivedTick = r.ReadBoolean() ? r.ReadInt64() : (long?)null; a.AuthorityStatus = r.ReadString(); int n = Count(r); var reports = new long[n]; for (int j = 0; j < n; j++) reports[j] = r.ReadInt64(); a.ReportMessageIds = reports;
                if (a.Id < 1 || a.Id >= nextAttempt || !jobs.ContainsKey(a.JobId) || !actors.TryGetValue(new Id<ActorTag>(a.ActorId), out var actor) || actor.Account.Value != a.AccountId || a.Expires <= a.DispatchTick || string.IsNullOrWhiteSpace(a.DispatchKey)) throw new ArgumentException("Invalid saved attempt.");
                workAttempts.Add(a.Id, a); assignmentKeys.Add(a.DispatchKey, a.Id);
            }
            count = Count(r); for (int i = 0; i < count; i++) cancellationKeys.Add(r.ReadString(), r.ReadInt64());
            count = Count(r); for (int i = 0; i < count; i++) workMessageKeys.Add(r.ReadString(), r.ReadInt64());
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var o = new LocalWorkOrderSnapshot { AttemptId = r.ReadInt64(), JobId = r.ReadInt64(), ActorId = r.ReadInt64(), ProjectId = r.ReadInt64(), MachineId = r.ReadInt64(), Kind = ReadEnum<WorkKind>(r), TileId = r.ReadInt64(), CleanupChecked = r.ReadBoolean(), CleanedUnits = r.ReadInt64(), OrderMessageId = r.ReadInt64(), ReadTick = r.ReadInt64(), Expires = r.ReadInt64(), Status = ReadEnum<LocalOrderStatus>(r), Reason = r.ReadString(), NextDecision = r.ReadInt64(), BlockedReported = r.ReadBoolean(), Backoff = r.ReadInt64(), CompletionCauseId = r.ReadInt64(), CompletionObservedTick = r.ReadInt64() };
                if (!workAttempts.TryGetValue(o.AttemptId, out var a) || a.JobId != o.JobId || a.ActorId != o.ActorId || jobs[o.JobId].ProjectId != o.ProjectId || jobs[o.JobId].MachineId != o.MachineId || jobs[o.JobId].Kind != o.Kind || jobs[o.JobId].TileId != o.TileId || o.CleanedUnits < 0 || a.OrderMessageId != o.OrderMessageId) throw new ArgumentException("Invalid private order copy.");
                if (o.CompletionCauseId < 0 || (o.CompletionCauseId == 0 ? o.CompletionObservedTick != -1 : o.Kind != WorkKind.ConstructHabitat || o.CompletionCauseId >= nextTrace || o.CompletionObservedTick < o.ReadTick || o.CompletionObservedTick > LastProcessedTick)) throw new ArgumentException("Invalid construction completion evidence."); localOrders.Add(o.AttemptId, o);
                if (!messages.TryGetValue(o.OrderMessageId, out var message) || message.Stage != MessageStage.Read || message.FirstReadTick != o.ReadTick ||
                    message.SenderAccount.Value != 0 || message.Draft.RecipientKind != RecipientKind.Account || message.Draft.Recipient.Value != a.AccountId || message.Draft.AttemptId != o.AttemptId || message.Draft.TaskId != o.JobId ||
                    message.Draft.WorkOrder == null || message.Draft.WorkOrder.Kind != o.Kind || message.Draft.WorkOrder.MachineId != o.MachineId || message.Draft.WorkOrder.Tile.Value != o.TileId ||
                    (message.Draft.WorkOrder.Construction?.ProjectId ?? 0) != o.ProjectId || message.Draft.WorkOrder.Expires != o.Expires || o.Expires != a.Expires ||
                    o.Kind != WorkKind.CleanTile && (o.CleanupChecked || o.CleanedUnits != 0)) throw new ArgumentException("Invalid local order read evidence.");
                if (o.Reason == "OwnCompletionAlreadyPosted" && (o.Status != LocalOrderStatus.Completed || o.NextDecision >= 0)) throw new ArgumentException("Invalid remembered completion order.");
            }
            count = Count(r);
            for (int i = 0; i < count; i++) workHistory.Add(new WorkHistoryEntry { Version = r.ReadInt64(), Tick = r.ReadInt64(), JobId = r.ReadInt64(), AttemptId = r.ReadInt64(), MessageId = r.ReadInt64(), Kind = r.ReadString() });
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var c = new CheckInAgent { Actor = new Id<ActorTag>(r.ReadInt64()), Interval = r.ReadInt64(), NextDecision = r.ReadInt64(), DueTick = r.ReadInt64(), Due = r.ReadBoolean(), Completed = r.ReadInt64(), Backoff = r.ReadInt64(), Stamp = new GoalStamp(r.ReadInt64(), r.ReadInt64()), BlockedUntil = r.ReadInt64() };
                if (!actors.ContainsKey(c.Actor) || c.Interval < 1) throw new ArgumentException("Invalid saved check-in schedule."); checkIns.Add(c.Actor, c);
            }
            count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var p = (r.ReadInt64(), r.ReadInt64(), r.ReadInt64());
                if (!actors.ContainsKey(new Id<ActorTag>(p.Item1)) || p.Item2 < 0 || p.Item3 < 0 || !ownCompletionPosts.Add(p)) throw new ArgumentException("Invalid remembered completion submission.");
            }
            if (localOrders.Values.Any(o => o.Reason == "OwnCompletionAlreadyPosted" && !ownCompletionPosts.Contains((o.ActorId, o.JobId, o.AttemptId)))) throw new ArgumentException("Missing remembered completion submission.");
            if (nextAttempt < 1 || workHistory.Count != workVersion || jobs.Values.Any(j => !ValidJobTarget(j) || j.CurrentAttemptId != 0 && !workAttempts.ContainsKey(j.CurrentAttemptId)) || cancellationKeys.Values.Any(id => !workAttempts.ContainsKey(id)) || workMessageKeys.Values.Any(id => !messages.ContainsKey(id))) throw new ArgumentException("Invalid work record indexes.");
            foreach (var agent in repairAgents.Values)
                if (agent.Attempts.Any(p => !agent.Pending.Contains(p.Key) || !localOrders.TryGetValue(p.Value, out var order) || order.ActorId != agent.Actor.Value || order.Kind != WorkKind.RepairMachine || order.MachineId != p.Key)) throw new ArgumentException("Invalid repair attempt binding.");
            if (nextJob < 1 || jobs.Keys.Any(id => id < 1 || id >= nextJob)) throw new ArgumentException("Invalid job ID sequence.");
        }
    }
}
