using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>
    /// What a builder taking a new site does with what he carries (the real builders' run, 2026-10-18: two builders both
    /// fetched the Bronze for one hut, and the spare stayed in a pocket while he went on to other sites; another hut then
    /// waited 40 game minutes for "1 Bronze" that had been supplied). He keeps what the new site still needs and puts the
    /// rest back in a colony chest first, where every builder looks.
    /// </summary>
    public static class Leftovers
    {
        /// <summary>Of each item he carries, as many as the next site still needs (none without a next site).</summary>
        public static Dictionary<string, int> KeepFor(CarryLoad carrying, IReadOnlyDictionary<string, int> stillNeeded)
        {
            var keep = new Dictionary<string, int>();
            if (carrying == null || stillNeeded == null) return keep;
            foreach (string item in carrying.ItemNames)
            {
                int n = stillNeeded.TryGetValue(item, out int need) ? Math.Min(need, carrying.Count(item)) : 0;
                if (n > 0) keep[item] = n;
            }
            return keep;
        }

        /// <summary>He carries something the next site doesn't need: it goes back to a chest before he starts there.</summary>
        public static bool AnyToPutBack(CarryLoad carrying, IReadOnlyDictionary<string, int> stillNeeded) =>
            carrying != null && carrying.Beyond(KeepFor(carrying, stillNeeded)).Total > 0;
    }
}
