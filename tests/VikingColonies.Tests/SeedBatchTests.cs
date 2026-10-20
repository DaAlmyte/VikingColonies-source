using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// Worker polish (0.20): watching a colony, the farmer walked to the Farm's chest for every plot he planted. He now takes
    /// seeds for every plot waiting for that crop at once, and holds them apart from his harvest.
    /// </summary>
    public class SeedBatchTests
    {
        [Fact]
        public void With_no_seed_taken_he_looks_again_a_little_later_not_every_moment()
        {
            // The whole-mod review of 1.4.1: with a player at the Farm's chest he scanned every plot every frame.
            Assert.Equal(SeedBatch.RetrySeconds, SeedBatch.LookAgainIn(taken: 0));
            Assert.Equal(0f, SeedBatch.LookAgainIn(taken: 3));   // some, not enough: another crop at once
            Assert.True(SeedBatch.RetrySeconds >= 1f);
        }

        [Fact]
        public void He_takes_seeds_for_every_waiting_plot_at_once() => Assert.Equal(6, SeedBatch.ToTake(6, 1, 30, 0));

        [Fact]
        public void Never_more_than_twenty_in_hand()
        {
            Assert.Equal(20, SeedBatch.ToTake(40, 1, 100, 0));
            Assert.Equal(5, SeedBatch.ToTake(40, 1, 100, 15));
        }

        [Fact]
        public void A_bush_takes_whole_prices() => Assert.Equal(10, SeedBatch.ToTake(3, 5, 12, 0));

        [Fact]
        public void Not_more_than_the_chest_has() => Assert.Equal(2, SeedBatch.ToTake(6, 1, 2, 0));

        [Fact]
        public void Nothing_when_he_holds_enough_for_the_plots() => Assert.Equal(0, SeedBatch.ToTake(3, 1, 30, 3));

        [Fact]
        public void Seeds_in_hand_are_not_harvest()
        {
            var s = new FarmerSituation
            {
                HasHut = true, HasField = true, CarryTotal = 12, SeedsInHand = 8, CarryCapacity = 10, PlantablePlot = true, SeedToPlant = true, HasCultivator = true,
            };
            Assert.Equal(FarmerAction.Plant, FarmerBrain.Decide(s));   // a load of 4, not 12
        }

        [Fact]
        public void Seeds_with_nowhere_to_go_go_back_to_the_chest()
        {
            var s = new FarmerSituation { HasHut = true, HasField = true, CarryTotal = 8, SeedsInHand = 8, CarryCapacity = 20, HasCultivator = true };
            Assert.Equal(FarmerAction.Deposit, FarmerBrain.Decide(s));
        }
    }
}
