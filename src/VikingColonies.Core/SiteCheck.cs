using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    public enum SiteColour { Green, Yellow, Red }

    /// <summary>What the builder clears from a site, in the order he does it.</summary>
    public enum ClearKind { Item, Plant, Tree, Log, Stump, Rock, Leftover }

    public enum ToolKind { Axe, Pickaxe, Hoe }

    /// <summary>What a look at a site found: what's in the way, how the ground must change, and why it can't be used.</summary>
    public sealed class SiteFindings
    {
        private readonly SortedDictionary<ClearKind, int> _clear = new SortedDictionary<ClearKind, int>();
        private readonly SortedSet<RedReason> _red = new SortedSet<RedReason>();
        private readonly List<KeyValuePair<ClearKind, int>> _tiers = new List<KeyValuePair<ClearKind, int>>();

        public IReadOnlyDictionary<ClearKind, int> Clear => _clear;
        /// <summary>Every reason found, first reason first.</summary>
        public IReadOnlyCollection<RedReason> Red => _red;
        /// <summary>Every tree, log, stump and rock with the tool tier it needs (a big rock once, with its hardest part).</summary>
        public IReadOnlyList<KeyValuePair<ClearKind, int>> Tiers => _tiers;
        /// <summary>The best axe tier any tree, log or stump needs; -1 when there are none.</summary>
        public int AxeTier { get; private set; } = -1;
        /// <summary>The best pickaxe tier any rock needs; -1 when there are none.</summary>
        public int PickaxeTier { get; private set; } = -1;
        /// <summary>Some ground is higher than the target.</summary>
        public bool Dig { get; set; }
        /// <summary>Some ground is more than half a metre too high (a pickaxe digs it; the hoe levels the rest).</summary>
        public bool DeepDig { get; set; }
        /// <summary>Some ground is lower than the target.</summary>
        public bool Raise { get; set; }
        /// <summary>Stone needed to raise the low ground.</summary>
        public int Stone { get; set; }
        /// <summary>The floor (the chest's height), how far the design reaches below it, the natural ground, the ground's target.</summary>
        public float Floor { get; set; }
        public float ReachBelow { get; set; }
        public float Natural { get; set; }
        public float Target { get; set; }
        /// <summary>What nothing breaks on the site, named for the log (RedReason.Unbreakable).</summary>
        public List<string> Unbreakable { get; } = new List<string>();

        public void Add(ClearKind kind, int toolTier = 0)
        {
            _clear[kind] = (_clear.TryGetValue(kind, out int n) ? n : 0) + 1;
            toolTier = Math.Max(0, toolTier);
            ToolKind? tool = SiteCheck.ToolFor(kind);
            if (tool == ToolKind.Axe) AxeTier = Math.Max(AxeTier, toolTier);
            if (tool == ToolKind.Pickaxe) PickaxeTier = Math.Max(PickaxeTier, toolTier);
            if (tool != null) _tiers.Add(new KeyValuePair<ClearKind, int>(kind, toolTier));
        }

        public void AddRed(RedReason reason)
        {
            if (reason != RedReason.None) _red.Add(reason);
        }
    }

    /// <summary>The colour of a site's outline, and what the line under it says.</summary>
    public sealed class SiteVerdict
    {
        public SiteColour Colour { get; set; }
        public RedReason Red { get; set; }
        public IReadOnlyList<KeyValuePair<ClearKind, int>> Clear { get; set; }
        public bool Flatten { get; set; }
        public int Stone { get; set; }
        public IReadOnlyList<ToolKind> Tools { get; set; }
        public int AxeTier { get; set; }
        public int PickaxeTier { get; set; }
        /// <summary>What needs a better tool than the colony's best Builder's Hut allows: by kind (in clearing order) and count.</summary>
        public IReadOnlyList<KeyValuePair<ClearKind, int>> AboveLevel { get; set; }
        /// <summary>The Builder's Hut level that clears all of <see cref="AboveLevel"/>; 0 when nothing is above, or some of it is beyond every level.</summary>
        public int NeedsLevel { get; set; }
    }

    public static class SiteCheck
    {
        /// <summary>
        /// Red when anything makes the site unusable (the first reason is shown); yellow when the builder has something
        /// to clear or reshape first (with the tools he'll need: an axe for wood, a pickaxe for rocks, a
        /// hoe for any flattening, a pickaxe for deep digging); green when he can start building straight away.
        /// <paramref name="builderHutLevel"/> is the colony's best Builder's Hut level (0 or 1: none yet, or level 1): what
        /// needs a better tool than that level allows is listed with the level it takes; the colour stays.
        /// </summary>
        /// <param name="atHand">The tools in the colony's chests: those good enough for the work are not asked for (null: ask for every tool the work needs).</param>
        public static SiteVerdict Judge(SiteFindings findings, int builderHutLevel = HutLevels.MaxLevel, IEnumerable<ToolInfo> atHand = null)
        {
            var tools = new List<ToolKind>();
            if (findings.AxeTier >= 0) tools.Add(ToolKind.Axe);
            int pickaxeTier = Math.Max(findings.PickaxeTier, findings.DeepDig ? 0 : -1);
            if (pickaxeTier >= 0) tools.Add(ToolKind.Pickaxe);
            bool flatten = findings.Dig || findings.Raise;
            if (flatten) tools.Add(ToolKind.Hoe);
            // Seen: "needs an axe, a pickaxe and a hoe in the colony's chests" with an axe and a hoe in them. Only what's missing
            // for the work his level allows is asked for; work above his level gets the level line instead.
            if (atHand != null)
            {
                List<ToolInfo> held = atHand.Where(t => t != null).ToList();
                tools = tools.Where(tool => !Covered(findings, tool, builderHutLevel, held)).ToList();
            }
            RedReason red = findings.Red.Count > 0 ? findings.Red.First() : RedReason.None;
            bool work = findings.Clear.Count > 0 || flatten;
            var above = new SortedDictionary<ClearKind, int>();
            int needsLevel = 0;
            bool beyond = false;
            foreach (KeyValuePair<ClearKind, int> thing in findings.Tiers)
            {
                ToolKind tool = ToolFor(thing.Key) ?? ToolKind.Axe;
                if (thing.Value <= HutLevels.ToolTierCap(tool, builderHutLevel)) continue;
                above[thing.Key] = (above.TryGetValue(thing.Key, out int n) ? n : 0) + 1;
                int level = HutLevels.LevelForTool(tool, thing.Value);
                if (level == 0) beyond = true;
                else needsLevel = Math.Max(needsLevel, level);
            }
            return new SiteVerdict
            {
                Colour = red != RedReason.None ? SiteColour.Red : work ? SiteColour.Yellow : SiteColour.Green,
                Red = red,
                Clear = findings.Clear.ToList(),
                Flatten = flatten,
                Stone = findings.Raise ? findings.Stone : 0,
                Tools = tools,
                AxeTier = findings.AxeTier,
                PickaxeTier = pickaxeTier,
                AboveLevel = above.ToList(),
                NeedsLevel = beyond ? 0 : needsLevel,
            };
        }

        /// <summary>The colony's chests hold a tool of this kind good enough for the work within the builder's level (or none is needed there).</summary>
        private static bool Covered(SiteFindings findings, ToolKind tool, int builderHutLevel, IReadOnlyList<ToolInfo> held)
        {
            int cap = HutLevels.ToolTierCap(tool, builderHutLevel);
            int need = -1;
            foreach (KeyValuePair<ClearKind, int> thing in findings.Tiers)
                if ((ToolFor(thing.Key) ?? ToolKind.Axe) == tool && thing.Value <= cap) need = Math.Max(need, thing.Value);
            if (tool == ToolKind.Pickaxe && findings.DeepDig) need = Math.Max(need, 0);
            if (tool == ToolKind.Hoe && (findings.Dig || findings.Raise)) need = Math.Max(need, 0);
            if (need < 0) return true;   // only work above his level: the level line says so
            return ToolChoice.Best(held, tool, need, cap) != null;
        }

        /// <summary>The tool the builder clears a kind of thing with: an axe for wood, a pickaxe for rocks; null for the rest (by hand or hammer).</summary>
        public static ToolKind? ToolFor(ClearKind kind)
        {
            switch (kind)
            {
                case ClearKind.Tree:
                case ClearKind.Log:
                case ClearKind.Stump:
                    return ToolKind.Axe;
                case ClearKind.Rock:
                    return ToolKind.Pickaxe;
                default:
                    return null;
            }
        }
    }
}
