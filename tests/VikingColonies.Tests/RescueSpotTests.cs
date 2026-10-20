using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // 1.4.0's self-test on master: a settler stuck by the life test's fire burned to death; and every rescue sets a
    // settler down on "a free spot by the Town Hall", where the camp's fire pit (a bonfire from level 3) now burns. A
    // fire pit's stones are too low for the spot's standing-room check, so a rescue could set someone down in the fire.
    public class RescueSpotTests
    {
        private static readonly List<FireRing> CampFire = new List<FireRing> { new FireRing(10f, 16f, 2.2f) };   // a bonfire

        [Fact]
        public void A_rescue_spot_keeps_clear_of_a_fire_by_its_reach_and_a_margin()
        {
            Assert.False(RescueSpot.ClearOfFires(10f, 16f, CampFire));                                   // in it
            Assert.False(RescueSpot.ClearOfFires(10f, 16f + 2.2f + RescueSpot.FireMargin - 0.1f, CampFire)); // at its edge
            Assert.True(RescueSpot.ClearOfFires(10f, 16f + 2.2f + RescueSpot.FireMargin + 0.1f, CampFire));
        }

        [Fact]
        public void With_no_fire_anywhere_every_spot_is_clear()
        {
            Assert.True(RescueSpot.ClearOfFires(0f, 0f, new List<FireRing>()));
            Assert.True(RescueSpot.ClearOfFires(0f, 0f, null));
        }

        [Fact]
        public void A_small_campfire_still_gets_the_margin()
        {
            var campfire = new List<FireRing> { new FireRing(0f, 0f, 0f) };   // reach unknown: the margin alone
            Assert.False(RescueSpot.ClearOfFires(1f, 0f, campfire));
            Assert.True(RescueSpot.ClearOfFires(RescueSpot.FireMargin + 0.6f, 0f, campfire));
        }
    }
}
