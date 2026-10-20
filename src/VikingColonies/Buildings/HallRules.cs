using System;
using System.Collections.Generic;
using UnityEngine;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Buildings
{
    /// <summary>
    /// A colony's Town Hall level as this computer knows it (1.4) and what it gives: the Town Hall's own level while it is
    /// loaded, else the level its huts remember (as they remember where it stands), else 1.
    /// </summary>
    internal static class HallRules
    {
        public static HallSettings Settings() => new HallSettings(ColonyConfig.HallLevels.Value, ColonyConfig.ColonyRadius.Value,
            ColonyConfig.HallRadiusPerLevel.Value, ColonyConfig.SettlersAtLevel1.Value, ColonyConfig.SettlersPerLevel.Value, ColonyConfig.MaxSettlers.Value);

        public static HallPerks PerksAt(int level) => TownHallLevels.Perks(level, Settings());

        public static int LevelOf(string colonyId)
        {
            if (string.IsNullOrEmpty(colonyId)) return 1;
            ColonyBuilding hall = ColonyBuilding.FindTownHall(colonyId);
            if (hall != null) return TownHallLevels.Clamp(hall.Level);
            int best = 1;
            foreach (ColonyBuilding b in ColonyBuilding.All)
                if (b != null && b.IsValid && b.ColonyId == colonyId) best = Math.Max(best, b.ColonyLevel);
            return TownHallLevels.Clamp(best);
        }

        public static HallPerks PerksOf(string colonyId) => PerksAt(LevelOf(colonyId));

        /// <summary>The colony's area around its Town Hall; the base radius for no colony.</summary>
        public static float RadiusOf(string colonyId) => PerksOf(colonyId).Radius;

        /// <summary>
        /// The area of the colony whose Town Hall stands at <paramref name="hallAt"/> (messages carry only where it stands):
        /// its circle as this computer knows it (the Town Hall's own, else what its huts remember), else level 1's.
        /// </summary>
        public static float RadiusAt(Vector3 hallAt)
        {
            // The colony's own circle (its Town Hall's, else what its huts remember: review of 1.4).
            foreach (ColonyAreaInfo area in ColonyAreas.Current())
                if (Math.Abs(area.Hall.Centre.X - hallAt.x) < 1f && Math.Abs(area.Hall.Centre.Z - hallAt.z) < 1f) return area.Hall.Radius;
            return PerksAt(1).Radius;
        }

        /// <summary>What raising the hall to <paramref name="level"/> costs (the setting's text, read leniently).</summary>
        public static List<KeyValuePair<string, int>> Cost(int level) => TownHallLevels.ParseCost(ColonyConfig.HallLevelCost(level));
    }
}
