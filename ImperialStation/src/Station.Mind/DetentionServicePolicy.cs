using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Mind
{
    // Local detention facility service rules: intake admission and refusal labels, the escort
    // leader's retry choice, and when a due release preempts or defers.
    public static class DetentionServicePolicy
    {
        public const string ReleaseDeferredReason = "ReleaseServiceUnavailable";
        // A local refusal remains known until this service changes. Routine wakeups must
        // not turn a full facility into a two-tick retry loop.
        public static bool RetryIntake(string knownRefusal) => !(knownRefusal is "Intake/FacilityFull" or "Intake/ServiceBusy" or "Intake/NoPower" or "Intake/Disconnected");
        public static bool IntakeAdmits(DetentionFacility facility, int otherSlots, int otherOperations) => otherSlots < facility.Capacity && otherOperations < facility.ServiceCapacity;
        // The label a leader keeps after a refused intake. occupied counts reserved, detained and releasing stays.
        public static string IntakeRefusal(FailureReason failure, DetentionFacility? facility, bool powered, bool connected, int occupied)
        {
            if (failure != FailureReason.ServiceBusy || facility == null) return "Intake/" + failure;
            return !powered ? "Intake/NoPower" : !connected ? "Intake/Disconnected" : occupied >= facility.Capacity ? "Intake/FacilityFull" : "Intake/ServiceBusy";
        }
        private static bool ServiceAvailable(DetentionFacility facility, bool powered, bool connected, int operations) => powered && connected && operations < facility.ServiceCapacity;
        // A due release interrupts the occupant's own need action only when the service could run it now.
        public static bool PreemptNeedForRelease(DetentionFacility facility, bool powered, bool connected, int operations, bool occupantInNeedAction) =>
            occupantInNeedAction && ServiceAvailable(facility, powered, connected, operations);
        public static bool ReleaseReady(DetentionFacility facility, bool powered, bool connected, int operations, bool occupantBusy, bool occupantPresent) =>
            ServiceAvailable(facility, powered, connected, operations) && !occupantBusy && occupantPresent;
    }
}
