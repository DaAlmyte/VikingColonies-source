using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>
    /// Which kind a tree prefab is, for the Lumberjack's Hut's Trees list (worker polish part 3, 0.22): the game's trees by
    /// their names ("Beech_small1" is a beech), a tree a mod adds by its name without numbers and size words.
    /// </summary>
    public static class TreeKinds
    {
        /// <summary>The game's tree names by their stem, matched as a whole word (0.24: "FireTree" or "Pineapple" is a mod's own).</summary>
        private static readonly KeyValuePair<string, string>[] s_known =
        {
            new KeyValuePair<string, string>("beech", "beech"), new KeyValuePair<string, string>("birch", "birch"),
            new KeyValuePair<string, string>("firtree", "fir"), new KeyValuePair<string, string>("pinetree", "pine"),
            new KeyValuePair<string, string>("oak", "oak"), new KeyValuePair<string, string>("swamptree", "ancient"),
            new KeyValuePair<string, string>("yggashoot", "yggdrasil"), new KeyValuePair<string, string>("snowfirtree", "snowfir"),
        };

        /// <summary>The Ashlands' trees, whatever follows (their names aren't known here).</summary>
        private const string AshlandsStem = "ashlands";

        private static readonly string[] s_suffixes = { "_small", "_aut", "_old", "_" };

        public static string Of(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return "";
            string lower = prefab.ToLowerInvariant();
            foreach (KeyValuePair<string, string> known in s_known)
                if (lower.StartsWith(known.Key, StringComparison.Ordinal) && WordEnds(prefab, known.Key.Length)) return known.Value;
            if (lower.StartsWith(AshlandsStem, StringComparison.Ordinal)) return "ashwood";
            string name = prefab;
            for (bool changed = true; changed;)
            {
                changed = false;
                string trimmed = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                foreach (string suffix in s_suffixes)
                    if (trimmed.Length > suffix.Length && trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        trimmed = trimmed.Substring(0, trimmed.Length - suffix.Length);
                if (trimmed != name && trimmed.Length > 0)
                {
                    name = trimmed;
                    changed = true;
                }
            }
            return name.Trim();   // 0.24: a mod name with spaces
        }

        /// <summary>The stem ends a word there: the name ends, or a digit, "_" or a capital follows ("FirTreeSmall", "PineTreeOLD").</summary>
        private static bool WordEnds(string name, int at) =>
            at >= name.Length || char.IsDigit(name[at]) || name[at] == '_' || name[at] == ' ' || char.IsUpper(name[at]);   // 'SnowFirTree 2' (1.0 play-through)

        /// <summary>"$vc_tree_beech" for the game's kinds; "" for a mod's (the list shows its name).</summary>
        public static string Token(string kind) => kind == "ashwood" || s_known.Any(k => k.Value == kind) ? "$vc_tree_" + kind : "";
    }

    /// <summary>One line of the Trees list: a tree kind, whether he fells it and whether he replants its sapling.</summary>
    public sealed class TreeLine
    {
        public string Kind = "";
        /// <summary>Its name token, or "" (then the list shows <see cref="Kind"/>).</summary>
        public string Token = "";
        /// <summary>The axe tier its easiest tree needs.</summary>
        public int Tier;
        /// <summary>The hut's level allows an axe that fells it.</summary>
        public bool CanFell;
        /// <summary>When it can't: the hut level that can; else 0.</summary>
        public int NeedsLevel;
        /// <summary>He fells it: he can, and the player didn't untick it.</summary>
        public bool Fell;
        /// <summary>The saplings its trees grow from (a mod may add a second one for a kind; review of 0.22); empty for none.</summary>
        public List<SaplingInfo> Saplings = new List<SaplingInfo>();
        /// <summary>The first of <see cref="Saplings"/>, or null.</summary>
        public SaplingInfo Sapling => Saplings.Count > 0 ? Saplings[0] : null;
        /// <summary>Some of its saplings may be planted here (the hut's level and biome allow them).</summary>
        public bool ReplantOffered;
        /// <summary>Its saplings' trees are above his axe: the hut level that replants them; else 0 (0.24).</summary>
        public int ReplantNeedsLevel;
        public bool Replant;
        public bool WrongBiome;
    }

    /// <summary>
    /// The Trees list (0.22, MineColonies' Saplings tab; it replaces the replanting list): one line per tree kind with a
    /// Fell tick (on for every kind his axe allows until the player unticks it) and a Replant tick (the replanting
    /// choice of its sapling, saved as before, so old worlds keep theirs).
    /// </summary>
    public static class TreeChoice
    {
        /// <param name="trees">Every tree prefab and the axe tier it needs.</param>
        /// <param name="replant">The replanting list's lines (<see cref="ReplantChoice.Lines"/>).</param>
        /// <param name="saplingsOfTree">The saplings a tree grows from (none, one, or a mod's second).</param>
        /// <param name="unfelled">The kinds the player unticked from Fell.</param>
        public static List<TreeLine> Lines(IReadOnlyDictionary<string, int> trees, IReadOnlyList<ReplantLine> replant,
            Func<string, IEnumerable<string>> saplingsOfTree, int hutLevel, ICollection<string> unfelled)
        {
            int cap = HutLevels.ToolTierCap(ToolKind.Axe, hutLevel);
            var lines = new List<TreeLine>();
            foreach (IGrouping<string, KeyValuePair<string, int>> kind in (trees ?? new Dictionary<string, int>()).GroupBy(t => TreeKinds.Of(t.Key)))
            {
                if (kind.Key.Length == 0) continue;
                int tier = kind.Min(t => t.Value);
                // Every sapling of its trees: a replanting line each, all on this kind's line (review of 0.22).
                var names = new HashSet<string>(kind.SelectMany(t => saplingsOfTree?.Invoke(t.Key) ?? Enumerable.Empty<string>())
                    .Where(s => !string.IsNullOrEmpty(s)));
                List<ReplantLine> replantLines = replant == null ? new List<ReplantLine>()
                    : replant.Where(l => l.Sapling != null && names.Contains(l.Sapling.Sapling)).ToList();
                bool canFell = tier <= cap;
                lines.Add(new TreeLine
                {
                    Kind = kind.Key,
                    Token = TreeKinds.Token(kind.Key),
                    Tier = tier,
                    CanFell = canFell,
                    NeedsLevel = canFell ? 0 : HutLevels.LevelForTool(ToolKind.Axe, tier),
                    Fell = canFell && (unfelled == null || !unfelled.Contains(kind.Key)),
                    Saplings = replantLines.Select(l => l.Sapling).ToList(),
                    ReplantOffered = replantLines.Any(l => l.Offered),
                    ReplantNeedsLevel = replantLines.Any(l => l.Offered) ? 0
                        : replantLines.Where(l => l.NeedsLevel > 0).Select(l => l.NeedsLevel).DefaultIfEmpty(0).Min(),
                    Replant = replantLines.Any(l => l.Ticked),   // ticked while any of its saplings is
                    WrongBiome = replantLines.Count > 0 && replantLines.All(l => l.WrongBiome),
                });
            }
            return lines.OrderBy(l => l.Tier).ThenBy(l => l.Kind, StringComparer.Ordinal).ToList();
        }

        /// <summary>Whether he may fell a tree of this prefab (its kind isn't unticked).</summary>
        public static bool MayFell(string prefab, ICollection<string> unfelled) =>
            unfelled == null || unfelled.Count == 0 || !unfelled.Contains(TreeKinds.Of(prefab));
    }

    /// <summary>Trees by the player's buildings stay standing (0.22, Lumberjack.LeaveTreesNearBuildings).</summary>
    public static class TreeRules
    {
        /// <summary>
        /// A piece a player built that is a structure; not a field marker (a sign piece, put where trees are to go; review
        /// of 0.22), nor a plant, a crop or someone's lost piece.
        /// </summary>
        public static bool CountsAsBuilding(bool placedByPlayer, bool structure, bool fieldMarker) => placedByPlayer && structure && !fieldMarker;

        /// <summary>
        /// A tree within <paramref name="metres"/> of any part of one of the player's pieces, by each piece's box on the
        /// ground (0.26, review of 0.22: by its middle, a tree by the end of a long wall was felled).
        /// </summary>
        /// <param name="metres">0 or less: off.</param>
        public static bool NearBuilding(GroundPoint tree, IReadOnlyList<GroundBox> buildingPieces, float metres)
        {
            if (metres <= 0f || buildingPieces == null) return false;
            float squared = metres * metres;
            foreach (GroundBox piece in buildingPieces)
            {
                float dx = Math.Max(0f, Math.Max(piece.MinX - tree.X, tree.X - piece.MaxX));
                float dz = Math.Max(0f, Math.Max(piece.MinZ - tree.Z, tree.Z - piece.MaxZ));
                if (dx * dx + dz * dz <= squared) return true;
            }
            return false;
        }
    }

    /// <summary>"Stop at" on the Lumberjack's Hut (0.22): with this much Wood in the colony's chests he rests.</summary>
    public static class WoodLimit
    {
        /// <summary>0 is no limit (the start).</summary>
        public static readonly int[] Options = { 0, 200, 500, 1000 };

        public static int Next(int current)
        {
            int index = Array.IndexOf(Options, current);
            return index < 0 ? Options[0] : Options[(index + 1) % Options.Length];
        }

        public static bool Enough(int limit, int woodInChests) => limit > 0 && woodInChests >= limit;
    }
}
