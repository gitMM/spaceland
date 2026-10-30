using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    public enum UrgentNeedStep { LowerNeed, KnownUrgentOrder, Plan }
    public enum CriticalPreemption { None, InterruptEscort, StopAtBoundary, CancelNow }

    // Own-need priority: when urgent or critical needs preempt other goals, which
    // need relief is sought first and when lower (optional) needs may start.
    public static class NeedPriority
    {
        private static readonly PriorityGate[] LowerNeed = { PriorityGate.KnownUrgentOrder, PriorityGate.UrgentNeed, PriorityGate.Recovery };
        private static readonly PriorityGate[] PreSleep = { PriorityGate.KnownUrgentOrder, PriorityGate.Recovery };

        public static bool Relieves(ActionKind kind, NeedKind need) => kind == ActionKind.Eat && need == NeedKind.Hunger || kind == ActionKind.UseToilet && need == NeedKind.Bladder || kind == ActionKind.Sleep && (need is NeedKind.SleepPressure or NeedKind.WorkStrain) || kind == ActionKind.Rest && need == NeedKind.WorkStrain;
        // The most pressing urgent need: highest band, then need kind.
        public static NeedValue? UrgentNeed(IEnumerable<NeedValue> needs) => needs.Where(v => v.Band >= NeedBand.Urgent).OrderByDescending(v => v.Band).ThenBy(v => v.Kind).FirstOrDefault();
        // A failed bounded urgent attempt yields until its private retry clock expires.
        public static bool UrgentRetryPending(long urgentRetryAt, long now) => urgentRetryAt > now;
        // An urgent need preempts ordinary decisions unless a known urgent order holds it
        // back; a critical need always does.
        public static bool Preempts(NeedValue? need, bool urgentOrder, Func<bool> knownUrgentOrder) =>
            !(need == null || need.Band != NeedBand.Critical && (urgentOrder || knownUrgentOrder()));
        public static UrgentNeedStep UrgentStep(NeedValue? need, long urgentRetryAt, long now, Func<bool> knownUrgentOrder)
        {
            if (need == null || UrgentRetryPending(urgentRetryAt, now)) return UrgentNeedStep.LowerNeed;
            if (need.Band != NeedBand.Critical && knownUrgentOrder()) return UrgentNeedStep.KnownUrgentOrder;
            return UrgentNeedStep.Plan;
        }
        // Needs to seek relief for, in order. One bounded request can reject an
        // unavailable higher need and seek useful relief for another. Urgent (not
        // critical) sleep first relieves a need forecast to become critical in bed; that
        // need is placed first even when it is also listed later.
        public static List<NeedValue> UrgentCandidates(IReadOnlyList<NeedValue> needs, Func<bool> knownUrgentOrder, Func<NeedKind?> preSleepRelief, out NeedKind? preSleep)
        {
            var candidates = needs.Where(v => v.Band >= NeedBand.Urgent && (v.Band == NeedBand.Critical || !knownUrgentOrder())).OrderByDescending(v => v.Band).ThenBy(v => v.Kind).ToList();
            preSleep = candidates.FirstOrDefault()?.Kind == NeedKind.SleepPressure && candidates[0].Band != NeedBand.Critical ? preSleepRelief() : null;
            if (preSleep.HasValue) { var kind = preSleep.Value; candidates.Insert(0, needs.Single(v => v.Kind == kind)); }
            return candidates;
        }
        // Whether the planned need is still worth relieving when its step is about to start.
        public static bool StillSought(NeedValue need, NeedKind goal, Func<NeedKind?> preSleepRelief) => need.Kind == goal && (need.Band >= NeedBand.Seek || preSleepRelief() == need.Kind);
        // An urgent step yields to a known urgent order unless critical; a lower-need step
        // rechecks whether it may start.
        public static bool StartBlocked(NeedValue? need, Func<bool> knownUrgentOrder, Func<bool> lowerNeedMayStart) =>
            need == null || (need.Band >= NeedBand.Urgent ? need.Band != NeedBand.Critical && knownUrgentOrder() : !lowerNeedMayStart());
        // Urgent sleep pressure whose unmet relief may fall back to rest.
        public static NeedValue? UrgentSleep(IEnumerable<NeedValue> needs, Func<bool> knownUrgentOrder) =>
            needs.FirstOrDefault(v => v.Kind == NeedKind.SleepPressure && v.Band >= NeedBand.Urgent && (v.Band == NeedBand.Critical || !knownUrgentOrder()));
        // After a failed request: own backoff, but no later than a known failed service's retry.
        public static long RetryAt(long now, long backoff, IEnumerable<long> failureRetries)
        {
            long retry = checked(now + backoff);
            foreach (long failure in failureRetries) retry = Math.Min(retry, failure);
            return retry;
        }
        // Whether any own need goal may be worth a decision.
        public static bool MayHaveNeedGoal(bool urgentNeed, long toiletSequence, Func<bool> optionalRest, Func<bool> conversation) =>
            urgentNeed || toiletSequence > 0 || optionalRest() || conversation();
        // Optional rest or conversation runs only when nothing else is pending.
        public static bool OptionalReliefDefers(long retryAt, long now, Func<bool> opportunity, Func<bool> knownUrgentOrder, Func<bool> ordinaryWork) =>
            !(retryAt > now || !opportunity() || knownUrgentOrder() || ordinaryWork());
        // The lower need to seek: pre-sleep relief, a due toilet visit, optional rest, else conversation.
        public static NeedKind LowerNeedGoal(NeedKind? preSleep, DueSource due, Func<bool> optionalRest) =>
            preSleep ?? (due == DueSource.Toilet ? NeedKind.Bladder : optionalRest() ? NeedKind.WorkStrain : NeedKind.Social);
        // Pre-sleep relief deliberately precedes urgent (never critical) sleep pressure.
        public static bool IsPreSleepGoal(NeedKind goal, NeedKind? preSleep, Func<bool> criticalNeed) => preSleep == goal && !criticalNeed();
        public static IReadOnlyList<PriorityGate> LowerNeedGates(bool preSleepGoal) => preSleepGoal ? PreSleep : LowerNeed;
        // After the gates pass: a toilet visit runs when it is the selected due goal; optional
        // rest and conversation run only with no due goal and no ready on-duty work.
        public static bool LowerNeedMayStart(NeedKind goal, bool preSleepGoal, Func<DueSource> due, Func<bool> atSeek, Func<bool> optionalRest, Func<bool> conversation, Func<bool> ordinaryWork)
        {
            if (preSleepGoal) return true;
            var selected = due();
            if (goal == NeedKind.Bladder) return atSeek() && selected == DueSource.Toilet;
            return (goal == NeedKind.WorkStrain && optionalRest() || goal == NeedKind.Social && conversation()) && selected == DueSource.None && !ordinaryWork();
        }
        // A newly critical need interrupts an escort the citizen guards, or an unrelated
        // own action (movement stops at the next tile boundary).
        public static CriticalPreemption CriticalResponse(bool escorted, bool escortSubject, bool escortMoving, ActionKind? action, NeedKind critical, Func<bool> travellingForNeed)
        {
            if (escorted) return !escortSubject && escortMoving ? CriticalPreemption.InterruptEscort : CriticalPreemption.None;
            if (!action.HasValue || Relieves(action.Value, critical) || travellingForNeed()) return CriticalPreemption.None;
            return action.Value == ActionKind.Move ? CriticalPreemption.StopAtBoundary : CriticalPreemption.CancelNow;
        }
        // Before committing to the known bed duration, own hunger or bladder that would
        // become critical while asleep is relieved first. Only own needs, configured
        // sleeping rates and known bed durations are used. A failed relief backs off and
        // yields to sleep, so this cannot keep a citizen awake indefinitely. Sleep is
        // imminent through the roster's sleep opportunity or urgent sleep pressure.
        public static NeedKind? PreSleepRelief(long retryAt, long now, bool asleep, Func<bool> sleepOpportunity, Func<IReadOnlyList<NeedValue>> needs, Func<IActorView> view, IReadOnlyList<NeedRule> rules, long tickDurationMs)
        {
            if (retryAt > now || asleep) return null;
            if (!sleepOpportunity() && !needs().Any(n => n.Kind == NeedKind.SleepPressure && n.Band >= NeedBand.Urgent)) return null;
            var v = view();
            long sleep = v.NeedServices.Where(s => s.Kind == NeedServiceKind.Bed && s.DetentionFacilityId == 0 && (s.Id == v.OwnBerthId || s.AssignedActorId == v.Actor.Value))
                .Select(s => s.DurationTicks).DefaultIfEmpty(0).Max();
            if (sleep <= 0) return null;
            foreach (var need in v.Needs.Where(n => n.Kind is NeedKind.Hunger or NeedKind.Bladder).OrderByDescending(n => n.Band).ThenBy(n => n.Kind))
            {
                var rule = rules[(int)need.Kind];
                if (rule.Critical > 0 && need.Band < NeedBand.Critical && v.NeedServices.Any(s => s.DetentionFacilityId == 0 && NeedPlanning.Serves(s.Kind, need.Kind)) &&
                    new NeedAccumulator(need.Q, now, rule.SleepingRate).At(checked(now + sleep), tickDurationMs) >= rule.Critical * NeedAccumulator.HourMilliseconds) return need.Kind;
            }
            return null;
        }
    }
}
