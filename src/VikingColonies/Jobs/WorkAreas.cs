using VikingColonies.Buildings;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Jobs
{
    /// <summary>The huts whose worker has a work area around them (the lumberjack's, the forager's, the guard's patrol, 1.1), and
    /// how big it is. A Farm has none: its farmer works its fields (0.20.1).</summary>
    internal static class WorkAreas
    {
        /// <summary>The work area of a new hut of this type (level 1), or null when its worker has none.</summary>
        public static float? Base(string typeId)
        {
            if (typeId == BuildingTypes.LumberjackHutId) return ColonyConfig.WorkRadius.Value;
            if (typeId == BuildingTypes.ForagerHutId) return ColonyConfig.ForagerWorkRadius.Value;
            if (typeId == BuildingTypes.WatchtowerId) return GuardLevels.Radius(1);
            return null;
        }

        /// <summary>This hut's work area at its level, or null when its worker has none.</summary>
        public static float? Of(ColonyBuilding hut)
        {
            if (hut == null) return null;
            if (hut.TypeId == BuildingTypes.LumberjackHutId) return LumberjackJob.WorkRadiusOf(hut);
            if (hut.TypeId == BuildingTypes.ForagerHutId) return ForagerJob.WorkRadiusOf(hut);
            if (hut.TypeId == BuildingTypes.WatchtowerId) return GuardLevels.Radius(hut.Level);
            return null;
        }
    }
}
