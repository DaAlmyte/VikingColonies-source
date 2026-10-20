using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    public enum ChopKind { None, Tree, Log, Stump }

    public sealed class ReplantInfo
    {
        public ReplantInfo(string sapling, string seed)
        {
            Sapling = sapling;
            Seed = seed;
        }

        public string Sapling { get; }
        public string Seed { get; }
    }

    /// <summary>What a lumberjack can chop, what it picks up, and what to replant — keyed by prefab name.</summary>
    /// <summary>A sapling the lumberjack can plant: its seed, its name token, the axe tier its grown tree needs, where it grows.</summary>
    public sealed class SaplingInfo
    {
        public SaplingInfo(string sapling, string seed, string name, int treeTier, int biomes)
        {
            Sapling = sapling;
            Seed = seed;
            Name = name;
            TreeTier = treeTier;
            Biomes = biomes;
        }

        public string Sapling { get; }
        public string Seed { get; }
        public string Name { get; }
        public int TreeTier { get; }
        /// <summary>The game's biome flags it grows in; -1 for everywhere.</summary>
        public int Biomes { get; }
    }

    public sealed class TreeCatalog
    {
        private readonly Dictionary<string, ChopKind> _kinds = new Dictionary<string, ChopKind>();
        private readonly Dictionary<string, int> _tiers = new Dictionary<string, int>();
        private readonly HashSet<string> _treesWithStump = new HashSet<string>();
        private readonly Dictionary<string, List<string>> _treesOfStump = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, ReplantInfo> _replantOfTree = new Dictionary<string, ReplantInfo>();
        private readonly Dictionary<string, List<string>> _saplingsOfTree = new Dictionary<string, List<string>>();
        private readonly HashSet<string> _collectibles = new HashSet<string>();
        private readonly HashSet<string> _saplings = new HashSet<string>();
        private readonly List<SaplingInfo> _saplingInfos = new List<SaplingInfo>();

        public int TreeCount => _kinds.Values.Count(k => k == ChopKind.Tree);
        public int SaplingCount => _saplings.Count;
        public int CollectibleCount => _collectibles.Count;
        public IReadOnlyList<SaplingInfo> Saplings => _saplingInfos;

        /// <summary>Every standing tree prefab and the axe tier it needs (the Trees list, 0.22).</summary>
        public IReadOnlyDictionary<string, int> Trees =>
            _kinds.Where(k => k.Value == ChopKind.Tree).ToDictionary(k => k.Key, k => _tiers[k.Key]);

        public void AddTree(string tree, int minToolTier, string stump)
        {
            _kinds[tree] = ChopKind.Tree;
            _tiers[tree] = minToolTier;
            if (string.IsNullOrEmpty(stump)) return;
            _treesWithStump.Add(tree);
            if (!_treesOfStump.TryGetValue(stump, out List<string> trees))
            {
                trees = new List<string>();
                _treesOfStump[stump] = trees;
            }
            if (!trees.Contains(tree)) trees.Add(tree);
        }

        public void AddLog(string log, int minToolTier) => AddIfNew(log, ChopKind.Log, minToolTier);

        public void AddStump(string stump, int minToolTier) => AddIfNew(stump, ChopKind.Stump, minToolTier);

        public void AddSapling(string sapling, string seed, IEnumerable<string> growsInto, int biomes = -1, string name = null)
        {
            List<string> trees = growsInto.ToList();
            if (_saplings.Add(sapling))
            {
                int tier = trees.Where(t => _tiers.ContainsKey(t)).Select(t => _tiers[t]).DefaultIfEmpty(0).Max();
                _saplingInfos.Add(new SaplingInfo(sapling, seed, name ?? sapling, tier, biomes));
            }
            AddCollectible(seed);
            foreach (string tree in trees)
            {
                if (!_replantOfTree.ContainsKey(tree)) _replantOfTree[tree] = new ReplantInfo(sapling, seed);
                if (!_saplingsOfTree.TryGetValue(tree, out List<string> saplings)) _saplingsOfTree[tree] = saplings = new List<string>();
                if (!saplings.Contains(sapling)) saplings.Add(sapling);
            }
        }

        public void AddCollectible(string item)
        {
            if (!string.IsNullOrEmpty(item)) _collectibles.Add(item);
        }

        public ChopKind KindOf(string prefab) =>
            prefab != null && _kinds.TryGetValue(prefab, out ChopKind kind) ? kind : ChopKind.None;

        public bool CanChop(string prefab, int toolTier) =>
            KindOf(prefab) != ChopKind.None && _tiers[prefab] <= toolTier;

        public bool HasStump(string tree) => tree != null && _treesWithStump.Contains(tree);

        public bool IsCollectible(string item) => item != null && _collectibles.Contains(item);

        public bool TryGetReplantForTree(string tree, out ReplantInfo info)
        {
            info = null;
            return tree != null && _replantOfTree.TryGetValue(tree, out info);
        }

        /// <summary>Every sapling that grows into this tree (the Trees list, 0.22); empty for none.</summary>
        public IEnumerable<string> SaplingsOfTree(string tree) =>
            tree != null && _saplingsOfTree.TryGetValue(tree, out List<string> saplings) ? saplings : (IEnumerable<string>)new string[0];

        public bool TryGetReplantForStump(string stump, out ReplantInfo info)
        {
            info = null;
            if (stump == null || !_treesOfStump.TryGetValue(stump, out List<string> trees)) return false;
            foreach (string tree in trees)
                if (TryGetReplantForTree(tree, out info)) return true;
            return false;
        }

        private void AddIfNew(string prefab, ChopKind kind, int minToolTier)
        {
            if (string.IsNullOrEmpty(prefab) || _kinds.ContainsKey(prefab)) return;
            _kinds[prefab] = kind;
            _tiers[prefab] = minToolTier;
        }
    }
}
