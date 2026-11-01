using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The old huts' upgrade test (2026-10-19): a builder took from the Builder's Hut's chest standing behind its back wall,
    /// through the wall. Within reach of a chest he takes from it unless a wall stands between them; then he goes round to
    /// a side of it with nothing between. The fix pass's review: once round, a post still counting as a wall sent him
    /// round again every other frame, and a chest whose open side he couldn't reach cost a stuck wait on every fetch: what
    /// he found is kept for that chest until he turns to another one or leaves the site.
    /// </summary>
    public class ChestApproachTests
    {
        [Fact]
        public void Within_reach_with_nothing_between_he_takes()
        {
            var round = new ChestRound();
            Assert.Equal(ChestStep.Take, round.AtReach("chest1", wallBetween: false));
        }

        [Fact]
        public void Within_reach_through_a_wall_he_goes_round_first()
        {
            var round = new ChestRound();
            Assert.Equal(ChestStep.GoRound, round.AtReach("chest1", wallBetween: true));
        }

        [Fact]
        public void Once_round_he_keeps_taking_from_there_even_with_a_post_between()
        {
            var round = new ChestRound();
            round.WentRound("chest1");
            for (int frame = 0; frame < 5; frame++) Assert.Equal(ChestStep.Take, round.AtReach("chest1", wallBetween: true));
            Assert.True(round.GoingRound("chest1"));   // the next fetch goes straight to its open side
        }

        [Fact]
        public void A_chest_whose_open_side_he_couldnt_reach_is_taken_from_where_he_is_without_trying_again()
        {
            var round = new ChestRound();
            round.Failed("chest1");
            Assert.Equal(ChestStep.Take, round.AtReach("chest1", wallBetween: true));
            Assert.Equal(ChestStep.Take, round.AtReach("chest1", wallBetween: true));
            Assert.False(round.GoingRound("chest1"));
        }

        // The whole-mod review of 1.4.1's builder (M3): a fetch goes to his hut's chest and the Town Hall's by turns, and the
        // memory of one chest was lost at the other. Each chest is remembered until he leaves the site.
        [Fact]
        public void Each_chest_is_remembered_until_he_leaves_the_site()
        {
            var round = new ChestRound();
            round.WentRound("chest1");
            Assert.Equal(ChestStep.GoRound, round.AtReach("chest2", wallBetween: true));   // a new one is looked at afresh
            round.Failed("chest2");
            Assert.True(round.GoingRound("chest1"));                                        // the first is still remembered
            Assert.Equal(ChestStep.Take, round.AtReach("chest2", wallBetween: true));
            round.Leave();
            Assert.False(round.GoingRound("chest1"));
            Assert.Equal(ChestStep.GoRound, round.AtReach("chest2", wallBetween: true));
        }
    
        [Fact]   // the review round's bug hunt (the 1.5.0 build-through's log): an idle builder putting leftovers back looked for a
                 // site every 5 s, each look forgot the chest he couldn't get round to, and he circled his hut for an hour
        public void A_look_for_a_site_that_finds_none_keeps_what_he_learned()
        {
            var round = new ChestRound();
            round.Failed("hut");
            round.Relook(heldSite: false, newSite: false);
            Assert.Equal(ChestStep.Take, round.AtReach("hut", wallBetween: true));      // from where he can, not round again
            round.Relook(heldSite: false, newSite: true);
            Assert.Equal(ChestStep.GoRound, round.AtReach("hut", wallBetween: true));   // a new site: looked at afresh
            round.WentRound("hut");
            round.Relook(heldSite: true, newSite: false);
            Assert.False(round.GoingRound("hut"));                                       // he left a site: afresh too
        }

        [Fact]   // a player's report (1.4.3, "NPC Path (Builder)"): almost every fetch, the builder walked to the Builder's Hut's
                 // chest from behind its back wall, then went round to the door: a chest new to him is gone to at its open side
        public void A_chest_new_to_him_is_gone_to_at_its_open_side_from_the_first_walk()
        {
            var round = new ChestRound();
            Assert.True(round.IsNew("hut"));
            Assert.Equal(ChestGoal.OpenSide, round.FirstGoal("hut", hasOpenSide: true));
            Assert.True(round.GoingRound("hut"));                                        // straight there, not by the back wall first
            Assert.False(round.IsNew("hut"));
            Assert.Equal(ChestGoal.OpenSide, round.FirstGoal("hut", hasOpenSide: false));   // decided once for the site
            Assert.Equal(ChestGoal.Chest, round.FirstGoal("open", hasOpenSide: false));    // none: to the chest, as before
            Assert.False(round.IsNew("open"));                                            // looked for once, not on every step
            round.Failed("hut");
            Assert.Equal(ChestGoal.Chest, round.FirstGoal("hut", hasOpenSide: true));      // couldn't get to it: not again this site
            round.Leave();
            Assert.True(round.IsNew("hut"));
        }

        [Fact]   // its review: the game check must be able to see a take from behind a chest (its back to a hut's wall)
        public void Standing_behind_a_chest_is_told_from_its_front_and_sides()
        {
            Assert.True(ChestSides.Behind(0f, -1.2f, 0f, 1f));     // the chest faces +z, he stands at -z
            Assert.False(ChestSides.Behind(0f, 1.1f, 0f, 1f));     // in front
            Assert.False(ChestSides.Behind(1.1f, 0f, 0f, 1f));     // beside
            Assert.False(ChestSides.Behind(0f, 0f, 0f, 1f));       // right on it: nothing to tell
        }

        [Fact]   // its review: the first look is a plan, checked once when he gets there (the look may come before the walls stand)
        public void A_planned_open_side_is_checked_once_on_arrival()
        {
            var round = new ChestRound();
            round.FirstGoal("hut", hasOpenSide: true);
            Assert.True(round.ChecksOnArrival("hut"));
            Assert.Equal(ChestStep.Take, round.AtReach("hut", wallBetween: false));      // the side is open: settled
            Assert.False(round.ChecksOnArrival("hut"));
            Assert.True(round.GoingRound("hut"));
            Assert.Equal(ChestStep.Take, round.AtReach("hut", wallBetween: true));       // a post counting as a wall doesn't send him round again (1.4.1)
        }

        [Fact]   // its review: a hut built round its own chest has no walls at the first look
        public void A_side_planned_before_the_walls_stood_is_looked_at_again_on_arrival()
        {
            var round = new ChestRound();
            round.FirstGoal("hut", hasOpenSide: true);
            Assert.Equal(ChestStep.GoRound, round.AtReach("hut", wallBetween: true));    // there's a wall between after all: look again
            round.WentRound("hut");
            Assert.False(round.ChecksOnArrival("hut"));
            Assert.Equal(ChestStep.Take, round.AtReach("hut", wallBetween: true));
        }

        [Fact]   // its review: a piece mid-placement may block every side at the first look
        public void A_chest_with_no_open_side_at_first_is_looked_at_again_once_on_arrival()
        {
            var round = new ChestRound();
            round.FirstGoal("chest", hasOpenSide: false);
            Assert.True(round.ChecksOnArrival("chest"));
            Assert.Equal(ChestStep.GoRound, round.AtReach("chest", wallBetween: true));
            round.Failed("chest");
            Assert.False(round.ChecksOnArrival("chest"));
            Assert.Equal(ChestStep.Take, round.AtReach("chest", wallBetween: true));
            round.FirstGoal("open", hasOpenSide: false);
            Assert.Equal(ChestStep.Take, round.AtReach("open", wallBetween: false));     // nothing between: from where he is, and settled
            Assert.False(round.ChecksOnArrival("open"));
        }
    }
}
