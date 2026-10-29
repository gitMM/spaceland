using System;
using Station.Contracts;

namespace Station.Mind
{
    public static class TravelEstimates
    {
        // The caller supplies evidence. There is intentionally no World parameter.
        public static long Authority(Coord lastKnownTile, Coord target, long nominalTicks)
        {
            if (nominalTicks < 1) throw new ArgumentOutOfRangeException(nameof(nominalTicks));
            return checked(lastKnownTile.Manhattan(target) * nominalTicks);
        }
    }
}
