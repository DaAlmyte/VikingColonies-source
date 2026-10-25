using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Core;

namespace VikingColonies.Construction
{
    /// <summary>
    /// The tools a builder can borrow: every axe, pickaxe and hoe the game knows (modded ones too), read once from the
    /// game's item list. Tools stay in the colony's chests; the builder holds his own copy while he works with one.
    /// </summary>
    internal static class BuilderTools
    {
        private static List<ToolInfo> s_known;

        public static IReadOnlyList<ToolInfo> Known
        {
            get
            {
                if (s_known == null && ObjectDB.instance != null && ObjectDB.instance.m_items.Count > 0) s_known = Catalogue();
                return (IReadOnlyList<ToolInfo>)s_known ?? new ToolInfo[0];
            }
        }

        /// <summary>The best tool of the kind, good enough for the tier, that one of the stores holds; null if none does.</summary>
        public static ToolInfo FindIn(IEnumerable<StorageModule> stores, ToolKind kind, int minTier, int maxTier = int.MaxValue) =>
            ToolChoice.Best(AtHand(stores), kind, minTier, maxTier);

        /// <summary>The known tools these chests hold.</summary>
        public static List<ToolInfo> AtHand(IEnumerable<StorageModule> stores)
        {
            var held = new HashSet<string>();
            foreach (StorageModule store in stores)
                if (store != null) held.UnionWith(store.Contents().Keys);
            return Known.Where(t => held.Contains(t.Prefab)).ToList();
        }

        /// <summary>
        /// What the colony's chests hold that builders may take: all of a Builder's Hut's chest (its builder's own), what
        /// other chests spare beyond what they keep (0.21). The Town Hall's "Still to bring" and Hand over (0.23) count it.
        /// </summary>
        public static Dictionary<string, int> StoresForBuilders(string colonyId, Vector3 near)
        {
            var stock = new Dictionary<string, int>();
            foreach (StorageModule store in ColonyStores(colonyId, null, near))
            {
                ColonyBuilding owner = store.GetComponent<ColonyBuilding>();
                bool builders = owner != null && owner.TypeId == BuildingTypes.BuilderHutId;
                NeedsTally.Add(stock, builders ? store.Contents() : store.SpareContents());
            }
            return stock;
        }

        /// <summary>A tool a worker uses from where it lies (a Cultivator): in his hut's chest, else any of the colony's (1.3.1).</summary>
        public static bool InColony(ColonyBuilding hut, string item)
        {
            if (hut == null) return false;
            if (hut.Storage != null && hut.Storage.HasItem(item)) return true;
            return !string.IsNullOrEmpty(hut.ColonyId) && ColonyStores(hut.ColonyId, hut, hut.transform.position).Any(s => s.HasItem(item));
        }

        /// <summary>
        /// The colony's chests in the order a settler tries them (<see cref="StorehouseRules.Rank"/>): the hut's own, the
        /// storehouse's store (1.5), the Town Hall's, then the colony's other huts, nearest first.
        /// </summary>
        public static List<StorageModule> ColonyStores(string colonyId, ColonyBuilding hut, Vector3 near)
        {
            var buildings = new List<ColonyBuilding>();
            if (hut != null) buildings.Add(hut);
            ColonyBuilding hall = ColonyBuilding.FindTownHall(colonyId);
            if (hall != null && hall != hut) buildings.Add(hall);
            buildings.AddRange(ColonyBuilding.All.Where(b => b != null && b.IsValid && b.ColonyId == colonyId && b != hut && !b.IsTownHall));
            var stores = new List<StorageModule>();
            foreach (ColonyBuilding b in buildings
                         .OrderBy(b => StorehouseRules.Rank(b == hut, b.IsStorehouse, b.IsTownHall))
                         .ThenBy(b => Vector3.Distance(b.transform.position, near)))
                foreach (StorageModule store in b.Stores)
                    if (!stores.Contains(store)) stores.Add(store);
            return stores;
        }

        /// <summary>As <see cref="Describe(ToolKind,int)"/>, naming the best tool his level allows: "a pickaxe (up to Iron pickaxe)".</summary>
        public static string Describe(ToolKind kind, int minTier, int maxTier)
        {
            if (maxTier == int.MaxValue) return Describe(kind, minTier);
            ToolInfo best = ToolChoice.Weakest(Known.Where(t => t.Tier == maxTier), kind, maxTier);
            ToolInfo weakest = minTier > 0 ? ToolChoice.Weakest(Known, kind, minTier) : null;
            string range = weakest != null ? $"{weakest.Name} $vc_or_better" : "";
            if (best != null) range += (range.Length > 0 ? ", " : "") + $"$vc_up_to {best.Name}";
            return range.Length > 0 ? $"{ToolToken(kind)} ({range})" : ToolToken(kind);
        }

        /// <summary>What to ask for, in words the game translates: "a pickaxe (Antler pickaxe or better)".</summary>
        public static string Describe(ToolKind kind, int minTier)
        {
            ToolInfo weakest = minTier > 0 ? ToolChoice.Weakest(Known, kind, minTier) : null;
            return weakest != null ? $"{ToolToken(kind)} ({weakest.Name} $vc_or_better)" : ToolToken(kind);
        }

        public static string ToolToken(ToolKind kind)
        {
            switch (kind)
            {
                case ToolKind.Axe: return "$vc_tool_axe";
                case ToolKind.Pickaxe: return "$vc_tool_pickaxe";
                default: return "$vc_tool_hoe";
            }
        }

        private static List<ToolInfo> Catalogue()
        {
            var tools = new List<ToolInfo>();
            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null) continue;
                ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
                ToolKind kind;
                if (shared.m_skillType == Skills.SkillType.Axes) kind = ToolKind.Axe;
                else if (shared.m_skillType == Skills.SkillType.Pickaxes) kind = ToolKind.Pickaxe;
                else if (GroundShaper.IsGroundTable(shared.m_buildPieces)) kind = ToolKind.Hoe;
                else continue;
                tools.Add(new ToolInfo(prefab.name, shared.m_name, kind, shared.m_toolTier));
            }
            Log.Info($"Builder tools: {tools.Count(t => t.Kind == ToolKind.Axe)} axes, {tools.Count(t => t.Kind == ToolKind.Pickaxe)} pickaxes, " +
                     $"{tools.Count(t => t.Kind == ToolKind.Hoe)} hoes");
            return tools;
        }
    }
}
