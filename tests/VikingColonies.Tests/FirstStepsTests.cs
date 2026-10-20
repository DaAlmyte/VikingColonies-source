using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // First steps (0.11): words above a settler's head when he needs the player, and when Hugin's tips come.
    public class FirstStepsTests
    {
        [Fact]
        public void Asleep_sheltering_or_fleeing_a_settler_greets_nobody()
        {
            Assert.True(HelpWords.IsResting("$vc_status_sleeping"));
            Assert.True(HelpWords.IsResting("$vc_status_sheltering"));
            Assert.True(HelpWords.IsResting("$vc_status_fleeing"));
            Assert.False(HelpWords.IsResting("$vc_status_jobless"));
            Assert.False(HelpWords.IsResting(null));
        }

        [Fact]
        public void A_settler_shows_words_only_when_he_needs_the_player()
        {
            Assert.Equal("$vc_status_needs 12 Wood", HelpWords.Words("$vc_status_needs 12 Wood", hungry: false));
            Assert.Equal("$vc_status_storage_full", HelpWords.Words("$vc_status_storage_full", hungry: false));
            Assert.Equal("$vc_status_cant_reach_hut", HelpWords.Words("$vc_status_cant_reach_hut", hungry: false));
            Assert.Equal("$vc_status_in_the_way $piece_stone", HelpWords.Words("$vc_status_in_the_way $piece_stone", hungry: false));
            Assert.Equal("", HelpWords.Words("$vc_status_working", hungry: false));     // plain work: nothing
            Assert.Equal("", HelpWords.Words("$vc_status_sleeping", hungry: false));
            Assert.Equal("", HelpWords.Words(null, hungry: false));
        }

        [Fact]
        public void A_hungry_settler_says_so_unless_he_needs_something_more_pressing()
        {
            Assert.Equal(HelpWords.HungryToken, HelpWords.Words("$vc_status_working", hungry: true));
            Assert.Equal("$vc_status_needs 3 Stone", HelpWords.Words("$vc_status_needs 3 Stone", hungry: true));
            Assert.Equal("", HelpWords.Words("$vc_status_sleeping", hungry: true));     // review: not through a Longhouse's walls at night
            Assert.Equal("", HelpWords.Words("$vc_status_sheltering", hungry: true));
        }

        [Fact]
        public void Hugin_tells_of_the_night_near_a_colony_with_settlers()
        {
            Assert.True(TipRules.NightNearColony(night: true, distanceToTownHall: 30f, colonyRadius: 50f, settlers: 4));
            Assert.False(TipRules.NightNearColony(night: false, distanceToTownHall: 30f, colonyRadius: 50f, settlers: 4));
            Assert.False(TipRules.NightNearColony(night: true, distanceToTownHall: 80f, colonyRadius: 50f, settlers: 4));
            Assert.False(TipRules.NightNearColony(night: true, distanceToTownHall: 30f, colonyRadius: 50f, settlers: 0));
        }

        [Fact]
        public void A_hut_placed_before_any_Builder_s_Hut_gets_the_builder_tip()
        {
            Assert.True(TipRules.NeedsBuilderTip(isBuilderHut: false, colonyHasBuilderHut: false));
            Assert.False(TipRules.NeedsBuilderTip(isBuilderHut: true, colonyHasBuilderHut: false));
            Assert.False(TipRules.NeedsBuilderTip(isBuilderHut: false, colonyHasBuilderHut: true));
        }
    }
}
