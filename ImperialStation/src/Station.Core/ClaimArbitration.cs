using System;
using System.Collections.Generic;

namespace Station.Core
{
    public static class ClaimArbitration
    {
        // Fisher-Yates; bounded uniform draws come from the world's snapshotted stream.
        // A draw callback also permits exhaustive controlled-draw correctness tests.
        public static void Permute<T>(IList<T> contenders, Func<int, int> draw)
        {
            for (int i = contenders.Count - 1; i > 0; i--)
            {
                int j = draw(i + 1);
                if (j < 0 || j > i) throw new ArgumentOutOfRangeException(nameof(draw));
                T temporary = contenders[i]; contenders[i] = contenders[j]; contenders[j] = temporary;
            }
        }
    }
}
