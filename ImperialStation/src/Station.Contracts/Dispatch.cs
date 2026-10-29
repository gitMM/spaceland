using System.Collections.Generic;

namespace Station.Contracts
{
    public sealed class DispatchCandidate
    {
        public KnownPersonnel Person { get; }
        public Coord? KnownOrigin { get; }
        public bool Committed { get; }
        // Committed only to cleanup work orders: no repair, verification, construction, patrol or escort.
        public bool CleanupOnly { get; }
        public DispatchCandidate(KnownPersonnel person, Coord? knownOrigin, bool committed, bool cleanupOnly = false)
        { Person = person; KnownOrigin = knownOrigin; Committed = committed; CleanupOnly = committed && cleanupOnly; }
    }
}
