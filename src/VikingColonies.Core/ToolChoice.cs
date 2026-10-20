using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>A tool the game knows: its item, its name token, what it is for and how hard a thing it can work.</summary>
    public sealed class ToolInfo
    {
        public ToolInfo(string prefab, string name, ToolKind kind, int tier)
        {
            Prefab = prefab ?? "";
            Name = name ?? "";
            Kind = kind;
            Tier = tier;
        }

        public string Prefab { get; }
        public string Name { get; }
        public ToolKind Kind { get; }
        public int Tier { get; }
    }

    /// <summary>What work needing a tool waits on: nothing, a higher hut level, or a tool in the colony's chests.</summary>
    public enum ToolNeed { Ready, HutLevel, Tool }

    public static class ToolChoice
    {
        /// <summary>
        /// Whether a worker at this hut level can do work needing a tool of this kind and tier with the tools at hand.
        /// Ready: <paramref name="tool"/> is the best one his level allows. HutLevel: his level's cap is below the tier;
        /// <paramref name="needsLevel"/> is the hut level it takes (0 when no level does). Tool: none at hand within his cap.
        /// </summary>
        public static ToolNeed ForLevel(IEnumerable<ToolInfo> atHand, ToolKind kind, int tier, int hutLevel, out ToolInfo tool, out int needsLevel)
        {
            tool = null;
            needsLevel = 0;
            int cap = HutLevels.ToolTierCap(kind, hutLevel);
            if (tier > cap)
            {
                needsLevel = HutLevels.LevelForTool(kind, tier);
                return ToolNeed.HutLevel;
            }
            tool = Best(atHand, kind, tier, cap);
            return tool != null ? ToolNeed.Ready : ToolNeed.Tool;
        }

        /// <summary>The best tool of a kind that is good enough (highest tier; ties by item name), or null.</summary>
        public static ToolInfo Best(IEnumerable<ToolInfo> tools, ToolKind kind, int minTier) =>
            GoodEnough(tools, kind, minTier).OrderByDescending(t => t.Tier).ThenBy(t => t.Prefab, StringComparer.Ordinal).FirstOrDefault();

        /// <summary>The best tool of a kind with a tier from <paramref name="minTier"/> to <paramref name="maxTier"/>, or null.</summary>
        public static ToolInfo Best(IEnumerable<ToolInfo> tools, ToolKind kind, int minTier, int maxTier) =>
            Best(GoodEnough(tools, kind, minTier).Where(t => t.Tier <= maxTier), kind, minTier);

        /// <summary>The weakest tool of a kind that would do, to name in "Antler pickaxe or better"; null when none would.</summary>
        public static ToolInfo Weakest(IEnumerable<ToolInfo> tools, ToolKind kind, int minTier) =>
            GoodEnough(tools, kind, minTier).OrderBy(t => t.Tier).ThenBy(t => t.Prefab, StringComparer.Ordinal).FirstOrDefault();

        private static IEnumerable<ToolInfo> GoodEnough(IEnumerable<ToolInfo> tools, ToolKind kind, int minTier) =>
            tools.Where(t => t != null && t.Kind == kind && t.Tier >= minTier);
    }
}
