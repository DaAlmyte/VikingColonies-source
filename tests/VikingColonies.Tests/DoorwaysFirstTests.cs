using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The old-huts upgrade test (2026-10-19): an old door stayed up till the end with the new design's wall right behind
    /// it ("couldn't step through", 129 times), and in another hut the builder fetched from its chest while new walls and
    /// old ones closed round him before the new door's spot was cleared. In an upgrade, the doorways come first: old doors
    /// and gates, and whatever old piece stands where a new door goes, come down before anything new goes up.
    /// </summary>
    public class DoorwaysFirstTests
    {
        private static PieceBox Box(float x0, float y0, float z0, float x1, float y1, float z1) => new PieceBox(new Vec3(x0, y0, z0), new Vec3(x1, y1, z1));
        private static KeyValuePair<int, PieceBox> Old(int key, PieceBox box) => new KeyValuePair<int, PieceBox>(key, box);
        private static bool Never(PieceBox box) => false;

        private static readonly PieceBox NewDoor = Box(-1f, 0f, 2.9f, 1f, 2f, 3.1f);
        private static readonly PieceBox OldWallThere = Box(-1f, 0f, 2.85f, 1f, 2f, 3.15f);
        private static readonly PieceBox OldFloorUnder = Box(-1f, -0.2f, 2f, 1f, 0.05f, 4f);
        private static readonly PieceBox OldWallAside = Box(1f, 0f, 2.85f, 3f, 2f, 3.15f);
        private static readonly PieceBox OldBeamOnTop = Box(-1f, 2f, 2.8f, 1f, 2.3f, 3.2f);

        [Fact]
        public void An_old_wall_where_a_new_door_goes_is_in_its_doorway_a_floor_under_it_or_the_next_wall_isnt()
        {
            var doors = new List<PieceBox> { NewDoor };
            Assert.True(RemovalOrder.InDoorway(OldWallThere, doors));
            Assert.False(RemovalOrder.InDoorway(OldFloorUnder, doors));
            Assert.False(RemovalOrder.InDoorway(OldWallAside, doors));
            Assert.False(RemovalOrder.InDoorway(OldWallThere, new List<PieceBox>()));
        }

        [Fact]
        public void The_doorway_comes_down_first_and_what_rests_on_it_before_it()
        {
            var old = new List<KeyValuePair<int, PieceBox>>
            {
                Old(1, OldWallAside), Old(2, OldWallThere), Old(3, OldBeamOnTop), Old(4, OldFloorUnder),
            };
            var doorway = new HashSet<int> { 2 };
            Assert.Equal(3, RemovalOrder.NextDoorway(old, doorway, new List<PieceBox>(), Never));   // the beam on it first
            old.RemoveAt(2);
            Assert.Equal(2, RemovalOrder.NextDoorway(old, doorway, new List<PieceBox>(), Never));
            old.RemoveAt(1);
            Assert.Equal(-1, RemovalOrder.NextDoorway(old, doorway, new List<PieceBox>(), Never));  // the rest waits its turn
        }

        [Fact]
        public void A_doorway_piece_holding_up_a_kept_piece_waits_like_any_old_piece()
        {
            var old = new List<KeyValuePair<int, PieceBox>> { Old(2, OldWallThere) };
            Assert.Equal(-1, RemovalOrder.NextDoorway(old, new HashSet<int> { 2 }, new List<PieceBox> { OldBeamOnTop }, Never));
        }
    }
}
