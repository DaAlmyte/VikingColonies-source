using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;

namespace VikingColonies.Citizens
{
    /// <summary>Food as the game counts it for players: what settlers eat and newcomers are fed with.</summary>
    internal static class FoodItems
    {
        /// <summary>How filling an item is for a player (health and stamina); 0 for what isn't food.</summary>
        public static float Value(string item)
        {
            ItemDrop.ItemData.SharedData shared = Shared(item);
            return shared != null && shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable ? shared.m_food + shared.m_foodStamina : 0f;
        }

        /// <summary>The item's name in the player's language.</summary>
        public static string Name(string item)
        {
            ItemDrop.ItemData.SharedData shared = Shared(item);
            return shared != null && Localization.instance != null ? Localization.instance.Localize(shared.m_name) : item;
        }

        /// <summary>The food among a chest's contents, as <see cref="Meals"/> chooses from it.</summary>
        public static IEnumerable<FoodStock> In(IReadOnlyDictionary<string, int> contents) =>
            contents.Select(c => new FoodStock(c.Key, c.Value, Value(c.Key))).Where(f => f.Value > 0f);

        /// <summary>How many food items a chest holds.</summary>
        public static int Count(IReadOnlyDictionary<string, int> contents) => In(contents).Sum(f => f.Count);

        private static ItemDrop.ItemData.SharedData Shared(string item)
        {
            ItemDrop drop = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(item)?.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData.m_shared : null;
        }
    }
}
