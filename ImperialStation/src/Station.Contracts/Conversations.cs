using System;
using System.Collections.Generic;
using System.Linq;

namespace Station.Contracts
{
    public enum ConversationTopic { Routine, WorkStatus, WitnessedProblem, AllowedRequest }
    public sealed class ConversationConfig
    {
        public long DurationTicks { get; }
        public int PartnerCandidates { get; }
        public int FactsPerSpeaker { get; }
        public IReadOnlyList<long> ExplicitConsentActors { get; }
        public ConversationConfig(long durationTicks = 1800, int partnerCandidates = 4, int factsPerSpeaker = 4, IEnumerable<long>? explicitConsentActors = null)
        {
            var consent = (explicitConsentActors ?? Array.Empty<long>()).OrderBy(x => x).ToArray();
            if (durationTicks < 1 || partnerCandidates < 1 || partnerCandidates > 4 || factsPerSpeaker < 0 || factsPerSpeaker > 4 || consent.Any(x => x < 1) || consent.Distinct().Count() != consent.Length) throw new ArgumentException("Invalid conversation policy.");
            DurationTicks = durationTicks; PartnerCandidates = partnerCandidates; FactsPerSpeaker = factsPerSpeaker; ExplicitConsentActors = Array.AsReadOnly(consent);
        }
    }
}
