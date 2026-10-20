using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using VikingColonies.Core;
using Object = UnityEngine.Object;

namespace VikingColonies.Jobs
{
    /// <summary>
    /// One shared, cached look at the loaded world for every settler, refreshed at most every 2 seconds.
    /// Entries can be destroyed between refreshes: callers skip entries that are == null.
    /// </summary>
    /// <remarks>
    /// Measured (vc_perf, 2026-10-15): Unity's search for every object of a kind took 20–60 ms in a large loaded world,
    /// a visible hitch every couple of seconds. The game's own list of loaded networked objects, filtered by the prefabs
    /// that have the wanted part, gives the same answer in a fraction of a millisecond; the slow search is kept as the
    /// fallback when that list can't be read (a game update renamed it: see <see cref="GameCheck"/>). Each prefab is
    /// classified the first time one of its objects is met (review of 0.10: lists built once missed what mods register
    /// later, such as their doors).
    /// </remarks>
    internal static class WorldScan
    {
        private const float MaxAgeSeconds = 2f;

        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> s_instances = InstancesRef();
        // Per prefab (name hash): whether its objects have the wanted part, learned from the first one met.
        private static readonly Dictionary<int, bool> s_isDrop = new Dictionary<int, bool>(), s_isPickable = new Dictionary<int, bool>(),
            s_isChoppable = new Dictionary<int, bool>(), s_isLog = new Dictionary<int, bool>(), s_isDoor = new Dictionary<int, bool>(),
            s_isPlant = new Dictionary<int, bool>();

        private static readonly ScanCache<GameObject> s_choppables =
            new ScanCache<GameObject>(() => Timed("world scan: trees, logs, stumps", ScanChoppables), MaxAgeSeconds);

        private static readonly ScanCache<ItemDrop> s_drops =
            new ScanCache<ItemDrop>(() => Timed("world scan: items lying about",
                () => FromScene<ItemDrop>(s_isDrop, () => Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None).ToList())), MaxAgeSeconds);

