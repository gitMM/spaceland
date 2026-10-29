using System;
using System.IO;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteNeedAgents(BinaryWriter w)
        {
            w.Write(needAgents.Count);
            foreach (var a in needAgents.Values)
            {
                w.Write(a.ActorId); w.Write(a.NextDecision); w.Write(a.RetryAt); w.Write(a.Backoff); w.Write((int)a.Goal);
                w.Write(a.ServiceId); w.Write(a.ActiveActionId); w.Write(a.PlannedTick); w.Write(a.CompletedActivities); w.Write(a.BlockedReason); w.Write((int)a.LastOutcome); w.Write(a.LastCost); w.Write(a.SleepFallbackRetryAt); w.Write(a.ToiletDueTick); w.Write(a.ToiletSequence); w.Write(a.UrgentRetryAt); w.Write(a.ConversationCursor);
                w.Write(a.KnownFailures.Count);
                foreach (var f in a.KnownFailures.Values) { w.Write(f.ServiceId); w.Write(f.ActionId); w.Write(f.Tick); w.Write(f.RetryAt); w.Write((int)f.Reason); }
            }
        }
        private void ReadNeedAgents(BinaryReader r)
        {
            int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var a = new NeedAgentSnapshot { ActorId = r.ReadInt64(), NextDecision = r.ReadInt64(), RetryAt = r.ReadInt64(), Backoff = r.ReadInt64(), Goal = ReadEnum<NeedKind>(r),
                    ServiceId = r.ReadInt64(), ActiveActionId = r.ReadInt64(), PlannedTick = r.ReadInt64(), CompletedActivities = r.ReadInt64(), BlockedReason = r.ReadString(), LastOutcome = ReadEnum<PlanOutcome>(r), LastCost = r.ReadInt64(), SleepFallbackRetryAt = r.ReadInt64(), ToiletDueTick = r.ReadInt64(), ToiletSequence = r.ReadInt64(), UrgentRetryAt = r.ReadInt64(), ConversationCursor = r.ReadInt64() };
                if (!actorNeeds.ContainsKey(a.ActorId) || a.NextDecision < -1 || a.RetryAt < 0 || a.Backoff < planner.InitialBackoff || a.Backoff > planner.MaximumBackoff ||
                    a.ActiveActionId < 0 || a.ActiveActionId >= nextAction || a.ServiceId < 0 || a.ServiceId > 0 && !needServices.ContainsKey(a.ServiceId) ||
                    a.PlannedTick < -1 || a.PlannedTick > LastProcessedTick || a.CompletedActivities < 0 || a.LastCost < 0 || a.SleepFallbackRetryAt < 0 || a.SleepFallbackRetryAt > checked(NeedNow + planner.MaximumBackoff) || a.UrgentRetryAt < 0 || a.UrgentRetryAt > checked(NeedNow + planner.MaximumBackoff) ||
                    a.CompletedActivities > needActivities.Values.Count(x => x.ActorId == a.ActorId && x.Status == NeedActivityStatus.Completed) + conversations.Values.Count(c => c.Status == ConversationStatus.Completed && (c.FirstActorId == a.ActorId || c.SecondActorId == a.ActorId)) || a.ConversationCursor < 0 || a.ConversationCursor > 0 && !actors.ContainsKey(new Id<ActorTag>(a.ConversationCursor))) throw new ArgumentException("Invalid autonomous need state.");
                var actor = actors[new Id<ActorTag>(a.ActorId)];
                if (a.ToiletDueTick < -1 || a.ToiletDueTick > NeedNow || a.ToiletSequence < 0 || (a.ToiletSequence == 0 ? a.ToiletDueTick != -1 : a.ToiletDueTick < 0) ||
                    (a.ToiletSequence > 0) != AtSeek(actor.Id, NeedKind.Bladder)) throw new ArgumentException("Invalid retained toilet goal.");
                if (a.SleepFallbackRetryAt > 0 && !needActivities.Values.Any(x => x.ActorId == a.ActorId && x.Kind == ActionKind.Rest && x.Status == NeedActivityStatus.Completed &&
                    a.SleepFallbackRetryAt - x.EndedTick >= planner.InitialBackoff && a.SleepFallbackRetryAt - x.EndedTick <= planner.MaximumBackoff)) throw new ArgumentException("Sleep fallback retry lacks completed rest.");
                if (a.ActiveActionId > 0 && actor.Action?.Kind == ActionKind.Converse)
                {
                    if (actor.Action.Id.Value != a.ActiveActionId || a.Goal != NeedKind.Social || a.ServiceId != 0 || !conversationClaims.ContainsKey(a.ActorId) || a.PlannedTick != actor.Action.Started) throw new ArgumentException("Invalid owned conversation action.");
                }
                else if (a.ActiveActionId > 0 && (actor.Action?.Id.Value != a.ActiveActionId || actor.Action.ServiceId != a.ServiceId || a.ServiceId == 0 ||
                    !(NeedPlanning.Serves(needServices[a.ServiceId].Definition.Kind, a.Goal) || IsSleepFallback(a.Goal, a.ServiceId) && a.BlockedReason == UnmetSleepReason) || !(actor.Action.Kind == ActionKind.Move || actor.Action.Kind == ActionKind.AllocateBerth && a.Goal == NeedKind.SleepPressure || actor.Action.Kind == needServices[a.ServiceId].Definition.Action) ||
                    actor.Action.Target != needServices[a.ServiceId].Definition.Position || a.PlannedTick < 0 || a.PlannedTick > actor.Action.Started)) throw new ArgumentException("Need plan does not own its physical action.");
                int failures = Count(r);
                for (int j = 0; j < failures; j++)
                {
                    var f = new NeedServiceFailureSnapshot { ServiceId = r.ReadInt64(), ActionId = r.ReadInt64(), Tick = r.ReadInt64(), RetryAt = r.ReadInt64(), Reason = ReadEnum<FailureReason>(r) };
                    if (!needServices.ContainsKey(f.ServiceId) || f.ActionId < 1 || f.ActionId >= nextAction || f.Tick < 0 || f.Tick > LastProcessedTick || f.RetryAt <= f.Tick ||
                        f.RetryAt - f.Tick < planner.InitialBackoff || f.RetryAt - f.Tick > planner.MaximumBackoff || f.Reason == FailureReason.None) throw new ArgumentException("Invalid private need failure memory.");
                    a.KnownFailures.Add(f.ServiceId, f);
                }
                var timers = scheduled.Where(e => e.Kind == ScheduledKind.EvaluateNeed && e.Intent.Actor.Value == a.ActorId).ToArray();
                if (timers.Length != (a.NextDecision < 0 ? 0 : 1) || timers.Any(e => e.Tick != a.NextDecision || e.Phase != EventPhase.Decision) ||
                    a.NextDecision >= 0 && (a.NextDecision <= LastProcessedTick || a.NextDecision < a.RetryAt && !(UrgentNeed(actor.Id) != null && (a.UrgentRetryAt <= NeedNow || a.NextDecision == a.UrgentRetryAt)))) throw new ArgumentException("Invalid autonomous need decision timer.");
                needAgents.Add(a.ActorId, a);
            }
            if (scheduled.Any(e => e.Kind == ScheduledKind.NeedCommand || e.Kind == ScheduledKind.EvaluateNeed && !needAgents.ContainsKey(e.Intent.Actor.Value))) throw new ArgumentException("Unregistered autonomous need command.");
        }
    }
}
