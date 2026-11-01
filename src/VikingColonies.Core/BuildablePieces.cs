using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>
    /// What a builder may put up from a design or a saved plan: only a piece on one of the players' build tools' lists (the
    /// Hammer, the Hoe, the Cultivator, another mod's tool). A design file, or a plan another computer saved on a hut, can
    /// name any of the game's things, and one that isn't a piece costs nothing: the security sweep of 27 Oct 2026 found a
    /// builder would put up a boss, an item or a loot chest from a design, for any player on a server.
    /// </summary>
    public sealed class BuildablePieces
    {
        private readonly HashSet<string> _names = new HashSet<string>(StringComparer.Ordinal);

        /// <param name="toolLists">Each build tool's pieces, by prefab name.</param>
        public BuildablePieces(IEnumerable<IEnumerable<string>> toolLists)
        {
            if (toolLists == null) return;
            foreach (IEnumerable<string> list in toolLists)
            {
                if (list == null) continue;
                foreach (string name in list)
                    if (!string.IsNullOrEmpty(name)) _names.Add(name);
            }
        }

        /// <summary>How many different pieces the tools make.</summary>
        public int Count => _names.Count;

        /// <summary>The prefab is a piece some build tool makes (its name exactly: the game's names are case-sensitive).</summary>
        public bool Allows(string prefab) => !string.IsNullOrEmpty(prefab) && _names.Contains(prefab);
    }
}
