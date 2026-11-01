using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TestKit
{
    /// <summary>
    /// Random choices a test run can replay (mod-neutral; tools/testkit/README.md). Without a seed: one unseeded generator,
    /// as random as ever. With one: a stream of its own per purpose and subject (e.g. "pastime" and a character's name),
    /// seeded from the seed by FNV-1a, so one subject's extra draws never shift another's. Key subjects by something the
    /// seed itself decides (a name drawn from it), never by a fresh GUID. Main thread only.
    /// </summary>
    public sealed class SeededStreams
    {
        private readonly Random _unseeded = new Random();
        private readonly Dictionary<string, Random> _streams = new Dictionary<string, Random>(StringComparer.Ordinal);

        public int? Seed { get; private set; }

        /// <summary>A seed (null: unseeded again); every stream starts over.</summary>
        public void SetSeed(int? seed)
        {
            Seed = seed;
            _streams.Clear();
        }

        /// <summary>The generator for this purpose and subject ("" for none).</summary>
        public Random For(string purpose, string who)
        {
            if (Seed == null) return _unseeded;
            string key = (purpose ?? "") + "|" + (who ?? "");
            if (!_streams.TryGetValue(key, out Random stream))
            {
                uint mixed = Fnv1a(Seed.Value.ToString(CultureInfo.InvariantCulture) + "|" + key);
                _streams[key] = stream = new Random((int)(mixed & 0x7fffffff));
            }
            return stream;
        }

        /// <summary>A new id (32 hex digits, as a GUID's "N" form) from the "ids" stream; null without a seed.</summary>
        public string NewId()
        {
            if (Seed == null) return null;
            var bytes = new byte[16];
            For("ids", "").NextBytes(bytes);
            return new Guid(bytes).ToString("N");
        }

        /// <summary>32-bit FNV-1a of the text's UTF-8: the same on every computer and run (string.GetHashCode may not be).</summary>
        public static uint Fnv1a(string text)
        {
            uint hash = 2166136261u;
            foreach (byte b in Encoding.UTF8.GetBytes(text ?? ""))
            {
                hash ^= b;
                hash *= 16777619u;
            }
            return hash;
        }
    }
}
