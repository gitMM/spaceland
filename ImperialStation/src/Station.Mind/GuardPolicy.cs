using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // A guard's own escort-preparation state machine. Core supplies the private
    // order copy and lazily gathered observations, then applies the chosen step.
    public static class GuardPolicy
    {
        public const string PreparingForEscortReason = "PreparingForEscort";
        public const string TeamNotReady = "TeamNotReady";
        public const string CriticalNeedReason = "CriticalNeed";

        public static bool Preparing(KnownGuardOrderStatus status) => status is KnownGuardOrderStatus.EquipmentPending or KnownGuardOrderStatus.AtRendezvous or
            KnownGuardOrderStatus.WaitingTeam or KnownGuardOrderStatus.Searching or KnownGuardOrderStatus.TargetLocated;

        // An unexpired preparation that has not failed to find a route claims priority.
        public static bool HoldsPriority(KnownGuardOrderStatus status, string reason, long expires, long now) => Preparing(status) && expires > now &&
            !(reason is "NoKnownAvailableEquipmentRoute" or "NoKnownRendezvousRoute" or "NoKnownSearchRoute" or "PlanBudgetExhausted");

        // Orders whose next leg is the escort journey are forecast for relief.
        public static bool ForecastsJourney(KnownGuardOrderStatus status, string reason, long expires, long now) =>
            (status == KnownGuardOrderStatus.TargetLocated || status == KnownGuardOrderStatus.Searching && reason == PreparingForEscortReason) && expires > now;

        // Forecasts only this guard's own needs against the known route and the
        // published intake duration. No partner state or live queue is read.
        public static bool JourneyNeedsRelief(string orderReason, Coord destination, IReadOnlyList<NeedValue> needs, IReadOnlyList<NeedRule> rules, long reliefRetryAt, long now,
            long tickDurationMs, long inspectTicks, Func<long> intakeTicks, Func<IActorView> ownView)
        {
            var urgent = needs.Where(n => rules[(int)n.Kind].Critical > 0 &&
                (n.Band >= NeedBand.Urgent || n.Kind == NeedKind.Bladder && n.Band >= NeedBand.Seek)).ToArray();
            if (urgent.Length == 0 || urgent.All(n => n.Band < NeedBand.Urgent) && reliefRetryAt > now) return false;
            // Keep an accepted preparation goal through its ordinary travel and
            // relief actions. Leaving the target invalidates the sighting, not
            // the reason for seeking relief. Failure backoff above still yields.
            if (orderReason == PreparingForEscortReason) return true;
            var view = ownView(); if (!view.CurrentTile.HasValue) return false;
            var travel = new KnownNavigation.Distances(view, view.CurrentTile.Value).To(destination);
            if (!travel.HasValue) return false;
            long duration = checked(travel.Value + inspectTicks + intakeTicks());
            return urgent.Any(n => view.NeedServices.Any(s => s.DetentionFacilityId == 0 && NeedPlanning.Serves(s.Kind, n.Kind)) &&
                new NeedAccumulator(n.Q, now, rules.Single(r => r.Kind == n.Kind).AwakeRate).At(checked(now + duration), tickDurationMs) >=
                    rules.Single(r => r.Kind == n.Kind).Critical * NeedAccumulator.HourMilliseconds);
        }

        // A late-shift formation needs the next duty interval after sleep. One
        // complete configured roster cycle is still a finite local bound.
        public static long TargetWaitDeadline(long expires, long formedTick, long inspectTicks, long? dayTicks, long maximumBackoff) =>
            Math.Min(expires, checked(formedTick + Math.Max(checked(inspectTicks * 2), dayTicks ?? maximumBackoff)));

        // A retained local-handoff delay ends when the decision runs.
        public static string ReasonOnDecision(string reason) => reason == "AwaitingLocalHandoff" ? "RetryLocalHandoff" : reason;

        // The locally formed team's search, including need breaks, is time-bounded.
        public static bool BoundsTeamWait(KnownGuardOrderStatus status) => status is KnownGuardOrderStatus.Searching or KnownGuardOrderStatus.TargetLocated;

        public static bool TeamWaitExpired(long now, long deadline, long expires) => now >= deadline && now < expires;

        public static GuardStep Next(KnownGuardOrderStatus status, long now, long expires, IGuardPreparationInputs inputs)
        {
            if (inputs.CriticalNeed()) return GuardStep.WithdrawForCriticalNeed;
            if (now >= expires) return GuardStep.Expire;
            if (inputs.JourneyNeedsRelief()) return GuardStep.SeekRelief;
            if (inputs.AwaitingIntake()) return GuardStep.RegisterIntake;
            if (inputs.Busy() || !inputs.KnownOnDuty() || inputs.ReportsPending()) return GuardStep.Wait;
            if (inputs.EscortControls() || status == KnownGuardOrderStatus.WaitingTeam) return GuardStep.Wait;
            if (status == KnownGuardOrderStatus.TargetLocated) return GuardStep.BeginEscort;
            if (status == KnownGuardOrderStatus.Searching) return GuardStep.Search;
            return GuardStep.Prepare;
        }

        // A guard withdrawing for a critical need leaves an unformed team wait.
        public static KnownGuardOrderStatus AfterCriticalNeed(KnownGuardOrderStatus status) =>
            status == KnownGuardOrderStatus.WaitingTeam ? KnownGuardOrderStatus.EquipmentPending : status;

        public static GuardPreparationStep Prepare(KnownGuardOrderStatus status, bool equipped, bool atRendezvous) =>
            !(equipped && atRendezvous) ? GuardPreparationStep.Plan : status != KnownGuardOrderStatus.AtRendezvous ? GuardPreparationStep.ArriveAtRendezvous : GuardPreparationStep.Join;

        public static string PreparationReason(bool equipped) => equipped ? "TravellingToRendezvous" : "AcquiringEquipment";

        public static GuardPlanFailure PreparationFailure(PlanOutcome outcome, bool equipped) => outcome == PlanOutcome.BudgetExhausted ? new GuardPlanFailure("PlanBudgetExhausted", null) :
            new GuardPlanFailure(equipped ? "NoKnownRendezvousRoute" : "NoKnownAvailableEquipmentRoute", equipped ? "RendezvousUnreachable" : "EquipmentUnavailable");

        public static ActionIntent PreparationIntent(PlanResult plan, Id<ActorTag> actor, long orderId)
        { var step = plan.Steps[0].Intent; return new ActionIntent(actor, step.Kind, step.Target, step.Item, guardOrderId: orderId); }

        // Readiness checks after the guard's own action or wake.
        public static bool SightingStale(bool moving, Coord position, Coord sightedFrom) => moving || position != sightedFrom;
        public static bool ReadinessLost(bool moving, Coord position, Coord rendezvous, Func<bool> equipped) => moving || position != rendezvous || !equipped();
        public static string ReinspectReason(string reason) => reason != PreparingForEscortReason ? "TargetObservationNeedsRefresh" : reason;
    }
}
