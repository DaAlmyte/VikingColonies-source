using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>The Town Hall chest takes damage only from players: monsters, raids, thrown rocks and hits with no attacker don't count.</summary>
    public static class TownHallHits
    {
        public static bool Counts(bool attackerIsPlayer) => attackerIsPlayer;
    }

    /// <summary>
    /// When a colony's Town Hall is confirmed gone, its huts and settlers mark the in-game time; the colony lives on for
    /// <c>Colony.TownHallGraceDays</c> in-game days, so a new Town Hall placed among its huts can take it over. 0 days is
    /// the old way: the colony ends once the Town Hall is confirmed gone.
    /// </summary>
    public static class ColonyGrace
    {
        /// <summary>A Valheim day, in the game's seconds.</summary>
        public const double DaySeconds = 1800;
        public const int MaxDays = 10;
        /// <summary>"Its Town Hall was never lost."</summary>
        public const double NotLost = -1;

        public static bool IsLost(double lostAt) => lostAt >= 0;

        /// <param name="daySeconds">A day in this world (some mods change it).</param>
        public static bool Expired(double lostAt, double now, int graceDays, double daySeconds = DaySeconds) =>
            graceDays <= 0 || now - lostAt >= graceDays * daySeconds;

        /// <summary>Days left to build a new Town Hall, a part day counting as one; 0 once the time is up.</summary>
        public static int DaysLeft(double lostAt, double now, int graceDays, double daySeconds = DaySeconds) =>
            Expired(lostAt, now, graceDays, daySeconds) ? 0 : (int)Math.Ceiling((graceDays * daySeconds - (now - lostAt)) / daySeconds);
    }

    /// <summary>A hut whose colony may have lost its Town Hall.</summary>
    public struct OrphanHut
    {
        public OrphanHut(string colonyId, GroundPoint at, double lostAt)
        {
            ColonyId = colonyId ?? "";
            At = at;
            LostAt = lostAt;
        }

        public string ColonyId { get; }
        public GroundPoint At { get; }
        /// <summary>When it noticed its Town Hall was gone (game seconds); <see cref="ColonyGrace.NotLost"/> when it didn't.</summary>
        public double LostAt { get; }
    }

    /// <summary>
    /// A new Town Hall placed among the huts of a colony that lost its own within the grace time takes that colony over:
    /// it keeps the colony's id, so every settler and hut belongs to it again. Several such colonies in range: the one
    /// with the most huts there.
    /// </summary>
    public static class TownHallTakeover
    {
        public static string Choose(IEnumerable<OrphanHut> huts, GroundPoint hall, float radius, double now, int graceDays,
            double daySeconds = ColonyGrace.DaySeconds)
        {
            var counts = new Dictionary<string, int>();
            foreach (OrphanHut hut in huts)
            {
                if (hut.ColonyId.Length == 0 || !ColonyGrace.IsLost(hut.LostAt) || ColonyGrace.Expired(hut.LostAt, now, graceDays, daySeconds)) continue;
                if (ColonyRules.Distance(hut.At, hall) > radius) continue;
                counts[hut.ColonyId] = (counts.TryGetValue(hut.ColonyId, out int n) ? n : 0) + 1;
            }
            string best = null;
            int bestCount = 0;
            foreach (KeyValuePair<string, int> colony in counts)
                if (colony.Value > bestCount || (colony.Value == bestCount && string.CompareOrdinal(colony.Key, best) < 0))
                {
                    best = colony.Key;
                    bestCount = colony.Value;
                }
            return best;
        }
    }
}
