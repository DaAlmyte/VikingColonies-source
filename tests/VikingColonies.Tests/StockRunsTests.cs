using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Worker polish part 2 (0.21): which carry run a settler in free time makes for a building short of a kept item.</summary>
    public class StockRunsTests
    {
        private static bool IsFood(string item) => item == "Raspberry";
        private static bool Nobody(string key) => false;

        private static StockNeed Need(string id, float x, string item, int missing) =>
            new StockNeed { BuildingId = id, At = new GroundPoint(x, 0f), Item = item, Missing = missing };

        private static StockSource Source(string id, float x, string item, int spare) =>
            new StockSource { BuildingId = id, At = new GroundPoint(x, 0f), Item = item, Spare = spare };

        [Fact]
        public void Nearest_need_from_nearest_spare()
        {
            var needs = new List<StockNeed> { Need("B", 50f, "Wood", 5), Need("A", 10f, "Wood", 30) };
            var sources = new List<StockSource> { Source("S2", 40f, "Wood", 50), Source("S1", 12f, "Wood", 8) };
            StockRun run = StockRuns.Choose(needs, sources, new GroundPoint(0f, 0f), Nobody, IsFood);
            Assert.Equal("A", run.To);
            Assert.Equal("S1", run.From);
            Assert.Equal("Wood", run.Item);
            Assert.Equal(8, run.Amount);
        }

        [Fact]
        public void At_most_twenty()
        {
            StockRun run = StockRuns.Choose(new List<StockNeed> { Need("A", 10f, "Wood", 50) },
                new List<StockSource> { Source("S", 20f, "Wood", 100) }, new GroundPoint(0f, 0f), Nobody, IsFood);
            Assert.Equal(StockRuns.MaxCarry, run.Amount);
            Assert.Equal(20, StockRuns.MaxCarry);
        }

        [Fact]
        public void One_settler_per_building_and_item()
        {
            var needs = new List<StockNeed> { Need("A", 10f, "Wood", 30), Need("B", 50f, "Wood", 5) };
            var sources = new List<StockSource> { Source("S", 20f, "Wood", 50) };
            string taken = StockRuns.ClaimKey("A", "Wood");
            StockRun run = StockRuns.Choose(needs, sources, new GroundPoint(0f, 0f), key => key == taken, IsFood);
            Assert.Equal("B", run.To);
        }

        [Fact]
        public void Never_from_itself()
        {
            Assert.Null(StockRuns.Choose(new List<StockNeed> { Need("A", 10f, "Wood", 30) },
                new List<StockSource> { Source("A", 10f, "Wood", 50) }, new GroundPoint(0f, 0f), Nobody, IsFood));
        }

        [Fact]
        public void Food_of_any_kind_comes_from_a_food_source()
        {
            StockRun run = StockRuns.Choose(new List<StockNeed> { Need("Hall", 0f, StockList.AnyFood, 10) },
                new List<StockSource> { Source("Hut", 30f, "Stone", 40), Source("Hut", 30f, "Raspberry", 6) },
                new GroundPoint(0f, 0f), Nobody, IsFood);
            Assert.Equal("Raspberry", run.Item);
            Assert.Equal(6, run.Amount);
            Assert.Equal(StockList.AnyFood, run.Need);
        }
    
        [Fact]   // review of 0.21 (I2): a building with no room for it isn't carried to (it would get nothing, again and again)
        public void Never_to_a_chest_without_room()
        {
            var needs = new List<StockNeed> { Need("Full", 5f, "Wood", 10), Need("Roomy", 30f, "Wood", 10) };
            var sources = new List<StockSource> { Source("S", 10f, "Wood", 50) };
            StockRun run = StockRuns.Choose(needs, sources, new GroundPoint(0f, 0f), Nobody, IsFood, (building, item) => building != "Full");
            Assert.Equal("Roomy", run.To);
            Assert.Null(StockRuns.Choose(needs.GetRange(0, 1), sources, new GroundPoint(0f, 0f), Nobody, IsFood, (building, item) => building != "Full"));
        }
}
}
