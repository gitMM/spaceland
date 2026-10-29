using System;

namespace Station.Core
{
    // PCG-XSH-RR 64/32, version 1. Arithmetic intentionally wraps modulo 2^64.
    public sealed class Pcg32
    {
        public ulong State { get; private set; }
        public ulong Increment { get; }
        private Pcg32(ulong state, ulong increment) { State = state; Increment = increment; }
        public static Pcg32 Restore(ulong state, ulong increment)
        { if ((increment & 1) == 0) throw new ArgumentException("PCG increment must be odd."); return new Pcg32(state, increment); }
        public static Pcg32 Create(ulong seed, string key)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                foreach (char c in key) { hash ^= c; hash *= 1099511628211UL; }
                var rng = new Pcg32(0, (Mix(hash) << 1) | 1); rng.Next(); rng.State += Mix(seed ^ hash); rng.Next(); return rng;
            }
        }
        private static ulong Mix(ulong n)
        { unchecked { n += 0x9e3779b97f4a7c15UL; n = (n ^ (n >> 30)) * 0xbf58476d1ce4e5b9UL; n = (n ^ (n >> 27)) * 0x94d049bb133111ebUL; return n ^ (n >> 31); } }
        public uint Next()
        {
            unchecked
            {
                ulong old = State; State = old * 6364136223846793005UL + Increment;
                uint x = (uint)(((old >> 18) ^ old) >> 27); int r = (int)(old >> 59);
                return (x >> r) | (x << ((-r) & 31));
            }
        }
        public int Next(int exclusiveMax)
        {
            if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            uint bound = (uint)exclusiveMax; uint threshold = unchecked(0u - bound) % bound;
            uint n; do { n = Next(); } while (n < threshold); return (int)(n % bound);
        }
    }
}
