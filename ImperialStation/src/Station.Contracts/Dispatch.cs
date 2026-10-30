using System.Collections.Generic;

namespace Station.Contracts
{
    public sealed class DispatchCandidate
    {
        public KnownPersonnel Person { get; }
        public Coord? KnownOrigin { get; }
        public bool Committed { get; }
        // Committed only to routine cleanup, which an urgent repair may interrupt.
        public bool RoutineOnly { get; }
        public DispatchCandidate(KnownPersonnel person, Coord? knownOrigin, bool committed, bool routineOnly = false)
        { Person = person; KnownOrigin = knownOrigin; Committed = committed; RoutineOnly = committed && routineOnly; }
    }
}
