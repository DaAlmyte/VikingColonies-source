using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class SitePrepTests
    {
        [Fact]
        public void A_piece_counts_on_a_site_by_its_solid_parts_only()
        {
            Assert.False(SiteRules.Counts(isTrigger: true, isPiece: true));    // a fire's warmth or a workbench's base area
            Assert.True(SiteRules.Counts(isTrigger: false, isPiece: true));
            Assert.True(SiteRules.Counts(isTrigger: true, isPiece: false));    // a berry bush or a dropped item
        }

        // The release build's hands-on run (2026-10-19): a big boulder whose middle stood off the Town Hall camp's lot was
        // left, and the camp was built into it: a rock the site's scan reaches (any of it on the site) is cleared. The fix
        // pass's review: but breaking a rock takes the support of what rests on it (the game grounds a piece on any solid
        // body within 0.15 m that isn't a building piece), and a whole rock breaks, its part off the lot too. A rock
        // another building touches is never broken: in the way of the building itself it blocks the site, in its margin
        // it is left alone.
        [Fact]
        public void A_rock_on_a_site_is_cleared_unless_another_building_rests_on_it()
        {
            Assert.Equal(RockOnSite.Clear, SiteRules.Rock(holdsAnother: false, holdsOwn: false, inTheBuilding: true));
            Assert.Equal(RockOnSite.Clear, SiteRules.Rock(holdsAnother: false, holdsOwn: false, inTheBuilding: false));
            Assert.Equal(RockOnSite.Blocks, SiteRules.Rock(holdsAnother: true, holdsOwn: false, inTheBuilding: true));
            Assert.Equal(RockOnSite.LeftAlone, SiteRules.Rock(holdsAnother: true, holdsOwn: false, inTheBuilding: false));
        }

        // The whole-mod review of 1.4.1's builder (M6): a hut built into a boulder under 1.4.0 has its own pieces resting on
        // the rock; its repair or next level broke the rock under them, and they fell. A rock the site's own standing
        // pieces rest on is left as it is, wherever it reaches.
        [Fact]
        public void A_rock_the_sites_own_standing_pieces_rest_on_is_left_as_it_is()
        {
            Assert.Equal(RockOnSite.LeftAlone, SiteRules.Rock(holdsAnother: false, holdsOwn: true, inTheBuilding: true));
            Assert.Equal(RockOnSite.LeftAlone, SiteRules.Rock(holdsAnother: false, holdsOwn: true, inTheBuilding: false));
            Assert.Equal(RockOnSite.Blocks, SiteRules.Rock(holdsAnother: true, holdsOwn: true, inTheBuilding: true));   // another's still blocks
        }

        [Fact]
        public void Only_plain_items_are_carried_off_a_site()
        {
            Assert.True(SiteRules.CanCarryOff(maxStack: 50, quality: 1, worn: false, crafted: false));   // wood, stone, berries
            Assert.False(SiteRules.CanCarryOff(maxStack: 1, quality: 1, worn: false, crafted: false));   // a tool or a weapon
            Assert.False(SiteRules.CanCarryOff(maxStack: 50, quality: 2, worn: false, crafted: false));
            Assert.False(SiteRules.CanCarryOff(maxStack: 50, quality: 1, worn: true, crafted: false));
            Assert.False(SiteRules.CanCarryOff(maxStack: 50, quality: 1, worn: false, crafted: true));
        }

        [Fact]
        public void While_clearing_everything_waits_for_the_site()
        {
            Assert.True(SitePrep.HoldsUpBuilding(BuildState.Clearing, busy: false, waiting: true));
            Assert.True(SitePrep.HoldsUpBuilding(BuildState.Clearing, busy: true, waiting: false));
            Assert.False(SitePrep.HoldsUpBuilding(BuildState.Clearing, busy: false, waiting: false));
        }

        [Fact]
        public void Once_building_only_what_he_is_clearing_right_now_holds_it_up()
        {
            Assert.True(SitePrep.HoldsUpBuilding(BuildState.Building, busy: true, waiting: false));
            Assert.False(SitePrep.HoldsUpBuilding(BuildState.Building, busy: false, waiting: true));   // named in the hut window instead
        }
    }
}
