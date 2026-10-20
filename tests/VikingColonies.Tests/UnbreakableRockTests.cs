using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The release build's hands-on run (2026-10-19): the Town Hall's camp was built into a big meadow boulder. The site's
    /// scan only knew rocks to mine, so a rock nothing breaks read as clear ground, for huts too. It's in the way for good:
    /// the site is red and says why.
    /// </summary>
    public class UnbreakableRockTests
    {
        [Fact]
        public void A_big_solid_thing_nothing_breaks_is_in_the_way_a_pebble_an_area_or_a_cart_isnt()
        {
            Assert.True(SiteRules.Unbreakable(isTrigger: false, moves: false, across: 12f, aboveGround: 4f));    // the boulder
            Assert.True(SiteRules.Unbreakable(isTrigger: false, moves: false, across: 1f, aboveGround: 0.5f));   // just big enough
            Assert.False(SiteRules.Unbreakable(isTrigger: false, moves: false, across: 0.4f, aboveGround: 0.3f)); // a pebble
            Assert.False(SiteRules.Unbreakable(isTrigger: false, moves: false, across: 0.8f, aboveGround: 2f));   // a thin post of rock
            Assert.False(SiteRules.Unbreakable(isTrigger: false, moves: false, across: 3f, aboveGround: 0.2f));   // a slab in the ground
            Assert.False(SiteRules.Unbreakable(isTrigger: true, moves: false, across: 12f, aboveGround: 4f));     // an area, not a body
            Assert.False(SiteRules.Unbreakable(isTrigger: false, moves: true, across: 3f, aboveGround: 2f));      // a cart or a ship
        }

        [Fact]
        public void A_rock_nothing_breaks_turns_the_site_red()
        {
            var findings = new SiteFindings();
            findings.AddRed(RedReason.Unbreakable);
            SiteVerdict verdict = SiteCheck.Judge(findings);
            Assert.Equal(SiteColour.Red, verdict.Colour);
            Assert.Equal(RedReason.Unbreakable, verdict.Red);
        }

        [Fact]
        public void The_rock_is_named_before_the_slope_and_after_another_building()
        {
            var steep = new SiteFindings();
            steep.AddRed(RedReason.TooSteep);
            steep.AddRed(RedReason.Unbreakable);
            Assert.Equal(RedReason.Unbreakable, SiteCheck.Judge(steep).Red);
            var building = new SiteFindings();
            building.AddRed(RedReason.Unbreakable);
            building.AddRed(RedReason.OtherBuilding);
            Assert.Equal(RedReason.OtherBuilding, SiteCheck.Judge(building).Red);
        }
    }
}
