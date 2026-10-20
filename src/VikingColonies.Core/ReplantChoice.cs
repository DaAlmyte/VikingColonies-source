using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>One line of the Lumberjack's Hut's replanting list.</summary>
    public sealed class ReplantLine
    {
        public SaplingInfo Sapling;
        /// <summary>The hut's level and biome allow it.</summary>
        public bool Offered;
        /// <summary>Offered and not unticked by the player: he plants it.</summary>
        public bool Ticked;
        /// <summary>When the level's axe can't fell the grown tree: the hut level that can; else 0.</summary>
        public int NeedsLevel;
        /// <summary>It doesn't grow where the hut stands.</summary>
        public bool WrongBiome;
    }

    /// <summary>
    /// The replanting list: which sapling kinds a Lumberjack's Hut offers (its level's axe can fell the grown tree, and it
    /// grows where the hut stands), which are ticked (all offered ones the player didn't untick), and which to plant
    /// next at a felled tree's spot (the ticked kinds in turn, each only where it grows and while a seed is at hand).
    /// </summary>
    public static class ReplantChoice
    {
        public static List<ReplantLine> Lines(IEnumerable<SaplingInfo> saplings, int hutLevel, int hutBiome, ICollection<string> unticked)
        {
            int cap = HutLevels.ToolTierCap(ToolKind.Axe, hutLevel);
            var lines = new List<ReplantLine>();
            foreach (SaplingInfo s in saplings)
            {
                bool axe = s.TreeTier <= cap, biome = Grows(s, hutBiome);
                lines.Add(new ReplantLine
                {
                    Sapling = s,
                    Offered = axe && biome,
                    Ticked = axe && biome && !unticked.Contains(s.Sapling),
                    NeedsLevel = axe ? 0 : HutLevels.LevelForTool(ToolKind.Axe, s.TreeTier),
                    WrongBiome = !biome,
                });
            }
            return lines;
        }

        /// <summary>The next ticked kind, from <paramref name="turn"/> on, that grows at the spot and has a seed; null if none (turn unchanged).</summary>
        public static SaplingInfo Next(IReadOnlyList<SaplingInfo> ticked, int spotBiome, Func<string, bool> hasSeed, ref int turn)
        {
            int count = ticked.Count;
            for (int i = 0; i < count; i++)
            {
                int index = ((turn % count) + count + i) % count;
                SaplingInfo s = ticked[index];
                if (!Grows(s, spotBiome) || !hasSeed(s.Seed)) continue;
                turn = index + 1;
                return s;
            }
            return null;
        }

        public static HashSet<string> ParseUnticked(string saved) => TickList.Parse(saved);

        public static string SaveUnticked(IEnumerable<string> unticked) => TickList.Save(unticked);

        private static bool Grows(SaplingInfo s, int biome) => s.Biomes == -1 || biome == 0 || (s.Biomes & biome) != 0;
    }
}
