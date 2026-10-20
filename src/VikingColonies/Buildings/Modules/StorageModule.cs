using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Buildings
{
    /// <summary>A building's chest: deposit and take items, with the tested transfer and claim rules.</summary>
    public class StorageModule : MonoBehaviour
    {
        public enum DepositResult { Stored, StorageFull, Busy }

        /// <summary>Whether a settler may change the chest now: Waiting while its owner hands it over (or a player has it open there), Busy while it is open on this computer without being ours.</summary>
        public enum Access { Ready, Waiting, Busy }

        private const string RpcHandOver = "vc_HandOver";
        private readonly StorageClaim _claim = new StorageClaim();

        private static readonly AccessTools.FieldRef<Inventory, int> s_inventoryHeight = AccessTools.FieldRefAccess<Inventory, int>("m_height");
        // The game's own reload of a chest from its saved contents; it does nothing when nothing new was saved.
        private static readonly Func<Container, bool> s_load = AccessTools.MethodDelegate<Func<Container, bool>>(AccessTools.Method(typeof(Container), "Load"));
        private const float LevelCheckSeconds = 1f;

        private ZNetView _nview;
        private Container _container;
        private StockModule _stock;
        private int _baseRows = -1;
        private float _levelCheck;

        /// <summary>The chest's size as columns x rows, for the test report.</summary>
        public string SizeText => _container != null && _container.GetInventory() != null
            ? $"{_container.GetInventory().GetWidth()}x{_container.GetInventory().GetHeight()}" : "";

        public int Rows => _container != null && _container.GetInventory() != null ? _container.GetInventory().GetHeight() : 0;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _container = GetComponent<Container>();
            _stock = GetComponent<StockModule>();
            if (_nview != null && _nview.IsValid()) _nview.Register(RpcHandOver, OnHandOver);
            ApplyRows();
        }

        private void Update()
        {
            _levelCheck -= Time.deltaTime;
            if (_levelCheck > 0f) return;
            _levelCheck = LevelCheckSeconds;
            ApplyRows();   // an upgrade finished on any computer: every computer's copy of the chest grows
        }

        /// <summary>
        /// A colony building's chest has a row more per level (Storage › RowsPerLevel); it only grows. Runs in Awake, before the chest
        /// loads its items (the game loads them on its first check, after every Awake), so items in the extra rows load
        /// on every computer. The level is read from the hut's ZDO (ColonyBuilding may not have woken up yet).
        /// </summary>
        public void ApplyRows()
        {
            if (_container == null || _nview == null || !_nview.IsValid()) return;
            ColonyBuilding building = GetComponent<ColonyBuilding>();
            if (building == null) return;   // the Town Hall's chest too, by its level (1.4)
            Inventory inventory = _container.GetInventory();
            if (inventory == null) return;
            if (_baseRows < 0) _baseRows = inventory.GetHeight();
            int level = _nview.GetZDO().GetInt(ColonyBuilding.LevelKey, 1);
            int rows = HutLevels.ChestRows(_baseRows, ColonyConfig.RowsPerLevel.Value, level);
            if (rows <= inventory.GetHeight()) return;
            s_inventoryHeight(inventory) = rows;
            _container.m_height = rows;
        }

        /// <summary>
        /// Moves as much of the load as fits, holding back <paramref name="keep"/> amounts
        /// (e.g. seeds reserved for replanting). Whatever doesn't fit stays in the load.
        /// </summary>
        public DepositResult TryDeposit(CarryLoad load, IReadOnlyDictionary<string, int> keep = null)
        {
            if (Claim() != Access.Ready) return DepositResult.Busy;
            var discarded = new List<string>();
            int leftover = ItemTransfer.Deposit(load, new InventoryStore(_container.GetInventory()), keep, discarded);
            foreach (string item in discarded) Log.Warn($"Unknown item '{item}' in a settler's load; discarding it.");
            return leftover == 0 ? DepositResult.Stored : DepositResult.StorageFull;
        }

        public bool HasItem(string prefabName) => _container.GetInventory().GetItem(prefabName, -1, true) != null;

        /// <summary>
        /// Takes every item of <paramref name="cost"/> out of the chest (raising the Town Hall, 1.4), all or nothing; the
        /// caller has claimed the chest. False, with nothing taken, when one is short or unknown.
        /// </summary>
        public bool TakeAll(IEnumerable<KeyValuePair<string, int>> cost)
        {
            List<KeyValuePair<string, int>> items = cost.ToList();
            Dictionary<string, int> have = Contents();
            if (items.Any(c => !have.TryGetValue(c.Key, out int n) || n < c.Value)) return false;
            // Stack by stack of that very prefab (review of 1.4: by its shared name, another mod's item of the same name could go,
            // and the game's removal by name may pass over some stacks).
            Inventory inventory = _container.GetInventory();
            foreach (KeyValuePair<string, int> item in items)
            {
                int left = item.Value;
                foreach (ItemDrop.ItemData stack in inventory.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == item.Key).ToList())
                {
                    int take = System.Math.Min(left, stack.m_stack);
                    if (inventory.RemoveItem(stack, take)) left -= take;
                    if (left <= 0) break;
                }
                if (left > 0) Log.Warn($"Raising the Town Hall: {left} {item.Key} could not be taken from its chest");
            }
            return true;
        }

        /// <summary>
        /// Takes one item (e.g. a seed) out of storage into the load. Only what the chest can spare, unless it is for the
        /// building's own use (its worker, the Town Hall feeding newcomers): false when nothing is spare.
        /// </summary>
        public bool TryTakeItem(string prefabName, CarryLoad into, bool ownUse = false)
        {
            if (!ownUse && Spare(prefabName) <= 0) return false;   // on the copy here first: no hand-over for nothing (0.24)
            if (Claim() != Access.Ready) return false;
            if (!ownUse && Spare(prefabName) <= 0) return false;   // again on the latest contents
            Inventory inventory = _container.GetInventory();
            ItemDrop.ItemData item = inventory.GetItem(prefabName, -1, true);
            if (item == null || !StockRules.Carried(item.m_shared.m_maxStackSize) || !inventory.RemoveItem(item, 1)) return false;
            into.Add(prefabName, 1);
            return true;
        }

        /// <summary>Whether settlers carry this kind of item between chests (<see cref="StockRules.Carried"/>).</summary>
        public static bool Carried(string prefabName)
        {
            ItemDrop drop = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName)?.GetComponent<ItemDrop>() : null;
            return drop != null && StockRules.Carried(drop.m_itemData.m_shared.m_maxStackSize);
        }

        /// <summary>The food test the Keep in stock rules use (Food (any)).</summary>
        public static bool IsFood(string item) => FoodItems.Value(item) > 0f;

        /// <summary>The building's Keep in stock list (nothing for a chest without one).</summary>
        public StockList Keep => _stock != null ? _stock.List : null;

        /// <summary>How many of an item may be taken: all for the building's own use, else what's beyond its kept amount.</summary>
        public int Spare(string item, bool ownUse = false) => StockRules.Spare(Contents(), ownUse ? null : Keep, item, IsFood, ownUse);

        /// <summary>What the chest can spare, by item (items with none spare left out).</summary>
        public Dictionary<string, int> SpareContents(bool ownUse = false)
        {
            Dictionary<string, int> contents = Contents();
            if (ownUse) return contents;
            StockList keep = Keep;
            var spare = new Dictionary<string, int>();
            foreach (KeyValuePair<string, int> item in contents)
            {
                int n = StockRules.Spare(contents, keep, item.Key, IsFood, false);
                if (n > 0) spare[item.Key] = n;
            }
            return spare;
        }

        /// <summary>Whether one of the item would fit (a carry run brings it here only then).</summary>
        public bool CanTake(string item)
        {
            if (_container == null || string.IsNullOrEmpty(item)) return false;
            var store = new InventoryStore(_container.GetInventory());
            return store.Accepts(item) && store.CanAdd(item, 1);
        }

        /// <summary>Whether at least one item of the load would fit (a full worker's load may go here).</summary>
        public bool HasRoomFor(CarryLoad load)
        {
            if (_container == null || load == null) return false;
            var store = new InventoryStore(_container.GetInventory());
            foreach (string item in load.ItemNames)
                if (store.Accepts(item) && store.CanAdd(item, 1)) return true;
            return false;
        }

        /// <summary>What the chest holds, counted by item prefab name.</summary>
        public Dictionary<string, int> Contents()
        {
            var items = new Dictionary<string, int>();
            if (_container == null) return items;
            foreach (ItemDrop.ItemData item in _container.GetInventory().GetAllItems())
            {
                if (item.m_dropPrefab == null) continue;
                string name = item.m_dropPrefab.name;
                items[name] = (items.TryGetValue(name, out int n) ? n : 0) + item.m_stack;
            }
            return items;
        }

        /// <summary>
        /// Storage changes must happen on the computer that owns the chest. One that another computer owns is asked for:
        /// its owner sends the latest contents and hands it over (as the game does for a player opening a chest), so an
        /// old copy is never saved over a friend's change. Taken over directly only when nobody owns it, or its owner
        /// doesn't answer within a few seconds.
        /// </summary>
        public Access Claim()
        {
            if (_nview == null || !_nview.IsValid()) return Access.Busy;
            ZDO zdo = _nview.GetZDO();
            // IsInUse() only knows about this computer; the shared flag covers a player on another computer.
            bool openOnNetwork = zdo.GetInt(ZDOVars.s_inUse, 0) == 1;
            switch (_claim.Next(_nview.IsOwner(), _container.IsInUse(), openOnNetwork, zdo.GetOwner(), Time.time))
            {
                case StorageStep.Use:
                    Reload();
                    return Access.Ready;
                case StorageStep.Busy:
                    return Access.Busy;
                case StorageStep.Ask:
                    _nview.InvokeRPC(zdo.GetOwner(), RpcHandOver);
                    return Access.Waiting;
                case StorageStep.Take:
                    _nview.ClaimOwnership();
                    if (!_nview.IsOwner()) return Access.Waiting;
                    Reload();
                    return Access.Ready;
                default:
                    return Access.Waiting;
            }
        }

        /// <summary>
        /// Before a settler changes the chest: load what another computer saved last. The game reloads a chest only once a
        /// second, and a change made to an older copy would be saved over the newer one (a friend's items lost or doubled).
        /// </summary>
        private void Reload() => s_load?.Invoke(_container);

        /// <summary>On the chest's owner: a settler on another computer asks for it. Unless a player has it open, send him the latest contents, then hand it over.</summary>
        private void OnHandOver(long sender)
        {
            if (!_nview.IsOwner() || sender == ZNet.GetUID()) return;
            // Only a player who has it open here counts: a flag left by a player who crashed with it open doesn't.
            if (_container.IsInUse()) return;   // he asks again
            ZDOMan.instance.ForceSendZDO(sender, _nview.GetZDO().m_uid);
            _nview.GetZDO().SetOwner(sender);
        }
    }
}
