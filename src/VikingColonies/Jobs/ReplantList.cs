using System.Collections.Generic;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Core;

namespace VikingColonies.Jobs
{
    /// <summary>A Lumberjack's Hut's replanting list, from the game's saplings, its level, its biome and its unticked kinds.</summary>
    internal static class ReplantList
    {
        public static List<ReplantLine> For(ColonyBuilding hut)
        {
            WorkerModule worker = hut != null ? hut.GetComponent<WorkerModule>() : null;
            return ReplantChoice.Lines(TreeCatalogBuilder.Catalog.Saplings, hut != null ? hut.Level : 1, HutBiome(hut),
                worker != null ? (ICollection<string>)worker.Unticked : new HashSet<string>());
        }

        public static int HutBiome(ColonyBuilding hut) => hut != null ? BiomeAt(hut.transform.position) : 0;

        public static int BiomeAt(Vector3 at) => WorldGenerator.instance != null ? (int)WorldGenerator.instance.GetBiome(at.x, at.z) : 0;
    }
}
