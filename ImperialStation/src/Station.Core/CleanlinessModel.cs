using System;
using System.Collections.Generic;
using System.Linq;
using Station.Contracts;

namespace Station.Core
{
    public sealed class DirtSeed
    {
        public Id<TileTag> Tile { get; }
        public long Units { get; }
        public DirtSeed(Id<TileTag> tile, long units)
        { if (tile.Value == 0 || units < 0) throw new ArgumentException("Invalid initial dirt."); Tile = tile; Units = units; }
    }
    public sealed class CleanlinessSeed
    {
        public long CleanUnitTicks { get; }
        public int MeanSoilingTicks { get; }
        public long SpillUnits { get; }
        public WorkAccess Access { get; }
        public IReadOnlyList<DirtSeed> Dirt { get; }
        public CleanlinessSeed(IEnumerable<DirtSeed>? dirt = null, long cleanUnitTicks = 300, int meanSoilingTicks = 288000,
            long spillUnits = 4, WorkAccess? access = null)
        {
            if (cleanUnitTicks < 1 || meanSoilingTicks != 0 && (meanSoilingTicks < 2 || meanSoilingTicks > 1000000000) || spillUnits < 1)
                throw new ArgumentException("Invalid cleanliness duration, soiling interval or spill size.");
            CleanUnitTicks = cleanUnitTicks; MeanSoilingTicks = meanSoilingTicks; SpillUnits = spillUnits;
            Access = access ?? new WorkAccess(new[] { "cleaner" });
            Dirt = Array.AsReadOnly((dirt ?? Array.Empty<DirtSeed>()).OrderBy(d => d.Tile).ToArray());
            if (Dirt.Select(d => d.Tile).Distinct().Count() != Dirt.Count) throw new ArgumentException("Duplicate initial dirt tile.");
        }
    }
    public sealed class DirtSnapshot
    {
        public long TileId { get; internal set; }
        public Coord Position { get; internal set; }
        public long Units { get; internal set; }
        public long InitialUnits { get; internal set; }
        public long AddedUnits { get; internal set; }
        public long RemovedUnits { get; internal set; }
        public long NextSoilingTick { get; internal set; } = -1;
        internal DirtSnapshot Copy() => (DirtSnapshot)MemberwiseClone();
    }
}
