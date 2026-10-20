using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // Seen in game: in a level-2 Longhouse no bed's sleeping spot could be walked to. The spot was "1 m from the bed's
    // middle towards the chest", which ended against the bed's own edge. Now: the four spots around the bed (beside each
    // long side, past each end), nearest the chest first; the game takes the first that is free and can be walked to.
    public class BedSpotTests
    {
        [Fact]
        public void There_are_four_spots_around_a_bed_clear_of_it()
        {
            IReadOnlyList<Vec3> spots = BedSpot.Candidates(new Vec3(0f, 0f, 0f), Quat.Identity, new Vec3(10f, 0f, 0f));
            Assert.Equal(4, spots.Count);
            // A bed is 1.2 m wide (its x) and 2.8 m long (its z): spots 0.6 m clear of it.
            Assert.Contains(spots, s => Near(s, 1.2f, 0f));
            Assert.Contains(spots, s => Near(s, -1.2f, 0f));
            Assert.Contains(spots, s => Near(s, 0f, 2f));
            Assert.Contains(spots, s => Near(s, 0f, -2f));
        }

        [Fact]
        public void The_spot_nearest_the_chest_comes_first()
        {
            // A bunk: its head against the east wall, its foot towards the room, where the chest stands.
            Vec3 first = BedSpot.Candidates(new Vec3(3.05f, 0f, -2f), Quat.Yaw(90f), new Vec3(0f, 0f, -4.3f))[0];
            Assert.True(Near(first, 1.05f, -2f), $"first spot ({first.X}, {first.Z})");
        }

        [Fact]
        public void The_spots_are_on_the_chests_floor()
        {
            foreach (Vec3 spot in BedSpot.Candidates(new Vec3(2f, 9.6f, -1f), Quat.Identity, new Vec3(0f, 10f, -5f)))
                Assert.Equal(10f, spot.Y, 2);
        }

        private static bool Near(Vec3 v, float x, float z) => System.Math.Abs(v.X - x) < 0.01f && System.Math.Abs(v.Z - z) < 0.01f;
    }
}
