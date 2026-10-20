using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The camp-at-the-boulder run on the 1.4.0 build (2026-10-19): with rocks reaching into a site counted, the builder
    /// couldn't get to a big boulder's part, "trying again later" every minute for over ten minutes, and the camp never
    /// went up. Something he has given up on a few times is left, but only where the building itself doesn't go (the
    /// margin round it, in front of its doors, above its top): the fix pass's review found that leaving a tree or a rock
    /// where the building goes had its pieces put up into it, with nothing said.
    /// </summary>
    public class SiteGiveUpTests
    {
        [Fact]
        public void A_thing_outside_the_building_given_up_on_three_times_is_left()
        {
            var giveUps = new SiteGiveUps();
            giveUps.Add("rock4_coast#3");
            giveUps.Add("rock4_coast#3");
            Assert.False(giveUps.Left("rock4_coast#3", inTheBuilding: false));
            giveUps.Add("rock4_coast#3");
            Assert.True(giveUps.Left("rock4_coast#3", inTheBuilding: false));
            Assert.False(giveUps.Left("rock4_coast#4", inTheBuilding: false));   // another part is tried as before
            Assert.False(giveUps.Left(null, inTheBuilding: false));
        }

        [Fact]
        public void A_thing_where_the_building_goes_is_never_left_it_keeps_holding_the_site()
        {
            var giveUps = new SiteGiveUps();
            for (int i = 0; i < SiteGiveUps.LeaveAfter + 2; i++) giveUps.Add("Beech1#1");
            Assert.False(giveUps.Left("Beech1#1", inTheBuilding: true));
        }

        // The whole-mod review of 1.4.1's builder (M1): an item he couldn't pick up (on an old hut's roof) or a bush held the
        // clearing for good. Only what the building can't go up through holds the site: a tree, a log, a stump, a rock.
        [Fact]
        public void Only_trees_logs_stumps_and_rocks_hold_the_site_items_and_plants_are_left()
        {
            Assert.True(SiteGiveUps.HoldsTheSite(ClearKind.Tree));
            Assert.True(SiteGiveUps.HoldsTheSite(ClearKind.Log));
            Assert.True(SiteGiveUps.HoldsTheSite(ClearKind.Stump));
            Assert.True(SiteGiveUps.HoldsTheSite(ClearKind.Rock));
            Assert.False(SiteGiveUps.HoldsTheSite(ClearKind.Item));
            Assert.False(SiteGiveUps.HoldsTheSite(ClearKind.Plant));
            Assert.False(SiteGiveUps.HoldsTheSite(ClearKind.Leftover));
        }

        // The camp-at-the-boulder run on 1.4.1's builder (2026-10-19): the builder climbed onto the boulder on his way to
        // a part, and from up there no way led to any spot on the ground: every part failed in 2 s, over and over. His
        // pickaxe's blow lands where he aims; stuck within a few metres of a part, he breaks it from where he stands.
        [Fact]
        public void Stuck_within_a_few_metres_of_a_rock_part_he_breaks_it_from_where_he_stands()
        {
            Assert.True(RockWork.FromHere(stuck: true, rockPart: true, distance: 3f));
            Assert.True(RockWork.FromHere(stuck: true, rockPart: true, distance: RockWork.FromHereReach));
            Assert.False(RockWork.FromHere(stuck: true, rockPart: true, distance: RockWork.FromHereReach + 0.5f));
            Assert.False(RockWork.FromHere(stuck: false, rockPart: true, distance: 1f));   // still on his way
            Assert.False(RockWork.FromHere(stuck: true, rockPart: false, distance: 1f));   // a tree: felled from the right side
        }

        [Fact]
        public void A_new_site_starts_afresh()
        {
            var giveUps = new SiteGiveUps();
            for (int i = 0; i < SiteGiveUps.LeaveAfter; i++) giveUps.Add("Beech1#1");
            giveUps.Clear();
            Assert.False(giveUps.Left("Beech1#1", inTheBuilding: false));
        }
    }
}
