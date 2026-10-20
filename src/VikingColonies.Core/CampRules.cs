using System;

namespace VikingColonies.Core
{
    /// <summary>
    /// The Town Hall's camp (huts round 2, 2026-10-18; the player: "like a camping spot that starts simple and evolves as
    /// the town hall levels up"): the colony's meeting place, built by the builder like a hut from the camp's design, its
    /// level following the hall's one level at a time. It has a level of its own (the hall's can be raised before the
    /// builder has caught up), and only exists where a design for it does.
    /// </summary>
    public static class CampRules
    {
        /// <summary>A new camp starts at level 1, whatever the hall's level; it grows to the hall's one level at a time.</summary>
        public const int FirstLevel = 1;

        /// <summary>
        /// The camp level to order next: one above the camp that stands, once the hall has reached it, no work on the camp
        /// is under way (building, upgrading, repairing) and a builder may build it: the colony's highest Builder's Hut
        /// (<paramref name="builderHutLevel"/>, 0 for none) as high, as for a hut (the review of 2026-10-19: a level no
        /// builder may take was ordered, never taken, and its repairs stopped). 0 for none.
        /// </summary>
        public static int NextLevel(int hallLevel, int campLevel, bool busy, int builderHutLevel)
        {
            int target = HallAllows(hallLevel, campLevel, busy);
            return target > 0 && builderHutLevel > 0 && HutLevels.MayBuild(builderHutLevel, target, false) ? target : 0;
        }

        /// <summary>The hall allows the camp's next level, but no Builder's Hut is high enough to build it (for the Hall tab).</summary>
        public static bool WaitsForBuilder(int hallLevel, int campLevel, bool busy, int builderHutLevel) =>
            HallAllows(hallLevel, campLevel, busy) > 0 && NextLevel(hallLevel, campLevel, busy, builderHutLevel) == 0;

        private static int HallAllows(int hallLevel, int campLevel, bool busy)
        {
            if (busy || campLevel < FirstLevel) return 0;
            int target = campLevel + 1;
            return target <= Math.Min(HutLevels.MaxLevel, TownHallLevels.Clamp(hallLevel)) ? target : 0;
        }

        /// <summary>A camp site found red is looked at again after this long (seconds), not on every 15 s tick (A8).</summary>
        public const double RedRecheckSeconds = 60.0;

        /// <summary>Time to survey the camp's next level's site again: never found red, or red a while ago.</summary>
        public static bool LookAgain(double lastRedAt, double now) => now - lastRedAt >= RedRecheckSeconds;
    }

    /// <summary>
    /// The camp's waiting reason as the Town Hall stores it: its translation key, and a number when it has one ("needs a
    /// level 3 Builder's Hut"), so every player reads it in their own language (the 1.4.0 review: it was stored as its
    /// owner's text). Text 1.4.0 stored reads back as itself.
    /// </summary>
    public static class CampWait
    {
        private const char Gap = '|';

        public static string Store(string token, int number = 0) => number > 0 ? token + Gap + number : token ?? "";

        /// <returns>True when it carries a number.</returns>
        public static bool Read(string stored, out string token, out int number)
        {
            number = 0;
            token = stored ?? "";
            int gap = token.LastIndexOf(Gap);
            if (gap <= 0 || !token.StartsWith("$", StringComparison.Ordinal)) return false;
            if (!int.TryParse(token.Substring(gap + 1), out number)) { number = 0; return false; }
            token = token.Substring(0, gap);
            return true;
        }
    }
}
