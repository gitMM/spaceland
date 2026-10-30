using System;

namespace Station.Mind
{
    // Private retry clocks shared by work, recovery and guard goals.
    public static class WorkBackoff
    {
        // Doubles up to the configured maximum without overflowing near it.
        public static long Next(long value, long maximum) => value > maximum / 2 ? maximum : Math.Min(maximum, checked(value * 2));
        // A failed action retries after the current backoff; success re-decides now.
        public static long RetryTick(long now, long backoff, bool failed) => failed ? checked(now + backoff) : now;
    }
}
