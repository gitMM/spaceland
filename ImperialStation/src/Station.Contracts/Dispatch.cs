using System.Collections.Generic;

namespace Station.Contracts
{
    public sealed class DispatchCandidate
    {
        public KnownPersonnel Person { get; }
        public Coord? KnownOrigin { get; }
        public bool Committed { get; }
        public DispatchCandidate(KnownPersonnel person, Coord? knownOrigin, bool committed)
        { Person = person; KnownOrigin = knownOrigin; Committed = committed; }
    }
}
