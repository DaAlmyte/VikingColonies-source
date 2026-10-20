using System;

namespace VikingColonies.Core
{
    /// <summary>
    /// A settler's skill works like the player's: practice builds up, each level needs the game's own amount of it,
    /// and damage grows from a new character's 25-55 % of the weapon's damage to a master's 85-100 %.
    /// </summary>
    public static class SettlerSkill
    {
        public const float MaxLevel = 100f;
        /// <summary>Practice a player gets from one axe hit on a tree.</summary>
        public const float XpPerHit = 1f;

        /// <summary>
        /// Practice a settler gets per hit. Settlers chop all day, so they learn at a fraction of the player's rate
        /// (the Lumberjack.LearningRate setting; 1 = as fast as a player per hit).
        /// </summary>
        public static float PracticePerHit(float learningRate) => XpPerHit * Math.Max(0f, learningRate);

        public static float DamageFactor(float level, float random01)
        {
            float mid = 0.4f + 0.6f * Clamp01(level / MaxLevel);
            float low = Clamp01(mid - 0.15f), high = Clamp01(mid + 0.15f);
            return low + (high - low) * Clamp01(random01);
        }

        /// <summary>How much faster a skilled worker works (0.22): 1 at 0, twice as fast at 100.</summary>
        public static float SpeedFactor(float level) => 1f + Clamp01(level / MaxLevel);

        public static float NextLevelRequirement(float level) => (float)(Math.Pow(Math.Floor(level + 1f), 1.5) * 0.5 + 0.5);

        /// <summary>Adds practice; reaching the requirement raises the level by one and starts again. Returns true on a level-up.</summary>
        public static bool Raise(ref float level, ref float progress, float amount)
        {
            if (level >= MaxLevel) return false;
            progress += amount;
            if (progress < NextLevelRequirement(level)) return false;
            level = Math.Min(MaxLevel, level + 1f);
            progress = 0f;
            return true;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
