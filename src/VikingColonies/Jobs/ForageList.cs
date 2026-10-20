using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Core;

namespace VikingColonies.Jobs
{
    /// <summary>
    /// The forager's "What to pick" list: the kinds from the game's own pickables (what grows back, where the world grows
    /// it), read the first time it's needed, and a hut's unticked kinds.
    /// </summary>
    internal static class ForageList
    {
        private static List<ForageKind> s_kinds;
        private static HashSet<string> s_items;
        private static Dictionary<string, ForageKind> s_byItem;

        public static IReadOnlyList<ForageKind> Kinds => Build() ? s_kinds : new List<ForageKind>();

        /// <summary>The items on the list, for a quick "is it his kind?".</summary>
        public static ICollection<string> Items => Build() ? s_items : new HashSet<string>();

        public static List<ForageLine> For(ColonyBuilding hut) => ForageChoice.Lines(Kinds, Unticked(hut));

        /// <summary>The kind giving this item; null when it isn't on the list.</summary>
        public static ForageKind Kind(string item) =>
            Build() && !string.IsNullOrEmpty(item) && s_byItem.TryGetValue(item, out ForageKind kind) ? kind : null;

        /// <summary>The kind is on the list and ticked on this hut's (<paramref name="saved"/>: what the player changed there).</summary>
        public static bool Wants(string item, ICollection<string> saved)
        {
            ForageKind kind = Kind(item);
            return kind != null && ForageChoice.IsTicked(kind, saved);
        }

        /// <summary>The kinds the player unticked on this hut's list (saved with the hut).</summary>
        public static HashSet<string> Unticked(ColonyBuilding hut)
        {
            WorkerModule worker = hut != null ? hut.GetComponent<WorkerModule>() : null;
            return worker != null ? worker.PickUnticked : new HashSet<string>();
        }

        private static bool Build()
        {
            if (s_kinds != null) return true;
            if (ZNetScene.instance == null || ZoneSystem.instance == null) return false;
            long timer = Perf.Start();
            try { return BuildTimed(); }
            finally { Perf.Stop("building the forage list (once)", timer); }
        }

        private static bool BuildTimed()
        {
            var biomes = new Dictionary<GameObject, int>();
            foreach (ZoneSystem.ZoneVegetation veg in ZoneSystem.instance.m_vegetation)
            {
                if (veg == null || !veg.m_enable || veg.m_prefab == null) continue;
                biomes.TryGetValue(veg.m_prefab, out int mask);
                biomes[veg.m_prefab] = mask | (int)veg.m_biome;
            }
            var pickables = new List<PickableInfo>();
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                Pickable pick = prefab != null ? prefab.GetComponent<Pickable>() : null;
                ItemDrop drop = pick != null && pick.m_itemPrefab != null ? pick.m_itemPrefab.GetComponent<ItemDrop>() : null;
                if (drop == null) continue;
                ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
                biomes.TryGetValue(prefab, out int grows);
                pickables.Add(new PickableInfo(prefab.name, pick.m_itemPrefab.name, shared.m_name, pick.m_respawnTimeMinutes,
                    isFood: shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && shared.m_food + shared.m_foodStamina > 0f,
                    isMaterial: shared.m_itemType == ItemDrop.ItemData.ItemType.Material, biomes: grows));
            }
            s_kinds = ForageCatalog.Kinds(pickables).ToList();
            s_items = new HashSet<string>(s_kinds.Select(k => k.Item));
            s_byItem = s_kinds.ToDictionary(k => k.Item);
            Log.Info($"Forage list: {s_kinds.Count} kinds ({string.Join(", ", s_items)})");
            return true;
        }
    }
}
