using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The way round a hut to its door (1.5.1, the walk check's runs: the game's walking map took settlers 9-14 m out from a
    /// 4 by 6 m hut and back): close round its own corners, each leg straight.
    /// </summary>
    public class HutWayTests
    {
        // A hut's walls x -2..2, z -3..3; its door in the north wall (z 3), its spot in front at (0, 4.2).
        private static readonly Footprint Walls = new Footprint(new Vec3(0, 0, 0), Quat.Identity, 2f, 3f);
        private static readonly Vec3 Spot = new Vec3(0, 0, 4.2f);
        private const float Out = HutWay.Round;

        private static void Near(Vec3 expected, Vec3 actual)
        {
            Assert.Equal(expected.X, actual.X, 2);
            Assert.Equal(expected.Z, actual.Z, 2);
        }

        [Fact]
        public void With_the_walls_not_in_the_way_he_heads_for_the_spot()
        {
            Near(Spot, HutWay.Next(new Vec3(-10, 0, 15), Spot, Walls));
            Near(Spot, HutWay.Next(new Vec3(6, 0, 3.5f), Spot, Walls));   // beside the front corner, the spot in sight
        }

        [Fact]
        public void Close_behind_he_heads_for_the_back_corner_on_the_shorter_side()
        {
            // A little to the west behind the hut: round its west side, the south-west corner first, a body's width out.
            Near(new Vec3(-2 - Out, 0, -3 - Out), HutWay.Next(new Vec3(-1, 0, -6), Spot, Walls));
            Near(new Vec3(2 + Out, 0, -3 - Out), HutWay.Next(new Vec3(1, 0, -6), Spot, Walls));
        }

        [Fact]
        public void Far_behind_he_heads_straight_for_the_front_corner_past_the_back_one()
        {
            // From 20 m the front corner is in sight past the back one, clear of the wall: one straight leg to it.
            Near(new Vec3(-2 - Out, 0, 3 + Out), HutWay.Next(new Vec3(-1, 0, -20), Spot, Walls));
        }

        [Fact]
        public void Beside_the_hut_he_heads_for_the_front_corner_on_his_side()
        {
            Near(new Vec3(-2 - Out, 0, 3 + Out), HutWay.Next(new Vec3(-2 - Out, 0, -3 - Out), Spot, Walls));
            Near(new Vec3(2 + Out, 0, 3 + Out), HutWay.Next(new Vec3(8, 0, 0), Spot, Walls));
        }

        [Fact]
        public void Round_the_front_corner_the_spot_is_in_sight()
        {
            Near(Spot, HutWay.Next(new Vec3(-2 - Out, 0, 3 + Out), Spot, Walls));
        }

        [Fact]
        public void A_turned_hut_is_gone_round_as_it_stands()
        {
            // Turned a quarter: its walls x -3..3, z -2..2; its door in the east wall, the spot at (4.2, 0).
            var turned = new Footprint(new Vec3(0, 0, 0), Quat.Yaw(90f), 2f, 3f);
            var spot = new Vec3(4.2f, 0, 0);
            Near(new Vec3(-3 - Out, 0, 2 + Out), HutWay.Next(new Vec3(-6, 0, 1), spot, turned));
            Near(new Vec3(3 + Out, 0, 2 + Out), HutWay.Next(new Vec3(-3 - Out, 0, 2 + Out), spot, turned));
        }

        [Fact]
        public void Pressed_against_a_wall_he_still_goes_round_along_it()
        {
            // 0.2 m from the west wall, behind its middle: along the wall to its front corner.
            Near(new Vec3(-2 - Out, 0, 3 + Out), HutWay.Next(new Vec3(-2.2f, 0, 0), Spot, Walls));
        }

        [Fact]
        public void From_inside_the_walls_or_to_a_spot_inside_them_it_isnt_this_rules_way()
        {
            var inside = new Vec3(0, 0, 0);
            Near(inside, HutWay.Next(new Vec3(0, 0, -20), inside, Walls));
            Near(Spot, HutWay.Next(new Vec3(1, 0, 1), Spot, Walls));
        }

        [Fact]
        public void A_doors_walls_are_those_of_the_hut_it_stands_in()
        {
            // Two huts' outlines (the site's 1 m margin round their walls): the door in the second's north wall.
            var first = new Footprint(new Vec3(-20, 0, 0), Quat.Identity, 3f, 4f);
            var second = new Footprint(new Vec3(0, 0, 0), Quat.Identity, 3f, 4f);
            Footprint walls = HutWay.WallsOfDoor(new Vec3(0, 0, 2.9f), new[] { first, second }, 1f);
            Assert.NotNull(walls);
            Assert.Equal(0f, walls.Centre.X, 2);
            Assert.Equal(2f, walls.HalfX, 2);
            Assert.Equal(3f, walls.HalfZ, 2);
            Assert.Null(HutWay.WallsOfDoor(new Vec3(10, 0, 10), new[] { first, second }, 1f));   // a palisade gate: no hut's
        }

        [Fact]
        public void A_straight_leg_keeps_clear_of_a_fire()
        {
            // The review: a fire pit's stones are 0.2 m high, under the look along a leg; a Longhouse's pit is 0.8 m off its door's line.
            var pit = new[] { new FireRing(2.8f, 4.4f, 0.6f) };
            Assert.True(HutWay.PassesFire(new Vec3(4, 0, 1), new Vec3(2, 0, 6.2f), pit));      // by it within its reach and a body's width
            Assert.False(HutWay.PassesFire(new Vec3(-2, 0, 1), new Vec3(-1, 0, 6.2f), pit));   // well wide of it
            Assert.False(HutWay.PassesFire(new Vec3(4, 0, 1), new Vec3(2, 0, 6.2f), null));
        }
    }
}
