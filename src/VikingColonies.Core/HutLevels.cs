using System;

namespace VikingColonies.Core
{
    /// <summary>Why a hut can't go up a level now (None: it can).</summary>
    public enum UpgradeLock { None, MaxLevel, NotFinished, Upgrading, NoBuilderHut, BuilderTooLow, TakingDown, TownHallTooLow }

    /// <summary>
    /// Hut levels 1–5 and who may build which level: a builder builds and upgrades huts up to his own Builder's Hut's
    /// level, and his own hut one level higher, so the Builder's Hut is upgraded first.
    /// </summary>
    public static class HutLevels
    {
        public const int MaxLevel = 5;

        public static int Clamp(int level) => Math.Max(1, Math.Min(MaxLevel, level));

        // The best vanilla tool of each level's band (m_toolTier): flint, iron, black metal, Jotun Bane, Berserkir axes;
        // antler, iron, black metal pickaxes (no newer vanilla pickaxe). Modded tools fit in by their tier.
        private static readonly int[] s_axeTier = { 1, 3, 4, 4, 6 };
        private static readonly int[] s_pickaxeTier = { 0, 2, 3, 3, 3 };

        /// <summary>The highest tool tier a hut of this level lets its worker use; hoes have no cap.</summary>
        public static int ToolTierCap(ToolKind kind, int level)
        {
            int i = Clamp(level) - 1;
            switch (kind)
            {
                case ToolKind.Axe: return s_axeTier[i];
                case ToolKind.Pickaxe: return s_pickaxeTier[i];
                default: return int.MaxValue;
            }
        }

        /// <summary>The lowest level whose cap reaches <paramref name="tier"/>; 0 when no level does.</summary>
        public static int LevelForTool(ToolKind kind, int tier)
        {
            for (int level = 1; level <= MaxLevel; level++)
                if (ToolTierCap(kind, level) >= tier) return level;
            return 0;
        }

        /// <summary>The lumberjack's work area: the base radius plus <paramref name="perLevel"/> per level above 1.</summary>
        public static float WorkRadius(float baseRadius, float perLevel, int level) => baseRadius + perLevel * (Clamp(level) - 1);

        /// <summary>A hut chest's rows: the prefab's rows plus <paramref name="perLevel"/> per level above 1 (never fewer).</summary>
        public static int ChestRows(int baseRows, int perLevel, int level) => baseRows + Math.Max(0, perLevel) * (Clamp(level) - 1);

        /// <summary>A builder whose hut is <paramref name="builderHutLevel"/> may build a hut to <paramref name="targetLevel"/>.</summary>
        /// <param name="ownHut">The hut is his own Builder's Hut.</param>
        public static bool MayBuild(int builderHutLevel, int targetLevel, bool ownHut) =>
            targetLevel <= Clamp(builderHutLevel) + (ownHut ? 1 : 0);

        /// <summary>
        /// Whether a hut at <paramref name="level"/> may be upgraded now: below the top level, no upgrade under way, its
        /// building finished, and a Builder's Hut high enough (<paramref name="highestBuilderHutLevel"/>, 0 for none). A
        /// Builder's Hut may always go one level above itself.
        /// </summary>
        /// <param name="takingDown">It is being taken down (0.23): no upgrade (review of 0.23: Cancel upgrade then left the builder stuck).</param>
        /// <param name="hutCap">The highest level the colony's Town Hall lets huts reach (1.4, <see cref="TownHallLevels"/>).</param>
        public static UpgradeLock CanUpgrade(int level, bool finished, bool upgrading, int highestBuilderHutLevel, bool isBuilderHut, bool takingDown = false,
            int hutCap = MaxLevel)
        {
            if (takingDown) return UpgradeLock.TakingDown;
            if (level >= MaxLevel) return UpgradeLock.MaxLevel;
            if (upgrading) return UpgradeLock.Upgrading;
            if (!finished) return UpgradeLock.NotFinished;
            if (level + 1 > hutCap) return UpgradeLock.TownHallTooLow;
            if (highestBuilderHutLevel <= 0 && !isBuilderHut) return UpgradeLock.NoBuilderHut;
            int allowed = isBuilderHut ? Math.Max(highestBuilderHutLevel, level + 1) : highestBuilderHutLevel;
            return level + 1 <= allowed ? UpgradeLock.None : UpgradeLock.BuilderTooLow;
        }
    }
}
