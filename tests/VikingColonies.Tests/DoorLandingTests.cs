using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // Seen: a hut on a slope with its door facing uphill; the builder levelled only the hut's own footprint, so a bank
    // rose right in front of the door and settlers got stuck in the dip between it and the door.
    public class DoorLandingTests
    {
        private static readonly Footprint Hut = new Footprint(new Vec3(0f, 0f, 0f), Quat.Identity, 3f, 3f);

        [Fact]
        public void The_ground_in_front_of_a_door_is_levelled_out_from_the_huts_edge()
        {
            Footprint landing = DoorLanding.For(Hut, new Vec3(0f, 1f, 3f), new Vec3(0f, 0f, 1f));
            Assert.True(landing.Contains(new Vec3(0f, 0f, 3.5f)));     // just outside the door
            Assert.True(landing.Contains(new Vec3(1.2f, 0f, 5.5f)));   // a step aside, further out
            Assert.False(landing.Contains(new Vec3(0f, 0f, 2f)));      // inside the hut: the hut's own levelling
            Assert.False(landing.Contains(new Vec3(0f, 0f, 7f)));      // well away from the door
            Assert.False(landing.Contains(new Vec3(3f, 0f, 4.5f)));    // round the corner
        }

        [Fact]
        public void Nothing_between_the_huts_edge_and_the_landing_is_left_out()
        {
            // Self-test run 33: a wild bush stood in the sliver between the site's edge and the landing (the edge is found in
            // 0.25 m steps), so neither clearing took it, and it blocked the door.
            Footprint landing = DoorLanding.For(Hut, new Vec3(0f, 1f, 2.9f), new Vec3(0f, 0f, 1f));
            for (float z = 2f; z <= 6f; z += 0.05f)
            {
                var p = new Vec3(0f, 0f, z);
                Assert.True(Hut.Contains(p) || landing.Contains(p), $"left out at z = {z:0.00}");
            }
        }

        [Fact]
        public void A_door_set_back_inside_the_footprint_still_gets_its_landing_outside()
        {
            Footprint landing = DoorLanding.For(Hut, new Vec3(0f, 1f, 2f), new Vec3(0f, 0f, 1f));
            Assert.True(landing.Contains(new Vec3(0f, 0f, 4f)));
            Assert.False(landing.Contains(new Vec3(0f, 0f, 2.5f)));
        }

        [Fact]
        public void In_front_of_a_door_the_builder_clears_what_grows_and_lies_there_but_no_ones_things()
        {
            // Seen: a small beech in a doorway kept settlers out. Growing and natural things go; items and pieces stay.
            foreach (ClearKind kind in new[] { ClearKind.Plant, ClearKind.Tree, ClearKind.Log, ClearKind.Stump, ClearKind.Rock })
                Assert.True(SiteRules.ClearsInFrontOfDoor(kind), kind.ToString());
            Assert.False(SiteRules.ClearsInFrontOfDoor(ClearKind.Item));
            Assert.False(SiteRules.ClearsInFrontOfDoor(ClearKind.Leftover));
        }

        [Fact]
        public void A_door_on_the_east_side_gets_its_landing_to_the_east()
        {
            Footprint landing = DoorLanding.For(Hut, new Vec3(3f, 1f, 0f), new Vec3(1f, 0f, 0f));
            Assert.True(landing.Contains(new Vec3(4.5f, 0f, 0.8f)));
            Assert.False(landing.Contains(new Vec3(4.5f, 0f, 3f)));
            Assert.False(landing.Contains(new Vec3(0f, 0f, 4.5f)));
        }
    }
}
