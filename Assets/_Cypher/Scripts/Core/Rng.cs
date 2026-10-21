namespace Cypher.Core
{
    /// <summary>
    /// mulberry32, bit-for-bit identical to makeRng() in the web prototype
    /// (src/engine/Assets.js). The city regenerates the same way in both
    /// engines only if every call consumes the stream in the same order.
    /// </summary>
    public sealed class Rng
    {
        int _a;

        public Rng(uint seed)
        {
            _a = unchecked((int)seed);
        }

        /// <summary>Next value in [0, 1).</summary>
        public double Next()
        {
            unchecked
            {
                _a += 0x6d2b79f5;
                int a = _a;
                int t = (a ^ (int)((uint)a >> 15)) * (1 | a);
                t = (t + (t ^ (int)((uint)t >> 7)) * (61 | t)) ^ t;
                return (uint)(t ^ (int)((uint)t >> 14)) / 4294967296.0;
            }
        }
    }
}
