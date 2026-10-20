using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>One of the game's pickables, as the forager's list needs it (the game side reads these from the game's data).</summary>
    public sealed class PickableInfo
    {
        public PickableInfo(string prefab, string item, string itemName, float respawnMinutes, bool isFood, bool isMaterial, int biomes)
        {
            Prefab = prefab;
            Item = item;
            ItemName = itemName;
            RespawnMinutes = respawnMinutes;
            IsFood = isFood;
            IsMaterial = isMaterial;
            Biomes = biomes;
        }

        public string Prefab { get; }
        /// <summary>The item it gives, as the game knows it ("Dandelion").</summary>
        public string Item { get; }
        /// <summary>The item's name token ("$item_dandelion").</summary>
        public string ItemName { get; }
        /// <summary>How long it takes to grow back; 0 when it never does.</summary>
        public float RespawnMinutes { get; }
        public bool IsFood { get; }
        public bool IsMaterial { get; }
        /// <summary>The game's biome flags the world grows it in, out in the open; 0 when it grows only at certain spots (caves, ruins, special places).</summary>
        public int Biomes { get; }
    }

    /// <summary>One kind on the forager's list: an item that grows wild and grows back.</summary>
    public sealed class ForageKind
    {
        public ForageKind(string item, string name, int biomes, bool isFood, bool offByDefault = false)
        {
            OffByDefault = offByDefault;
            Item = item;
            Name = name;
            Biomes = biomes;
            IsFood = isFood;
        }

        public string Item { get; }
        public string Name { get; }
        public int Biomes { get; }
        /// <summary>Settlers can eat it; the rest waits in his chest for the player.</summary>
        public bool IsFood { get; }
        /// <summary>Unticked until the player ticks it (flint and branches: not plants, and piles of them may not be wanted).</summary>
        public bool OffByDefault { get; }
    }

    /// <summary>
    /// The kinds a forager may pick: every pickable that grows back and gives food or a material, one kind per item, in the
    /// order of the biomes from the meadows on (what grows only at certain spots last). Flint and branches grow back too:
    /// they are on the list but unticked to start (the player's decision of 2026-10-15). What never grows back stays off,
    /// or he would strip the land for good.
    /// </summary>
    public static class ForageCatalog
    {
        private static readonly HashSet<string> s_offByDefault = new HashSet<string>(StringComparer.Ordinal) { "Flint", "Wood", "Frostwood" };
        /// <summary>Meadows, Black Forest, Swamp, Mountain, Plains, Mistlands, Ashlands, Deep North.</summary>
        private static readonly int[] s_biomeOrder = { 1, 8, 2, 4, 16, 512, 32, 64 };

        public static IEnumerable<ForageKind> Kinds(IEnumerable<PickableInfo> pickables) =>
            pickables
                .Where(p => p != null && p.RespawnMinutes > 0f && !string.IsNullOrEmpty(p.Item) && (p.IsFood || p.IsMaterial))
                .GroupBy(p => p.Item, StringComparer.Ordinal)
                .Select(g => new ForageKind(g.Key, g.First().ItemName, g.Aggregate(0, (mask, p) => mask | p.Biomes), g.Any(p => p.IsFood),
                    s_offByDefault.Contains(g.Key)))
                .OrderBy(k => BiomeRank(k.Biomes))
                .ThenBy(k => k.Item, StringComparer.Ordinal)
                .ToList();

        private static int BiomeRank(int biomes)
        {
            for (int i = 0; i < s_biomeOrder.Length; i++)
                if ((biomes & s_biomeOrder[i]) != 0) return i;
            return s_biomeOrder.Length;
        }
    }

    /// <summary>One line of the forager's "What to pick" list.</summary>
    public sealed class ForageLine
    {
        public ForageKind Kind;
        public bool Ticked;
    }

    /// <summary>
    /// The forager's list: every kind ticked until the player unticks it, but flint and branches unticked until the player
    /// ticks them. The hut saves what the player changed (<paramref name="saved"/>): an unticked kind by name ("Dandelion"),
    /// a ticked off-to-start kind as "+name" ("+Flint").
    /// </summary>
    public static class ForageChoice
    {
        public static List<ForageLine> Lines(IEnumerable<ForageKind> kinds, ICollection<string> saved) =>
            kinds.Select(k => new ForageLine { Kind = k, Ticked = IsTicked(k, saved) }).ToList();

        public static bool IsTicked(ForageKind kind, ICollection<string> saved) => saved.Contains(Entry(kind)) == kind.OffByDefault;

        /// <summary>The name a change to this kind is saved under.</summary>
        public static string Entry(ForageKind kind) => kind.OffByDefault ? "+" + kind.Item : kind.Item;

        /// <summary>Whether the kind's entry is in the saved list once it's <paramref name="ticked"/> (or not).</summary>
        public static bool EntrySaved(ForageKind kind, bool ticked) => kind.OffByDefault ? ticked : !ticked;

        /// <summary>He picks an item when its kind is on the list and ticked.</summary>
        public static bool Wants(string item, IEnumerable<ForageKind> kinds, ICollection<string> saved)
        {
            if (string.IsNullOrEmpty(item)) return false;
            ForageKind kind = kinds.FirstOrDefault(k => k.Item == item);
            return kind != null && IsTicked(kind, saved);
        }
    }

    /// <summary>A list of names saved as one text ("a,b,c"): what the player unticked on a hut's list.</summary>
    public static class TickList
    {
        public static HashSet<string> Parse(string saved) =>
            new HashSet<string>((saved ?? "").Split(',').Select(p => p.Trim()).Where(p => p.Length > 0), StringComparer.Ordinal);

        public static string Save(IEnumerable<string> names) =>
            string.Join(",", names.Where(u => !string.IsNullOrEmpty(u)).Distinct().OrderBy(u => u, StringComparer.Ordinal));
    }
}
