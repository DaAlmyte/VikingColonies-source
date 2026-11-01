using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The grow-room line while placing a hut: how far it grows and which way, until the site check looks at its later levels.</summary>
    public class GrowRoomTests
    {
        /// <summary>An unturned site reaching this far from the chest: front, right, back, left.</summary>
        private static Footprint Site(float front, float right, float back, float left) =>
            new Footprint(new Vec3((right - left) / 2f, 0f, (front - back) / 2f), Quat.Identity, (right + left) / 2f, (front + back) / 2f);

        [Fact]
        public void A_longhouse_grows_only_backwards()
        {
            var levels = new List<Footprint> { Site(8f, 4f, 1f, 4f), Site(8f, 4f, 2f, 4f), Site(8f, 4f, 6f, 4f), Site(8f, 4f, 10f, 4f), Site(8f, 4f, 13f, 4f) };
            GrowRoom.Growth g = GrowRoom.From(levels, 1);
            Assert.Equal(new[] { 0f, 0f, 12f, 0f }, g.Sides);
            Assert.Equal(5, g.Level);
            Assert.True(g.Worth);
        }

        [Fact]
        public void Every_level_counts_wider_at_one_deeper_at_another()
        {
            var levels = new List<Footprint> { Site(3f, 3f, 3f, 3f), Site(3f, 6f, 3f, 3f), Site(7f, 3f, 3f, 3f) };
            GrowRoom.Growth g = GrowRoom.From(levels, 1);
            Assert.Equal(new[] { 4f, 3f, 0f, 0f }, g.Sides);
            Assert.Equal(3, g.Level);
        }

        [Fact]
        public void Placed_at_a_later_level_counts_from_there()
        {
            var levels = new List<Footprint> { Site(3f, 3f, 3f, 3f), Site(3f, 3f, 9f, 3f), Site(3f, 3f, 9f, 3f) };
            Assert.True(GrowRoom.From(levels, 1).Worth);
            GrowRoom.Growth fromTwo = GrowRoom.From(levels, 2);
            Assert.False(fromTwo.Worth);
            Assert.Equal(0, fromTwo.Level);
        }

        [Fact]
        public void A_hut_that_hardly_grows_or_has_no_design_gets_no_line()
        {
            Assert.False(GrowRoom.From(new List<Footprint> { Site(3f, 3f, 3f, 3f), Site(3.2f, 3.1f, 3f, 3f), null }, 1).Worth);
            Assert.Null(GrowRoom.From(new List<Footprint> { null, Site(9f, 9f, 9f, 9f) }, 1));
        }

        [Fact]
        public void Seen_from_the_player_the_sides_turn_with_the_hut()
        {
            var growth = new GrowRoom.Growth { Level = 5 };
            growth.Sides[2] = 12f;   // the hut's back
            Assert.Equal(new[] { 0f, 0f, 12f, 0f }, GrowRoom.Seen(growth, 0).Sides);   // front away: the back is near the player
            Assert.Equal(new[] { 12f, 0f, 0f, 0f }, GrowRoom.Seen(growth, 2).Sides);   // front toward them: the back is far
            Assert.Equal(new[] { 0f, 0f, 0f, 12f }, GrowRoom.Seen(growth, 1).Sides);   // front to their right: the back is on their left
            Assert.Equal(new[] { 0f, 12f, 0f, 0f }, GrowRoom.Seen(growth, -1).Sides);
            Assert.Equal(5, GrowRoom.Seen(growth, 3).Level);
        }
    }
}
