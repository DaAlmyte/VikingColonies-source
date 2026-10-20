using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Gui
{
    /// <summary>The game's biome flags as names in the player's language ("Meadows, Black Forest").</summary>
    internal static class BiomeText
    {
        private static readonly KeyValuePair<int, string>[] s_biomes =
        {
            new KeyValuePair<int, string>(1, "$biome_meadows"), new KeyValuePair<int, string>(8, "$biome_blackforest"),
            new KeyValuePair<int, string>(2, "$biome_swamp"), new KeyValuePair<int, string>(4, "$biome_mountain"),
            new KeyValuePair<int, string>(16, "$biome_plains"), new KeyValuePair<int, string>(512, "$biome_mistlands"),
            new KeyValuePair<int, string>(32, "$biome_ashlands"), new KeyValuePair<int, string>(64, "$biome_deepnorth"),
        };

        public static string Names(int mask) =>
            string.Join(", ", s_biomes.Where(b => (mask & b.Key) != 0).Select(b => Localization.instance != null ? Localization.instance.Localize(b.Value) : b.Value));
    }
}
