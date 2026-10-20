using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class LumberjackBrainTests
    {
        [Fact]
        public void The_trees_he_can_fell_go_by_the_axe_he_has_not_what_is_in_his_hand()
        {
            // The whole-mod review of 1.4.1: seated in his free time the axe is put away, and his bare hands found no birch.
            Assert.Equal(2, LumberjackBrain.AxeTier(borrowedTier: 2, carriedTier: 1));   // the colony's better axe
            Assert.Equal(1, LumberjackBrain.AxeTier(borrowedTier: null, carriedTier: 1));
            Assert.Equal(0, LumberjackBrain.AxeTier(borrowedTier: null, carriedTier: null));
        }

        private static LumberjackSituation Working() =>
            new LumberjackSituation { HasHall = true, CarryCapacity = 50, HasChopTarget = true };

        private static LumberjackAction Decide(LumberjackSituation s) => LumberjackBrain.Decide(s);

        [Fact]
        public void Without_a_hall_the_lumberjack_is_unemployed()
        {
            var s = Working(); s.HasHall = false; s.CarryTotal = 10;
            Assert.Equal(LumberjackAction.Unemployed, Decide(s));
        }

        [Fact]
        public void With_a_tree_in_range_it_chops() => Assert.Equal(LumberjackAction.Chop, Decide(Working()));

        [Fact]
        public void A_full_load_is_delivered_before_anything_else()
        {
            var s = Working(); s.CarryTotal = 50; s.DropsNearby = true;
            Assert.Equal(LumberjackAction.Deposit, Decide(s));
        }

        [Fact]
        public void Drops_are_collected_before_chopping_more()
        {
            var s = Working(); s.DropsNearby = true;
            Assert.Equal(LumberjackAction.Collect, Decide(s));
        }

        [Fact]
        public void Planting_happens_when_a_spot_and_a_seed_are_ready()
        {
            var s = Working(); s.PlantSpotPending = true; s.HasSeedForPlantSpot = true;
            Assert.Equal(LumberjackAction.Plant, Decide(s));
        }

        [Fact]
        public void Without_replanting_a_carried_seed_does_not_send_him_planting()
        {
            var s = Working(); s.PlantSpotPending = true; s.HasSeedForPlantSpot = true; s.ReplantBlocked = true;
            Assert.Equal(LumberjackAction.Chop, Decide(s));
        }

        [Fact]
        public void Without_replanting_he_does_not_fetch_seeds_from_storage()
        {
            var s = Working(); s.HasChopTarget = false; s.PlantSpotPending = true; s.SeedInStorage = true; s.ReplantBlocked = true;
            Assert.Equal(LumberjackAction.WaitNoTrees, Decide(s));
        }

        [Fact]
        public void A_pending_spot_without_a_seed_does_not_block_chopping()
        {
            var s = Working(); s.PlantSpotPending = true;
            Assert.Equal(LumberjackAction.Chop, Decide(s));
        }

        [Fact]
        public void No_trees_but_carrying_goes_to_deliver()
        {
            var s = Working(); s.HasChopTarget = false; s.CarryTotal = 3;
            Assert.Equal(LumberjackAction.Deposit, Decide(s));
        }

        [Fact]
        public void No_trees_with_a_pending_spot_and_a_seed_in_storage_goes_to_fetch_it()
        {
            var s = Working(); s.HasChopTarget = false; s.PlantSpotPending = true; s.SeedInStorage = true;
            Assert.Equal(LumberjackAction.Deposit, Decide(s));
        }

        [Fact]
        public void Nothing_to_do_waits_for_trees()
        {
            var s = Working(); s.HasChopTarget = false;
            Assert.Equal(LumberjackAction.WaitNoTrees, Decide(s));
        }

        // Review focus 1
        [Fact]
        public void Storage_full_while_carrying_waits_at_the_hall_even_at_night()
        {
            var s = Working(); s.StorageFull = true; s.CarryTotal = 20; s.IsNight = true;
            Assert.Equal(LumberjackAction.WaitStorageFull, Decide(s));
        }

        [Fact]
        public void Storage_full_is_ignored_once_nothing_is_carried()
        {
            var s = Working(); s.StorageFull = true;
            Assert.Equal(LumberjackAction.Chop, Decide(s));
        }

        // Review focus 2
        [Fact]
        public void At_night_a_carried_load_is_delivered_first()
        {
            var s = Working(); s.IsNight = true; s.CarryTotal = 7;
            Assert.Equal(LumberjackAction.Deposit, Decide(s));
        }

        [Fact]
        public void At_night_with_empty_hands_it_rests()
        {
            var s = Working(); s.IsNight = true; s.DropsNearby = true;
            Assert.Equal(LumberjackAction.Rest, Decide(s));
        }

        // Review C2: seeds held for replanting must not count as a load, or deposit/restock loops forever.
        [Fact]
        public void At_night_seeds_held_for_replanting_do_not_force_a_delivery()
        {
            var s = Working(); s.IsNight = true; s.CarryTotal = 1; s.ReservedSeeds = 1;
            s.PlantSpotPending = true; s.HasSeedForPlantSpot = true;
            Assert.Equal(LumberjackAction.Rest, Decide(s));
        }

        // Review C2
        [Fact]
        public void Capacity_one_with_a_held_seed_still_plants()
        {
            var s = Working(); s.CarryCapacity = 1; s.CarryTotal = 1; s.ReservedSeeds = 1;
            s.PlantSpotPending = true; s.HasSeedForPlantSpot = true;
            Assert.Equal(LumberjackAction.Plant, Decide(s));
        }

        // Review C2
        [Fact]
        public void Storage_full_with_only_held_seeds_keeps_working()
        {
            var s = Working(); s.StorageFull = true; s.CarryTotal = 1; s.ReservedSeeds = 1;
            Assert.Equal(LumberjackAction.Chop, Decide(s));
        }

        [Fact]
        public void Wood_carried_alongside_held_seeds_is_still_delivered_at_night()
        {
            var s = Working(); s.IsNight = true; s.CarryTotal = 6; s.ReservedSeeds = 1;
            Assert.Equal(LumberjackAction.Deposit, Decide(s));
        }

        // Review focus 3
        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void Capacity_below_one_is_treated_as_one(int capacity)
        {
            var s = Working(); s.CarryCapacity = capacity;
            Assert.Equal(LumberjackAction.Chop, Decide(s));
            s.CarryTotal = 1;
            Assert.Equal(LumberjackAction.Deposit, Decide(s));
        }
    
        [Fact]   // 0.22: Stop at — with enough wood in the colony's chests he rests
        public void Enough_wood_rests()
        {
            var s = Working(); s.EnoughWood = true;
            Assert.Equal(LumberjackAction.WaitEnoughWood, Decide(s));
        }

        [Fact]
        public void Enough_wood_still_delivers_and_plants()
        {
            var s = Working(); s.EnoughWood = true; s.CarryTotal = 10;
            Assert.Equal(LumberjackAction.Deposit, Decide(s));
            var p = Working(); p.EnoughWood = true; p.PlantSpotPending = true; p.HasSeedForPlantSpot = true; p.CarryTotal = 1; p.ReservedSeeds = 1;
            Assert.Equal(LumberjackAction.Plant, Decide(p));
            var n = Working(); n.EnoughWood = true; n.IsNight = true;
            Assert.Equal(LumberjackAction.Rest, Decide(n));
        }

        [Fact]   // 0.26 (review of 0.22): his last tree's wood doesn't lie there until he works again
        public void Enough_wood_collects_his_last_trees_wood_first()
        {
            var s = Working(); s.EnoughWood = true; s.DropsNearby = true; s.DropIsLeftover = true;
            Assert.Equal(LumberjackAction.Collect, Decide(s));
            s.CarryTotal = 5;
            Assert.Equal(LumberjackAction.Collect, Decide(s));
            var full = Working(); full.EnoughWood = true; full.DropsNearby = true; full.DropIsLeftover = true; full.CarryTotal = full.CarryCapacity;
            Assert.Equal(LumberjackAction.Deposit, Decide(full));
            var noTree = Working(); noTree.EnoughWood = true; noTree.DropsNearby = false;
            Assert.Equal(LumberjackAction.WaitEnoughWood, Decide(noTree));
        }

        [Fact]   // review of 0.26: any wood lying near him isn't his to fetch while he rests
        public void Enough_wood_leaves_other_wood_lying()
        {
            var s = Working(); s.EnoughWood = true; s.DropsNearby = true; s.DropIsLeftover = false;
            Assert.Equal(LumberjackAction.WaitEnoughWood, Decide(s));
        }

        [Fact]   // review of 0.26: the logs and stump of the tree he felled are finished; no new tree is felled
        public void Enough_wood_finishes_his_last_trees_logs_and_stump()
        {
            var s = Working(); s.EnoughWood = true; s.ChopTargetIsLeftover = true;
            Assert.Equal(LumberjackAction.Chop, Decide(s));
            var tree = Working(); tree.EnoughWood = true; tree.ChopTargetIsLeftover = false;   // a standing tree
            Assert.Equal(LumberjackAction.WaitEnoughWood, Decide(tree));
            var both = Working(); both.EnoughWood = true; both.ChopTargetIsLeftover = true; both.DropsNearby = true; both.DropIsLeftover = true;
            Assert.Equal(LumberjackAction.Collect, Decide(both));   // what lies first, as at work
        }
}
}
