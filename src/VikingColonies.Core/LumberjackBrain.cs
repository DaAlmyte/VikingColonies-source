using System;

namespace VikingColonies.Core
{
    public enum LumberjackAction { Unemployed, Rest, Deposit, WaitStorageFull, Collect, Plant, Chop, WaitNoTrees, WaitEnoughWood }

    /// <summary>Everything the lumberjack knows right now. The game side fills this in every tick.</summary>
    public struct LumberjackSituation
    {
        public bool HasHall;
        public bool IsNight;
        public int CarryTotal;
        /// <summary>Seeds in CarryTotal that are held back for pending plant spots (not a load to deliver).</summary>
        public int ReservedSeeds;
        public int CarryCapacity;
        public bool StorageFull;
        public bool DropsNearby;
        /// <summary>The drop he'd pick up is of the tree he felled last (review of 0.26: only that is his to finish at Stop at).</summary>
        public bool DropIsLeftover;
        public bool PlantSpotPending;
        public bool HasSeedForPlantSpot;
        public bool SeedInStorage;
        public bool HasChopTarget;
        /// <summary>What he'd chop is a log or stump of the tree he felled last, not a standing tree (review of 0.26).</summary>
        public bool ChopTargetIsLeftover;
        /// <summary>Replanting isn't allowed right now (setting off or no Cultivator): plant spots are ignored.</summary>
        public bool ReplantBlocked;
        /// <summary>The colony's chests hold the hut's "Stop at" amount of wood (0.22).</summary>
        public bool EnoughWood;
    }

    /// <summary>
    /// Picks the lumberjack's next action. Stateless: the same situation always gives the same answer,
    /// so a reload or an interruption can never leave the lumberjack stuck half-way through a step.
    /// </summary>
    public static class LumberjackBrain
    {
        /// <summary>
        /// The tier of the axe he works with: the colony's better one he borrowed, else his own; never what is in his hand
        /// (the whole-mod review of 1.4.1: seated, his axe put away, his bare hands felled nothing above tier 0).
        /// </summary>
        public static int AxeTier(int? borrowedTier, int? carriedTier) => borrowedTier ?? carriedTier ?? 0;

        public static LumberjackAction Decide(LumberjackSituation s)
        {
            // Seeds held back for pending plant spots are not a load to deliver.
            int load = Math.Max(0, s.CarryTotal - s.ReservedSeeds);
            if (!s.HasHall) return LumberjackAction.Unemployed;
            if (s.StorageFull && load > 0) return LumberjackAction.WaitStorageFull;
            if (s.IsNight) return load > 0 ? LumberjackAction.Deposit : LumberjackAction.Rest;
            if (load >= Math.Max(1, s.CarryCapacity)) return LumberjackAction.Deposit;
            bool planting = s.PlantSpotPending && !s.ReplantBlocked;
            if (s.EnoughWood)   // Stop at (0.22): he still plants, finishes the tree he felled and delivers, then rests
            {
                if (planting && s.HasSeedForPlantSpot) return LumberjackAction.Plant;
                // 0.26: his last tree's wood, logs and stump don't lie there till he works again; no new tree is felled.
                if (s.DropsNearby && s.DropIsLeftover) return LumberjackAction.Collect;
                if (s.HasChopTarget && s.ChopTargetIsLeftover) return LumberjackAction.Chop;
                return load > 0 ? LumberjackAction.Deposit : LumberjackAction.WaitEnoughWood;
            }
            if (s.DropsNearby) return LumberjackAction.Collect;
            if (planting && s.HasSeedForPlantSpot) return LumberjackAction.Plant;
            if (s.HasChopTarget) return LumberjackAction.Chop;
            if (load > 0) return LumberjackAction.Deposit;
            if (planting && s.SeedInStorage) return LumberjackAction.Deposit;
            return LumberjackAction.WaitNoTrees;
        }
    }
}
