using System.Collections.Generic;
using System.Linq;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Core;

namespace VikingColonies.Construction
{
    /// <summary>
    /// Hand over (worker polish part 4, 0.23; MineColonies' arrow in its Required Resources tab): what a work order still
    /// needs beyond what the colony's chests hold for builders goes from the local player's bag into a chest, as much as
    /// the bag has. Equipped items stay.
    /// </summary>
    internal static class HandOverFromBag
    {
        /// <returns>What happened, in the player's language.</returns>
        public static string ToChest(Player player, ConstructionModule site, StorageModule chest)
        {
            if (player == null || site == null || !site.IsValid || site.Building == null || chest == null || site.IsTakingDown) return L("$vc_handover_no_order");
            Dictionary<string, int> needs = site.RemainingNeeds();
            // What his builder already carries counts as there, in "nothing needed" too (0.26, review of 0.23 and of 0.26).
            Dictionary<string, int> stores = HandOver.Available(BuilderTools.StoresForBuilders(site.Building.ColonyId, site.transform.position),
                BuilderCarries(site));
            if (NeedsTally.Shortfall(needs, stores).Count == 0) return L("$vc_handover_nothing_needed");
            Inventory bag = player.GetInventory();
            // The chest's own Keep in stock is topped up first, or the builder couldn't take it (0.26).
            Dictionary<string, int> amounts = HandOver.Amounts(needs, stores, InBag(bag), null, KeepDeficit(chest, needs.Keys));
            if (amounts.Count == 0) return L("$vc_handover_none_in_bag");
            if (chest.Claim() != StorageModule.Access.Ready) return L("$vc_handover_busy");
            var load = new CarryLoad();
            foreach (KeyValuePair<string, int> amount in amounts) load.Add(amount.Key, TakeFromBag(bag, amount.Key, amount.Value));
            var moved = load.ItemNames.ToDictionary(item => item, item => load.Count(item));
            chest.TryDeposit(load);   // what doesn't fit stays in the load
            foreach (string item in load.ItemNames)
            {
                moved[item] -= load.Count(item);
                PutBack(bag, item, load.Count(item), player.transform.position);
            }
            foreach (string item in moved.Where(m => m.Value <= 0).Select(m => m.Key).ToList()) moved.Remove(item);
            if (moved.Count == 0) return L("$vc_handover_full");
            Log.Info($"{player.GetPlayerName()} handed over {string.Join(", ", moved.Select(m => m.Value + " " + m.Key))} for {site.Building.TypeId} {site.Building.Id}");
            return string.Format(L("$vc_handover_done"), L(PiecePlacer.DescribeItems(moved)));
        }

        /// <summary>
        /// What the order's builder carries: nothing when no builder holds it now (his claim lapsed), and from his saved
        /// record when another computer runs him (only that computer keeps his carry up to date; review of 0.26).
        /// </summary>
        internal static Dictionary<string, int> BuilderCarries(ConstructionModule site)
        {
            BuildOrderRecord order = site != null ? site.Order : null;
            if (order == null || !order.HasLiveClaim(ConstructionModule.Now())) return null;
            Citizen builder = Citizen.All.FirstOrDefault(c => c != null && c.Id == order.Builder);
            if (builder == null) return null;
            CarryLoad load = builder.IsOwner ? builder.Carry : CarryLoad.Deserialize(builder.Record.Carry);
            return load.ItemNames.ToDictionary(item => item, item => load.Count(item));
        }

        /// <summary>
        /// How much more of each item the chest keeps than it holds. Nothing for a Builder's Hut: its builders take all of
        /// its chest (their own use), so its list never stands in their way (review of 0.26).
        /// </summary>
        private static Dictionary<string, int> KeepDeficit(StorageModule chest, IEnumerable<string> items)
        {
            ColonyBuilding owner = chest.GetComponent<ColonyBuilding>();
            if (owner != null && owner.TypeId == BuildingTypes.BuilderHutId) return null;
            StockList keep = chest.Keep;
            if (keep == null) return null;
            Dictionary<string, int> holds = chest.Contents();
            var deficit = new Dictionary<string, int>();
            foreach (string item in items)
            {
                int missing = keep.Kept(item) - (holds.TryGetValue(item, out int n) ? n : 0);
                if (missing > 0) deficit[item] = missing;
            }
            return deficit;
        }

        /// <summary>What the bag holds, by item prefab name (equipped items left out).</summary>
        private static Dictionary<string, int> InBag(Inventory bag)
        {
            var items = new Dictionary<string, int>();
            foreach (ItemDrop.ItemData item in bag.GetAllItems())
            {
                if (item.m_dropPrefab == null || item.m_equipped) continue;
                string name = item.m_dropPrefab.name;
                items[name] = (items.TryGetValue(name, out int n) ? n : 0) + item.m_stack;
            }
            return items;
        }

        private static int TakeFromBag(Inventory bag, string prefab, int amount)
        {
            int taken = 0;
            foreach (ItemDrop.ItemData item in bag.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab && !i.m_equipped).ToList())
            {
                if (taken >= amount) break;
                int n = System.Math.Min(item.m_stack, amount - taken);
                if (bag.RemoveItem(item, n)) taken += n;
            }
            return taken;
        }

        /// <summary>What the chest didn't take goes back into the bag; should the bag not take it all, the rest drops at his feet.</summary>
        private static void PutBack(Inventory bag, string prefab, int amount, UnityEngine.Vector3 at)
        {
            UnityEngine.GameObject item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
            if (drop == null) return;
            string name = drop.m_itemData.m_shared.m_name;
            for (int guard = 0; amount > 0 && guard < 100; guard++)
            {
                int before = bag.CountItems(name);
                bag.AddItem(item, amount);
                int added = bag.CountItems(name) - before;
                if (added <= 0) break;
                amount -= added;
            }
            if (amount > 0) ItemDrop.DropItem(drop.m_itemData.Clone(), amount, at + UnityEngine.Vector3.up, UnityEngine.Quaternion.identity);
        }

        private static string L(string token) => Localization.instance.Localize(token);
    }
}
