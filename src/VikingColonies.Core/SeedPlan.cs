using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>Which carried seeds belong to pending plant spots (one seed per spot), and which are still missing.</summary>
    public static class SeedPlan
    {
        /// <summary>Seeds to hold back from delivery: per seed, the smaller of carried and needed.</summary>
        public static Dictionary<string, int> Reserved(CarryLoad carry, IEnumerable<string> spotSeeds)
        {
            var reserved = new Dictionary<string, int>();
            foreach (KeyValuePair<string, int> need in Needed(spotSeeds))
            {
                int held = Math.Min(carry.Count(need.Key), need.Value);
                if (held > 0) reserved[need.Key] = held;
            }
            return reserved;
        }

        /// <summary>Seeds still to fetch from storage so every pending spot has one.</summary>
        public static Dictionary<string, int> Missing(CarryLoad carry, IEnumerable<string> spotSeeds)
        {
            var missing = new Dictionary<string, int>();
            foreach (KeyValuePair<string, int> need in Needed(spotSeeds))
            {
                int short_ = need.Value - carry.Count(need.Key);
                if (short_ > 0) missing[need.Key] = short_;
            }
            return missing;
        }

        public static int Total(IReadOnlyDictionary<string, int> counts) => counts.Values.Sum();

        private static Dictionary<string, int> Needed(IEnumerable<string> spotSeeds)
        {
            var needed = new Dictionary<string, int>();
            foreach (string seed in spotSeeds)
                needed[seed] = (needed.TryGetValue(seed, out int n) ? n : 0) + 1;
            return needed;
        }
    }
}
