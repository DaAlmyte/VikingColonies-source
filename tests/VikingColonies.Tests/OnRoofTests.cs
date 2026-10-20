using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The old-huts upgrade test (2026-10-19): stepping out of the Builder's Hut under a low eave, a builder landed on its
    /// roof and walked about up there for 40 minutes, his orders waiting; roaming 8 m, the boxed-in backstop's 4 m patch
    /// never caught it. A settler standing on a roof, well above the ground, for a few seconds is put down.
    /// </summary>
    public class OnRoofTests
    {
        [Fact]
        public void A_settler_up_on_a_roof_for_a_while_is_put_down()
        {
            Assert.True(RoofRules.StuckUp(onRoof: true, aboveGround: 2.2f, seconds: RoofRules.Seconds));
            Assert.False(RoofRules.StuckUp(onRoof: true, aboveGround: 2.2f, seconds: RoofRules.Seconds - 1f));   // a moment: let him be
            Assert.False(RoofRules.StuckUp(onRoof: true, aboveGround: 0.6f, seconds: 60f));    // a lean-to's low end, by the ground
            Assert.False(RoofRules.StuckUp(onRoof: false, aboveGround: 4f, seconds: 60f));     // a platform or a floor up high: his to stand on
        }

        // The ship branch's check (2026-10-19): the roof rescue asked the boxed-in watch, which only says yes after two
        // minutes of trying to walk or four of wanting elsewhere, so a settler stayed up there minutes, not seconds.
        [Fact]
        public void Put_down_from_a_roof_needs_no_boxed_in_clock_only_the_limit_of_moves()
        {
            var watch = new TrappedWatch();
            watch.Arrived(0f, 0f);
            Assert.False(watch.Due);
            Assert.True(watch.ShouldMoveOffRoof(now: 1000));
            watch.Moved(0f, 0f, 1000);
            Assert.True(watch.ShouldMoveOffRoof(now: 1010));
            watch.Moved(0f, 0f, 1010);
            Assert.False(watch.ShouldMoveOffRoof(now: 1020));                                   // twice in ten minutes already
            Assert.True(watch.ShouldMoveOffRoof(now: 1000 + TrappedWatch.RescueWindow));
        }
    }
}
