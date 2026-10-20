using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>
    /// What a builder has given up on while clearing a site (he couldn't get to it, couldn't fell it without hitting a
    /// building, or swung at it too long). It is tried again later; after <see cref="LeaveAfter"/> give-ups it is left,
    /// and the building goes up without it (the camp-at-the-boulder run on the 1.4.0 build, 2026-10-19: a big boulder's
    /// part out of reach was tried every minute for over ten minutes, and the camp never went up) — but only outside
    /// where the building itself goes. A tree or a rock there keeps holding the site, named in the hut's window: the fix
    /// pass's review found pieces put up into one that was left.
    /// </summary>
    public sealed class SiteGiveUps
    {
        public const int LeaveAfter = 3;

        private readonly Dictionary<string, int> _count = new Dictionary<string, int>();

        public void Add(string key)
        {
            if (key == null) return;
            _count.TryGetValue(key, out int n);
            _count[key] = n + 1;
        }

        /// <summary>Given up on <see cref="LeaveAfter"/> times, and not where the building itself goes: left where it is.</summary>
        public bool Left(string key, bool inTheBuilding) =>
            !inTheBuilding && key != null && _count.TryGetValue(key, out int n) && n >= LeaveAfter;

        /// <summary>
        /// What can hold a site where the building itself goes: what the building can't go up through (a tree, a log, a
        /// stump, a rock). An item he can't pick up or a bush is left after a few tries anywhere (the whole-mod review of
        /// 1.4.1's builder, M1: an item on an old hut's roof held an upgrade's clearing for good).
        /// </summary>
        public static bool HoldsTheSite(ClearKind kind) =>
            kind == ClearKind.Tree || kind == ClearKind.Log || kind == ClearKind.Stump || kind == ClearKind.Rock;

        /// <summary>A new site: nothing is left yet.</summary>
        public void Clear() => _count.Clear();
    }

    /// <summary>
    /// A big rock's part he can't walk up to (the camp-at-the-boulder run on 1.4.1's builder, 2026-10-19: he climbed onto
    /// the boulder on his way, and from up there no way led to a spot on the ground; every part failed in 2 s, over and
    /// over). His pickaxe's blow lands where he aims: stuck within <see cref="FromHereReach"/> of the part, he breaks it from
    /// where he stands (the whole-mod review's M2: "count stuck within reach as arrived").
    /// </summary>
    public static class RockWork
    {
        public const float FromHereReach = 4f;

        public static bool FromHere(bool stuck, bool rockPart, float distance) => stuck && rockPart && distance <= FromHereReach;
    }
}
