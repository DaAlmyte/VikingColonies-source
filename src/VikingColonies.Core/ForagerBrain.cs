using System;

namespace VikingColonies.Core
{
    public enum ForagerAction { Unemployed, Rest, Deposit, WaitStorageFull, Gather, Pick, WaitNothingToPick }

    /// <summary>Everything the forager knows right now. The game side fills this in every tick.</summary>
    public struct ForagerSituation
    {
        public bool HasHut;
        public bool IsNight;
        public int CarryTotal;
        public int CarryCapacity;
        public bool StorageFull;
        /// <summary>Food he just picked lies on the ground by the bush.</summary>
        public bool DropsNearby;
        /// <summary>He picked a moment ago: the food is about to drop out of the bush.</summary>
        public bool JustPicked;
        public bool HasPickTarget;
    }

    /// <summary>
    /// Picks the forager's next action. Stateless, like the lumberjack's: the same situation always gives the same
    /// answer, so a reload or an interruption never leaves him half-way through a step.
    /// </summary>
    public static class ForagerBrain
    {
        public static ForagerAction Decide(ForagerSituation s)
        {
            int load = Math.Max(0, s.CarryTotal);
            if (!s.HasHut) return ForagerAction.Unemployed;
            if (s.StorageFull && load > 0) return ForagerAction.WaitStorageFull;
            if (s.IsNight) return load > 0 ? ForagerAction.Deposit : ForagerAction.Rest;
            if (load >= Math.Max(1, s.CarryCapacity)) return ForagerAction.Deposit;
            if (s.DropsNearby || s.JustPicked) return ForagerAction.Gather;
            if (s.HasPickTarget) return ForagerAction.Pick;
            return load > 0 ? ForagerAction.Deposit : ForagerAction.WaitNothingToPick;
        }
    }

    /// <summary>What the forager picks: wild kinds on his hut's list that are ready (see the forager design).</summary>
    public static class Foraging
    {
        /// <summary>How close he comes to a bush to pick it.</summary>
        public const float Reach = 1.5f;
        /// <summary>What he picked drops next to the bush: he gathers food lying this close to where he picked.</summary>
        public const float GatherRadius = 3f;
        /// <summary>He gathers what dropped for this long after picking; after that, food on the ground is someone else's.</summary>
        public const float GatherSeconds = 20f;
        /// <summary>After picking he waits up to this long for the food to drop (from another computer it comes later).</summary>
        public const float DropWaitSeconds = 5f;

        /// <summary>
        /// Wild (it grows back, as a planted crop doesn't, and no player planted it: placed by a player, or on cultivated
        /// ground), wanted (its kind is on his hut's list and ticked: <see cref="ForageChoice.Wants"/>) and ready (not picked).
        /// </summary>
        public static bool IsHis(bool growsBack, bool wanted, bool ready, bool planted) =>
            growsBack && !planted && wanted && ready;
    }
}
