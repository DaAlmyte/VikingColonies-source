using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;

namespace VikingColonies.Buildings
{
    public sealed class BuildingType
    {
        public BuildingType(string id, string prefabName, string nameToken, string jobId)
        {
            Id = id;
            PrefabName = prefabName;
            NameToken = nameToken;
            JobId = jobId;
        }

        public string Id { get; }
        public string PrefabName { get; }
        public string NameToken { get; }
        /// <summary>The job this building employs, or null (e.g. the Town Hall).</summary>
        public string JobId { get; }
    }

    /// <summary>Every kind of colony building. Adding a building = one entry here + its piece + its modules.</summary>
    public static class BuildingTypes
    {
        public const string TownHallId = SaveKeys.TownHallType;
        public const string LumberjackHutId = SaveKeys.LumberjackHutType;
        public const string BuilderHutId = SaveKeys.BuilderHutType;
        public const string LonghouseId = SaveKeys.LonghouseType;
        public const string ForagerHutId = SaveKeys.ForagerHutType;
        public const string FarmId = SaveKeys.FarmType;
        public const string WatchtowerId = SaveKeys.WatchtowerType;

        // The Town Hall keeps v1's prefab name so existing worlds load it.
        public static readonly BuildingType TownHall = new BuildingType(TownHallId, SaveKeys.TownHallPiece, "$vc_town_hall", null);
        public static readonly BuildingType LumberjackHut = new BuildingType(LumberjackHutId, SaveKeys.LumberjackHutPiece, "$vc_hut_lumberjack", "lumberjack");
        public static readonly BuildingType BuilderHut = new BuildingType(BuilderHutId, SaveKeys.BuilderHutPiece, "$vc_hut_builder", "builder");
        /// <summary>Beds for settlers: each bed is a home (no job).</summary>
        public static readonly BuildingType Longhouse = new BuildingType(LonghouseId, SaveKeys.LonghousePiece, "$vc_hut_longhouse", null);

        /// <summary>The forager picks wild berries and mushrooms around it.</summary>
        public static readonly BuildingType ForagerHut = new BuildingType(ForagerHutId, SaveKeys.ForagerHutPiece, "$vc_hut_forager", "forager");

        /// <summary>The farmer grows crops on the plots round it (0.12).</summary>
        public static readonly BuildingType Farm = new BuildingType(FarmId, SaveKeys.FarmPiece, "$vc_hut_farm", "farmer");

        /// <summary>Its guard keeps monsters off the colony, and it pushes the colony's border out (1.1).</summary>
        public static readonly BuildingType Watchtower = new BuildingType(WatchtowerId, SaveKeys.WatchtowerPiece, "$vc_hut_watchtower", SaveKeys.GuardJob);

        private static readonly BuildingType[] s_types = { TownHall, LumberjackHut, BuilderHut, Longhouse, ForagerHut, Farm, Watchtower };

        public static IReadOnlyList<BuildingType> All => s_types;

        public static BuildingType ByPrefab(string prefabName) => s_types.FirstOrDefault(t => t.PrefabName == prefabName);

        public static BuildingType Get(string id) => s_types.FirstOrDefault(t => t.Id == id);
    }
}
