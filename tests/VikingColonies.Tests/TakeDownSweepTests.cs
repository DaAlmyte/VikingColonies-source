using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The second full run on the 1.4.0 build (2026-10-19): a Forager's Hut taken down on a hillock left 1 stack of its
    /// materials lying 2 s after it went: the sweep at the end took only what lay on the hut's own ground, and a stack had
    /// rolled off it. What came down during the take-down is swept up a few metres past its ground too; a player's own
    /// pile beside the hut, lying there before, stays (0.26, the review of 0.24).
    /// </summary>
    public class TakeDownSweepTests
    {
        [Fact]
        public void What_lies_on_the_huts_ground_is_swept_up()
        {
            Assert.True(TakeDownSweep.Takes(onHutGround: true, newSinceTakeDown: false, nearItsGround: true));
        }

        [Fact]
        public void A_stack_that_rolled_off_its_ground_during_the_take_down_is_swept_up_too()
        {
            Assert.True(TakeDownSweep.Takes(onHutGround: false, newSinceTakeDown: true, nearItsGround: true));
            Assert.False(TakeDownSweep.Takes(onHutGround: false, newSinceTakeDown: true, nearItsGround: false));   // too far: someone else's
            Assert.True(TakeDownSweep.RolledFar >= 3f);
        }

        [Fact]
        public void A_players_pile_beside_the_hut_from_before_stays()
        {
            Assert.False(TakeDownSweep.Takes(onHutGround: false, newSinceTakeDown: false, nearItsGround: true));
        }
    }
}
