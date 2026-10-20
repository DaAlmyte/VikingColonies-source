using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class RemovalOrderTests
    {
        private static PieceBox Box(float x0, float y0, float z0, float x1, float y1, float z1) => new PieceBox(new Vec3(x0, y0, z0), new Vec3(x1, y1, z1));
        private static KeyValuePair<int, PieceBox> Old(int key, PieceBox box) => new KeyValuePair<int, PieceBox>(key, box);
        private static bool Never(PieceBox box) => false;

        private static readonly PieceBox WallLeft = Box(-3.15f, 0f, -1f, -2.85f, 2f, 1f);
        private static readonly PieceBox WallRight = Box(2.85f, 0f, -1f, 3.15f, 2f, 1f);
        private static readonly PieceBox Roof = Box(-3f, 2f, -1f, 3f, 2.13f, 1f);

        [Fact]
        public void Something_rests_on_a_piece_when_it_touches_its_top_and_overlaps_it()
        {
            Assert.True(RemovalOrder.RestsOn(Roof, WallLeft));
            Assert.False(RemovalOrder.RestsOn(WallLeft, Roof));
            Assert.False(RemovalOrder.RestsOn(Box(5f, 2f, 5f, 6f, 3f, 6f), WallLeft));
        }

        [Fact]
        public void A_roof_comes_down_before_the_walls_it_rests_on()
        {
            var old = new List<KeyValuePair<int, PieceBox>> { Old(1, WallLeft), Old(2, WallRight), Old(3, Roof) };
            Assert.Equal(3, RemovalOrder.Next(old, new List<PieceBox>(), Never, allBuilt: false));
        }

        [Fact]
        public void An_old_wall_holding_up_a_kept_roof_waits_until_a_staying_piece_holds_it_too()
        {
            var old = new List<KeyValuePair<int, PieceBox>> { Old(1, WallLeft) };
            Assert.Equal(-1, RemovalOrder.Next(old, new List<PieceBox> { Roof }, Never, allBuilt: false));
            Assert.Equal(1, RemovalOrder.Next(old, new List<PieceBox> { Roof, WallRight }, Never, allBuilt: false));
        }

        [Fact]
        public void Once_every_new_piece_stands_the_rest_comes_down_from_the_top_anyway()
        {
            var old = new List<KeyValuePair<int, PieceBox>> { Old(1, WallLeft) };
            Assert.Equal(1, RemovalOrder.Next(old, new List<PieceBox> { Roof }, Never, allBuilt: true));
        }

        [Fact]
        public void A_staying_piece_on_the_ground_needs_nothing_under_it()
        {
            PieceBox floor = Box(-1f, -0.1f, -1f, 1f, 0f, 1f);
            PieceBox chestOnFloor = Box(-0.5f, 0f, -0.5f, 0.5f, 0.8f, 0.5f);
            var old = new List<KeyValuePair<int, PieceBox>> { Old(4, floor) };
            Assert.Equal(4, RemovalOrder.Next(old, new List<PieceBox> { chestOnFloor }, box => true, allBuilt: false));
        }

        [Fact]
        public void The_highest_free_piece_goes_first()
        {
            var old = new List<KeyValuePair<int, PieceBox>> { Old(1, WallLeft), Old(2, Box(5f, 0f, 5f, 7f, 4f, 6f)) };
            Assert.Equal(2, RemovalOrder.Next(old, new List<PieceBox>(), Never, allBuilt: false));
        }

        [Fact]
        public void Two_floors_overlapping_at_the_same_height_do_not_rest_on_each_other()
        {
            PieceBox big = Box(-1f, -0.03f, -1f, 1f, 0.1f, 1f);            // wood_floor
            PieceBox small = Box(-0.5f, -0.03f, -0.5f, 0.5f, 0.1f, 0.5f);  // wood_floor_1x1 overlapping it
            Assert.False(RemovalOrder.RestsOn(big, small));
            Assert.False(RemovalOrder.RestsOn(small, big));
        }

        [Fact]
        public void Overlapping_old_floors_at_the_same_height_still_come_down()
        {
            PieceBox big = Box(-1f, -0.03f, -1f, 1f, 0.1f, 1f);
            PieceBox small = Box(-0.5f, -0.03f, -0.5f, 0.5f, 0.1f, 0.5f);
            var old = new List<KeyValuePair<int, PieceBox>> { Old(1, big), Old(2, small), Old(3, big) };
            Assert.NotEqual(-1, RemovalOrder.Next(old, new List<PieceBox>(), Never, allBuilt: false));
            Assert.NotEqual(-1, RemovalOrder.Next(old, new List<PieceBox>(), Never, allBuilt: true));
        }

        [Fact]
        public void An_old_piece_in_the_way_with_nothing_on_it_comes_down_itself()
        {
            var old = new List<KeyValuePair<int, PieceBox>> { Old(1, WallLeft), Old(2, WallRight) };
            Assert.Equal(1, RemovalOrder.TopOf(old, 1));
        }

        [Fact]
        public void An_old_beam_in_the_way_waits_for_the_old_roof_on_it()
        {
            PieceBox beam = Box(-1f, 2.05f, 2.8f, 1f, 2.45f, 3.2f);       // darkwood_beam at 2.0
            PieceBox roof = Box(-1f, 2.37f, 1f, 1f, 2.5f, 3f);            // wood_floor at 2.4, its edge on the beam
            var old = new List<KeyValuePair<int, PieceBox>> { Old(5, beam), Old(9, roof) };
            Assert.Equal(9, RemovalOrder.TopOf(old, 5));
        }

        [Fact]
        public void What_rests_on_what_rests_on_it_comes_down_first()
        {
            PieceBox upperWall = Box(-3.15f, 2.13f, -1f, -2.85f, 4.13f, 1f);
            PieceBox upperRoof = Box(-3f, 4.13f, -1f, 3f, 4.26f, 1f);
            var old = new List<KeyValuePair<int, PieceBox>> { Old(1, WallLeft), Old(3, Roof), Old(6, upperWall), Old(7, upperRoof) };
            Assert.Equal(7, RemovalOrder.TopOf(old, 1));
        }

        [Fact]
        public void A_key_that_is_not_among_the_old_pieces_is_given_back()
        {
            var old = new List<KeyValuePair<int, PieceBox>> { Old(1, WallLeft) };
            Assert.Equal(4, RemovalOrder.TopOf(old, 4));
        }

        // 1.4.1, A3: an upgrade took an old ridge down to put the new one in its place, and the kept ornament resting on
        // that ridge alone fell with it. What stays and rests on nothing but the old piece is named, so it comes down
        // first (with the hammer, not falling) and goes up again on the new one.
        [Fact]
        public void A_kept_piece_resting_only_on_an_old_one_is_named_one_with_other_support_isnt()
        {
            PieceBox ridge = Box(-3f, 3f, -0.2f, 3f, 3.2f, 0.2f);
            PieceBox ornament = Box(2.5f, 3.2f, -0.1f, 3f, 4f, 0.1f);       // on the ridge alone
            PieceBox board = Box(-3f, 3.2f, -1f, 2f, 3.3f, 1f);             // on the ridge and on a kept wall top
            PieceBox wallTop = Box(-3f, 3f, 0.6f, 3f, 3.2f, 0.8f);
            PieceBox post = Box(-0.1f, 0f, -0.1f, 0.1f, 3.2f, 0.1f);        // on the ground
            var staying = new List<PieceBox> { ornament, board, wallTop, post };
            Assert.Equal(new List<int> { 0 }, RemovalOrder.HeldUpBy(ridge, staying, b => b.Min.Y <= 0.01f));
            Assert.Empty(RemovalOrder.HeldUpBy(WallLeft, staying, b => b.Min.Y <= 0.01f));
        }
    }
}
