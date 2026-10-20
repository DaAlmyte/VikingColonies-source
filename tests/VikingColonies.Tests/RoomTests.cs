using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The old huts' upgrade test (2026-10-19): a builder fetching from the Builder's Hut's chest stood behind its back
    /// wall, in the woodpile strip that is part of the design's outline. The door rule took the whole outline for the hut,
    /// so he was "inside", was walked straight through the back wall, hopped onto the woodpile and onto the roof. Inside a
    /// hut is what its walls on the ground enclose.
    /// </summary>
    public class RoomTests
    {
        [Fact]
        public void A_room_is_what_its_walls_on_the_ground_enclose()
        {
            Assert.True(RoomRules.IsRoomWall("woodwall", 0f));
            Assert.True(RoomRules.IsRoomWall("stone_wall_2x1", 0f));
            Assert.True(RoomRules.IsRoomWall("Piece_grausten_wall_2x2", -0.1f));
            Assert.True(RoomRules.IsRoomWall("wood_wall_log", -0.1f));   // a log wall's bottom log sits in the ground
            Assert.False(RoomRules.IsRoomWall("wood_wall_roof_a", 0f));  // a gable
            Assert.False(RoomRules.IsRoomWall("woodwall", 2f));          // the wall of the storey above
            Assert.False(RoomRules.IsRoomWall("wood_floor", 0f));
            Assert.False(RoomRules.IsRoomWall("wood_fence", 0f));        // a yard's fence encloses no room
            Assert.False(RoomRules.IsRoomWall("", 0f));
        }

        [Fact]
        public void The_woodpile_strip_behind_the_back_wall_is_outside_the_room()
        {
            // The level-4 Builder's Hut: its walls on the ground run x -2..10, z -0.6..3.4; its woodpile lies behind, at z -1.95.
            var walls = new List<Vec3> { new Vec3(-2.1f, 0f, -0.7f), new Vec3(10.1f, 0f, -0.7f), new Vec3(-2.1f, 0f, 3.5f), new Vec3(10.1f, 0f, 3.5f) };
            Footprint room = Footprint.Around(walls, Vec3.Zero, Quat.Identity, 0f);
            Assert.True(room.Contains(new Vec3(0f, 0f, 1.4f)));
            Assert.False(room.Contains(new Vec3(-0.6f, 0f, -1.95f)));
        }
    }
}
