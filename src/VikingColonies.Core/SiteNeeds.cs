namespace VikingColonies.Core
{
    /// <summary>
    /// What a builder's site waits on: the first missing tool and the thing needing the highest Builder's Hut level are
    /// both named (tool first, so the same needs always read the same), and that level is kept so a better builder may
    /// take the site. Parts are joined with ", ": the game only sees where a $token ends at a space or punctuation.
    /// </summary>
    public sealed class SiteNeeds
    {
        private string _tool;
        private string _level;

        /// <summary>The highest Builder's Hut level something on the site needs; 0 when nothing needs one.</summary>
        public int Level { get; private set; }

        /// <summary>What to say, or null when nothing is missing.</summary>
        public string Text => _tool != null && _level != null ? _tool + ", " + _level : _tool ?? _level;

        /// <param name="text">How to name it, e.g. "an axe" or "Builder's Hut level 2 for a tree".</param>
        /// <param name="level">The Builder's Hut level it needs; 0 when it needs a tool instead.</param>
        public void Add(string text, int level)
        {
            if (level <= 0)
            {
                _tool = _tool ?? text;
                return;
            }
            if (level <= Level) return;
            Level = level;
            _level = text;
        }
    }
}
