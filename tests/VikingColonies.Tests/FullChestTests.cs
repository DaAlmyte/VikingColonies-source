using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Worker polish part 2 (0.21): where a worker whose hut chest is full takes his load.</summary>
    public class FullChestTests
    {
        private static ChestRoom Chest(string id, float x, bool hall, bool room) =>
            new ChestRoom { BuildingId = id, At = new GroundPoint(x, 0f), IsTownHall = hall, HasRoom = room };

        [Fact]
        public void Town_hall_first()
        {
            Assert.Equal("Hall", FullChest.Choose(new List<ChestRoom> { Chest("Hut", 5f, false, true), Chest("Hall", 50f, true, true) },
                new GroundPoint(0f, 0f)));
        }

        [Fact]
        public void Nearest_with_room()
        {
            Assert.Equal("Near", FullChest.Choose(new List<ChestRoom>
            {
                Chest("Hall", 5f, true, false), Chest("Far", 30f, false, true), Chest("Near", 10f, false, true),
            }, new GroundPoint(0f, 0f)));
        }

        [Fact]   // the storehouse (1.5)
        public void A_full_worker_takes_the_load_to_the_storehouse_before_the_Town_Hall()
        {
            var chests = new[] {
                new ChestRoom { BuildingId = "hall", IsTownHall = true, HasRoom = true, At = new GroundPoint(1, 0) },
                new ChestRoom { BuildingId = "store", IsStorehouse = true, HasRoom = true, At = new GroundPoint(50, 0) } };
            Assert.Equal("store", FullChest.Choose(chests, new GroundPoint(0, 0)));
            chests[1].HasRoom = false;
            Assert.Equal("hall", FullChest.Choose(chests, new GroundPoint(0, 0)));
        }

        [Fact]
        public void Every_chest_full_waits()
        {
            Assert.Null(FullChest.Choose(new List<ChestRoom> { Chest("Hall", 5f, true, false), Chest("Hut", 10f, false, false) },
                new GroundPoint(0f, 0f)));
        }
    }
}