        private static readonly ScanCache<Pickable> s_pickables =
            new ScanCache<Pickable>(() => Timed("world scan: plants",
                () => FromScene<Pickable>(s_isPickable, () => Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None).ToList())), MaxAgeSeconds);

        /// <summary>Logs lying about, for watching them settle (looked at four times a second while a settler waits).</summary>
        private static readonly ScanCache<TreeLog> s_logs =
            new ScanCache<TreeLog>(() => Timed("world scan: logs",
                () => FromScene<TreeLog>(s_isLog, () => Object.FindObjectsByType<TreeLog>(FindObjectsSortMode.None).ToList())), 0.5f);

        private static readonly ScanCache<Door> s_doors =
            new ScanCache<Door>(() => Timed("world scan: doors",
                () => FromScene<Door>(s_isDoor, () => Object.FindObjectsByType<Door>(FindObjectsSortMode.None).ToList())), MaxAgeSeconds);

        /// <summary>Plants growing (planted saplings and crops not yet grown), for the farmer's plots.</summary>
        private static readonly ScanCache<Plant> s_plants =
            new ScanCache<Plant>(() => Timed("world scan: growing plants",
                () => FromScene<Plant>(s_isPlant, () => Object.FindObjectsByType<Plant>(FindObjectsSortMode.None).ToList())), MaxAgeSeconds);

        public static IReadOnlyList<Plant> Plants() => s_plants.Get(Time.time);

        /// <summary>Something was just planted: the next read of the growing plants looks again.</summary>
        public static void InvalidatePlants() => s_plants.Invalidate();

        public static IReadOnlyList<GameObject> Choppables() => s_choppables.Get(Time.time);

        public static IReadOnlyList<TreeLog> Logs() => s_logs.Get(Time.time);

        public static IReadOnlyList<Door> Doors() => s_doors.Get(Time.time);

        /// <summary>Berry bushes, mushrooms and the like (and the player's crops, which the forager leaves).</summary>
        public static IReadOnlyList<Pickable> Pickables() => s_pickables.Get(Time.time);

        public static IReadOnlyList<ItemDrop> Drops() => s_drops.Get(Time.time);

        /// <summary>Something just dropped (a forager picked a bush): the next read of the drops looks again.</summary>
        public static void InvalidateDrops() => s_drops.Invalidate();

        /// <summary>Something just broke (a tree fell, a log split): the next read looks again.</summary>
        public static void Invalidate()
        {
            s_choppables.Invalidate();
            s_drops.Invalidate();
            s_pickables.Invalidate();
            s_logs.Invalidate();
        }

        /// <summary>Trees, logs and tree stumps; a tree kind the catalog hasn't seen is taught to it, as before.</summary>
        private static List<GameObject> ScanChoppables()
        {
            Dictionary<ZDO, ZNetView> instances = Instances();
            if (instances == null) return TreeCatalogBuilder.LoadedChoppables().ToList();
            TreeCatalog catalog = TreeCatalogBuilder.Catalog;
            var found = new List<GameObject>();
            foreach (KeyValuePair<ZDO, ZNetView> entry in instances)
            {
                ZNetView view = entry.Value;
                if (view == null || entry.Key == null || !Is(s_isChoppable, entry.Key.GetPrefab(), view, IsChoppable)) continue;
                GameObject go = view.gameObject;
                TreeBase tree = go.GetComponent<TreeBase>();
                if (tree != null)
                {
                    if (!TreeCatalogBuilder.KnowsTree(entry.Key.GetPrefab())) TreeCatalogBuilder.Learn(catalog, go, tree);
                    found.Add(go);
                }
                else if (go.GetComponent<TreeLog>() != null) found.Add(go);
                else if (go.GetComponent<Destructible>() is Destructible d && d.GetDestructibleType() == DestructibleType.Tree) found.Add(go);
            }
            return found;
        }

        private static List<T> FromScene<T>(Dictionary<int, bool> known, Func<List<T>> fallback) where T : Component
        {
            Dictionary<ZDO, ZNetView> instances = Instances();
            if (instances == null) return fallback();
            var found = new List<T>();
            foreach (KeyValuePair<ZDO, ZNetView> entry in instances)
            {
                ZNetView view = entry.Value;
                if (view == null || entry.Key == null || !Is(known, entry.Key.GetPrefab(), view, v => v.GetComponent<T>() != null)) continue;
                T part = view.GetComponent<T>();
                if (part != null) found.Add(part);
            }
            return found;
        }

        /// <summary>Whether objects of this prefab have the wanted part: learned from the first one met, then remembered.</summary>
        private static bool Is(Dictionary<int, bool> known, int prefab, ZNetView view, Func<ZNetView, bool> check)
        {
            if (known.TryGetValue(prefab, out bool yes)) return yes;
            yes = check(view);
            known[prefab] = yes;
            return yes;
        }

        private static bool IsChoppable(ZNetView view) =>
            view.GetComponent<TreeBase>() != null || view.GetComponent<TreeLog>() != null ||
            (view.GetComponent<Destructible>() is Destructible d && d.GetDestructibleType() == DestructibleType.Tree);

        private static Dictionary<ZDO, ZNetView> Instances() =>
            s_instances != null && ZNetScene.instance != null ? s_instances(ZNetScene.instance) : null;

        private static AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> InstancesRef()
        {
            try
            {
                return AccessTools.Field(typeof(ZNetScene), "m_instances") != null
                    ? AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances") : null;
            }
            catch (Exception)
            {
                return null;   // the slow search is used instead
            }
        }

        private static List<T> Timed<T>(string part, Func<List<T>> scan)
        {
            long start = Perf.Start();
            List<T> found = scan();
            Perf.Stop(part, start);
            return found;
        }
    }
}
