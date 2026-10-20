using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Core;

namespace VikingColonies.Buildings
{
    /// <summary>Lets the tested Core deposit logic work on a Valheim inventory, by item prefab name.</summary>
    internal sealed class InventoryStore : IItemStore
    {
        private readonly Inventory _inventory;
        private readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();

        public InventoryStore(Inventory inventory) { _inventory = inventory; }

        public bool Accepts(string item) => Prefab(item) != null;

        public bool CanAdd(string item, int amount) => _inventory.CanAddItem(Prefab(item), amount);

        public void Add(string item, int amount) => _inventory.AddItem(Prefab(item), amount);

        public int Count(string item)
        {
            string sharedName = Prefab(item).GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            return _inventory.GetAllItems().Where(i => i.m_shared.m_name == sharedName).Sum(i => i.m_stack);
        }

        private GameObject Prefab(string item)
        {
            if (!_prefabs.TryGetValue(item, out GameObject prefab))
            {
                prefab = ObjectDB.instance.GetItemPrefab(item);
                _prefabs[item] = prefab;
            }
            return prefab;
        }
    }
}
