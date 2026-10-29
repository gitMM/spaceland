using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Station.Contracts;
using Station.Mind;

namespace Station.Core
{
    public sealed partial class World
    {
        private void WriteDailyGoals(BinaryWriter w)
        {
            w.Write(nextGoalSequence); w.Write(dailySchedules.Count);
            foreach (var s in dailySchedules.Values) { w.Write(s.ActorId); w.Write(s.RosterVersion); w.Write(s.NextBoundary); w.Write(s.NextDecision); }
            w.Write(dailyGoals.Count);
            foreach (var g in dailyGoals.Values)
            {
                w.Write(g.Id); w.Write(g.ActorId); w.Write(g.RosterVersion); w.Write((int)g.Kind); w.Write(g.Opens); w.Write(g.DueTick); w.Write(g.Closes); w.Write((int)g.Status);
                w.Write(g.RetryAt); w.Write(g.Backoff); w.Write(g.Reason); w.Write(g.ServiceId); w.Write(g.ActiveActionId); w.Write(g.CompletedActionId); w.Write(g.EndedTick);
            }
        }
        private void ReadDailyGoals(BinaryReader r)
        {
            nextGoalSequence = r.ReadInt64(); if (nextGoalSequence < 1) throw new ArgumentException("Invalid due goal sequence.");
            var seen = new SortedSet<long>();
            void ValidateStamp(GoalStamp stamp)
            { if (stamp.Due < 0 || stamp.Due > NeedNow || stamp.Sequence < 1 || stamp.Sequence >= nextGoalSequence || !seen.Add(stamp.Sequence)) throw new ArgumentException("Invalid due goal stamp."); }
            foreach (var agent in reportAgents.Values)
            {
                if (agent.Pending.Count != agent.PendingStamps.Count) throw new ArgumentException("Report goal stamps do not match queue.");
                foreach (var stamp in agent.PendingStamps) ValidateStamp(stamp);
            }
            foreach (var agent in needAgents.Values.Where(a => a.ToiletSequence > 0)) ValidateStamp(new GoalStamp(agent.ToiletDueTick, agent.ToiletSequence));
            foreach (var check in checkIns.Values)
            {
                if (check.Stamp.Sequence > 0) ValidateStamp(check.Stamp);
                else if (check.Due || check.Stamp.Due != 0) throw new ArgumentException("Due check-in lacks its original goal stamp.");
                if (check.BlockedUntil < 0) throw new ArgumentException("Invalid check-in retry clock.");
            }
            int count = Count(r);
            for (int i = 0; i < count; i++)
            {
                var s = new DailySchedule { ActorId = r.ReadInt64(), RosterVersion = r.ReadInt64(), NextBoundary = r.ReadInt64(), NextDecision = r.ReadInt64() };
                var actor = new Id<ActorTag>(s.ActorId);
                if (!needAgents.ContainsKey(s.ActorId) || !routines.TryGetValue(actor, out var routine) || routine.KnownRoster?.Version != s.RosterVersion || s.NextBoundary <= NeedNow || s.NextDecision < -1) throw new ArgumentException("Invalid daily schedule owner.");
                var windows = DailyRoutine.Around(routine.KnownRoster!, NeedNow);
                if (s.NextBoundary != windows.SelectMany(w => new[] { w.Opens, w.Closes }).Where(t => t > NeedNow).Min()) throw new ArgumentException("Invalid daily calendar boundary.");
                ValidateRoutineEvent(actor, ScheduledKind.DailyBoundary, s.NextBoundary); ValidateRoutineEvent(actor, ScheduledKind.EvaluateDaily, s.NextDecision);
                dailySchedules.Add(s.ActorId, s);
            }
            count = Count(r); var active = new SortedSet<long>(); var keys = new SortedSet<(long Actor, long Roster, RoutineOpportunityKind Kind, long Opens)>();
            for (int i = 0; i < count; i++)
            {
                var g = new DailyGoalSnapshot { Id = r.ReadInt64(), ActorId = r.ReadInt64(), RosterVersion = r.ReadInt64(), Kind = ReadEnum<RoutineOpportunityKind>(r), Opens = r.ReadInt64(), DueTick = r.ReadInt64(), Closes = r.ReadInt64(), Status = ReadEnum<DailyGoalStatus>(r),
                    RetryAt = r.ReadInt64(), Backoff = r.ReadInt64(), Reason = r.ReadString(), ServiceId = r.ReadInt64(), ActiveActionId = r.ReadInt64(), CompletedActionId = r.ReadInt64(), EndedTick = r.ReadInt64() };
                ValidateStamp(new GoalStamp(g.DueTick, g.Id));
                if (!dailySchedules.TryGetValue(g.ActorId, out var schedule) || !rosters.TryGetValue(g.RosterVersion, out var roster) || roster.Roster.ActorId != g.ActorId ||
                    g.DueTick != Math.Max(0, g.Opens) || g.Closes <= g.Opens || g.RetryAt < 0 || g.Backoff < planner.InitialBackoff || g.Backoff > planner.MaximumBackoff ||
                    g.ServiceId < 0 || g.ServiceId > 0 && (!needServices.TryGetValue(g.ServiceId, out var service) || !(NeedPlanning.Serves(service.Definition.Kind, g.Need) || IsSleepFallback(g.Need, g.ServiceId))) ||
                    !keys.Add((g.ActorId, g.RosterVersion, g.Kind, g.Opens)) ||
                    !DailyRoutine.Around(roster.Roster, Math.Max(0, g.Opens)).Any(w => w.Kind == g.Kind && w.Opens == g.Opens && w.Closes == g.Closes)) throw new ArgumentException("Invalid roster opportunity goal.");
                if (g.Status == DailyGoalStatus.Pending ? g.EndedTick != -1 || schedule.RosterVersion != g.RosterVersion : g.EndedTick < g.DueTick || g.EndedTick > LastProcessedTick) throw new ArgumentException("Invalid daily goal lifecycle.");
                if (g.Status == DailyGoalStatus.Completed ? IsSleepFallback(g.Need, g.ServiceId) || !needActivities.TryGetValue(g.CompletedActionId, out var activity) || activity.ActorId != g.ActorId || activity.ServiceId != g.ServiceId || activity.Status != NeedActivityStatus.Completed || activity.EndedTick != g.EndedTick : g.CompletedActionId != 0) throw new ArgumentException("Daily completion lacks physical activity.");
                if (g.ActiveActionId < 0 || g.ActiveActionId >= nextAction) throw new ArgumentException("Invalid daily action identity.");
                if (g.ActiveActionId > 0)
                {
                    var action = actors[new Id<ActorTag>(g.ActorId)].Action;
                    if (g.Status != DailyGoalStatus.Pending || !active.Add(g.ActorId) || action?.Id.Value != g.ActiveActionId || action.ServiceId != g.ServiceId || IsSleepFallback(g.Need, g.ServiceId) && g.Reason != UnmetSleepReason ||
                        !(action.Kind == ActionKind.Move || action.Kind == ActionKind.AllocateBerth && g.Need == NeedKind.SleepPressure || action.Kind == needServices[g.ServiceId].Definition.Action) || action.Target != needServices[g.ServiceId].Definition.Position || needAgents[g.ActorId].ActiveActionId == g.ActiveActionId) throw new ArgumentException("Daily goal does not own its physical action.");
                }
                AddDailyGoal(g);
            }
            if (scheduled.Any(e => e.Kind == ScheduledKind.DailyCommand || (e.Kind is ScheduledKind.DailyBoundary or ScheduledKind.EvaluateDaily) && !dailySchedules.ContainsKey(e.Intent.Actor.Value))) throw new ArgumentException("Unregistered daily event.");
            if (needAgents.Keys.Where(a => routines.TryGetValue(new Id<ActorTag>(a), out var routine) && routine.KnownRoster != null).Any(a => !dailySchedules.ContainsKey(a))) throw new ArgumentException("Missing known daily calendar.");
        }
    }
}
