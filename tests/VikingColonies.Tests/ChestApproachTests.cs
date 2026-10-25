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
    }
}
