using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Core;
using Object = UnityEngine.Object;

namespace VikingColonies.Jobs
{
    /// <summary>Fills the TreeCatalog from the game's own data, the first time a lumberjack needs it.</summary>
    internal static class TreeCatalogBuilder
    {
        private static TreeCatalog s_catalog;
        private static readonly HashSet<int> s_knownTrees = new HashSet<int>();

        /// <summary>The catalog has this tree kind (by prefab name hash): no need to look at its name.</summary>
        public static bool KnowsTree(int prefabHash) => s_knownTrees.Contains(prefabHash);

        public static TreeCatalog Catalog => s_catalog ?? (s_catalog = Build());

        /// <summary>Every loaded tree, log and tree stump. Also teaches the catalog any tree type it hasn't seen.</summary>
        public static IEnumerable<GameObject> LoadedChoppables()
        {
            TreeCatalog catalog = Catalog;
            foreach (TreeBase tree in Object.FindObjectsByType<TreeBase>(FindObjectsSortMode.None))
            {
                string name = Utils.GetPrefabName(tree.gameObject);
                if (catalog.KindOf(name) == ChopKind.None) AddTree(catalog, name, tree);
                yield return tree.gameObject;
            }
            foreach (TreeLog log in Object.FindObjectsByType<TreeLog>(FindObjectsSortMode.None))
                yield return log.gameObject;
            foreach (Destructible d in Object.FindObjectsByType<Destructible>(FindObjectsSortMode.None))
                if (d.GetDestructibleType() == DestructibleType.Tree) yield return d.gameObject;
        }

        /// <summary>Teaches the catalog a loaded tree's kind if it hasn't seen it (a tree a mod added after the catalog was built).</summary>
        public static void Learn(TreeCatalog catalog, GameObject go, TreeBase tree)
        {
            string name = Utils.GetPrefabName(go);
            if (catalog.KindOf(name) == ChopKind.None) AddTree(catalog, name, tree);
        }

        private static TreeCatalog Build()
        {
            long timer = Perf.Start();
            try { return BuildTimed(); }
            finally { Perf.Stop("building the tree list (once)", timer); }
        }

        private static TreeCatalog BuildTimed()
        {
            var catalog = new TreeCatalog();
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                TreeBase tree = prefab != null ? prefab.GetComponent<TreeBase>() : null;
                if (tree != null) AddTree(catalog, prefab.name, tree);
            }

            GameObject cultivator = ObjectDB.instance.GetItemPrefab("Cultivator");
            PieceTable table = cultivator != null ? cultivator.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces : null;
            if (table != null)
            {
                foreach (GameObject pieceObject in table.m_pieces)
                {
                    Plant plant = pieceObject != null ? pieceObject.GetComponent<Plant>() : null;
                    Piece piece = pieceObject != null ? pieceObject.GetComponent<Piece>() : null;
                    if (plant == null || piece == null || plant.m_grownPrefabs == null) continue;
                    List<string> trees = plant.m_grownPrefabs
                        .Where(g => g != null && g.GetComponent<TreeBase>() != null)
                        .Select(g => g.name)
                        .ToList();
                    if (trees.Count == 0 || piece.m_resources.Length == 0 || piece.m_resources[0].m_resItem == null) continue;
                    catalog.AddSapling(pieceObject.name, piece.m_resources[0].m_resItem.name, trees, (int)plant.m_biome, piece.m_name);
                }
            }

            Log.Info($"Tree catalog: {catalog.TreeCount} trees, {catalog.SaplingCount} saplings, {catalog.CollectibleCount} collectible items " +
                     $"(trees: {string.Join(", ", catalog.Trees.Keys.OrderBy(k => k, System.StringComparer.Ordinal).ToArray())})");
            return catalog;
        }

        private static void AddTree(TreeCatalog catalog, string name, TreeBase tree)
        {
            s_knownTrees.Add(name.GetStableHashCode());
            GameObject stumpPrefab = tree.m_stubPrefab;
            catalog.AddTree(name, tree.m_minToolTier, stumpPrefab != null ? stumpPrefab.name : null);
            AddDrops(catalog, tree.m_dropWhenDestroyed);
            if (stumpPrefab != null)
            {
                Destructible stump = stumpPrefab.GetComponent<Destructible>();
                catalog.AddStump(stumpPrefab.name, stump != null ? stump.m_minToolTier : 0);
                DropOnDestroyed drops = stumpPrefab.GetComponent<DropOnDestroyed>();
                if (drops != null) AddDrops(catalog, drops.m_dropWhenDestroyed);
            }
            AddLogs(catalog, tree.m_logPrefab, 0);
        }

        private static void AddLogs(TreeCatalog catalog, GameObject logPrefab, int depth)
        {
            if (logPrefab == null || depth > 5) return;
            TreeLog log = logPrefab.GetComponent<TreeLog>();
            if (log == null) return;
            catalog.AddLog(logPrefab.name, log.m_minToolTier);
            AddDrops(catalog, log.m_dropWhenDestroyed);
            AddLogs(catalog, log.m_subLogPrefab, depth + 1);
        }

        private static void AddDrops(TreeCatalog catalog, DropTable table)
        {
            if (table?.m_drops == null) return;
            foreach (DropTable.DropData drop in table.m_drops)
                if (drop.m_item != null) catalog.AddCollectible(drop.m_item.name);
        }
    }
}
