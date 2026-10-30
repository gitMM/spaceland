using System;
using Station.Contracts;

namespace Station.Mind
{
    // Credential-service and construction-authority answers to citizen reports.
    public static class RecoveryCasePolicy
    {
        // The first authenticated lost-card report opens a case and revokes the old credential;
        // every report, including a retry, is answered with the case's current state.
        public static LostCardResponse OnLostCardReport(bool caseOpen) => caseOpen ? LostCardResponse.ReplyWithExistingCase : LostCardResponse.OpenCaseAndRevoke;
    }
    public static class SurveyEvidencePolicy
    {
        // Admissibility of an authenticated survey about a known growth commitment. Empty when admissible.
        public static string Admissibility(bool credited, long creditedTick, long committedBerths, long committedTick, long deliveryTick, long usableBerths, long observedTick, long submittedTick)
        {
            if (!credited || deliveryTick < creditedTick) return "NotYetCredited";
            if (usableBerths > committedBerths || observedTick < committedTick || observedTick > submittedTick) return "InvalidCapacityOrObservationTime";
            return "";
        }
        // A retry of an accepted operation key is a duplicate only when the draft is identical.
        public static bool DuplicateRetry(bool sameDraft) => sameDraft;
        // Last received admissible evidence wins, as for other authority beliefs.
        public static long CorrectedBelief(long believedTotal, long previousProjectBelief, long usableBerths) => checked(believedTotal - previousProjectBelief + usableBerths);
    }
}
