using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // The forager: the colony's first own food, picked from wild bushes and mushrooms around his hut.
    public class ForagerTests
    {
        private static ForagerSituation Working(int carry = 0, bool night = false, bool drops = false, bool target = true, bool full = false) =>
            new ForagerSituation { HasHut = true, IsNight = night, CarryTotal = carry, CarryCapacity = 20, StorageFull = full, DropsNearby = drops, HasPickTarget = target };

        [Fact]
        public void He_picks_what_is_ready_and_gathers_what_dropped_first()
        {
            Assert.Equal(ForagerAction.Pick, ForagerBrain.Decide(Working()));
            Assert.Equal(ForagerAction.Gather, ForagerBrain.Decide(Working(drops: true)));
        }

        [Fact]
        public void Just_after_picking_he_waits_for_the_food_to_drop()
        {
            // Seen: he set off home with his load the moment he'd picked, then turned back for the berries.
            var s = Working(carry: 1, target: false);
            s.JustPicked = true;
            Assert.Equal(ForagerAction.Gather, ForagerBrain.Decide(s));
        }

        [Fact]
        public void A_full_load_goes_home()
        {
            Assert.Equal(ForagerAction.Deposit, ForagerBrain.Decide(Working(carry: 20, drops: true)));
            Assert.Equal(ForagerAction.Pick, ForagerBrain.Decide(Working(carry: 19)));
        }

        [Fact]
        public void With_nothing_left_to_pick_he_takes_home_what_he_has_then_waits()
        {
            Assert.Equal(ForagerAction.Deposit, ForagerBrain.Decide(Working(carry: 3, target: false)));
            Assert.Equal(ForagerAction.WaitNothingToPick, ForagerBrain.Decide(Working(target: false)));
        }

        [Fact]
        public void At_night_he_delivers_then_rests()
        {
            Assert.Equal(ForagerAction.Deposit, ForagerBrain.Decide(Working(carry: 2, night: true)));
            Assert.Equal(ForagerAction.Rest, ForagerBrain.Decide(Working(night: true)));
        }

        [Fact]
        public void A_full_chest_makes_him_wait_with_his_load()
        {
            Assert.Equal(ForagerAction.WaitStorageFull, ForagerBrain.Decide(Working(carry: 5, full: true)));
            Assert.Equal(ForagerAction.Pick, ForagerBrain.Decide(Working(carry: 0, full: true)));
        }

        [Fact]
        public void Without_a_hut_he_has_no_work()
        {
            Assert.Equal(ForagerAction.Unemployed, ForagerBrain.Decide(new ForagerSituation { HasHut = false, HasPickTarget = true }));
        }

        [Fact]
        public void He_picks_only_wild_kinds_he_is_asked_for_that_are_ready()
        {
            Assert.True(Foraging.IsHis(growsBack: true, wanted: true, ready: true, planted: false));     // a raspberry bush, a dandelion
            Assert.False(Foraging.IsHis(growsBack: false, wanted: true, ready: true, planted: false));   // the player's carrots: they don't grow back
            Assert.False(Foraging.IsHis(growsBack: true, wanted: false, ready: true, planted: false));   // unticked, or flint and branches
            Assert.False(Foraging.IsHis(growsBack: true, wanted: true, ready: false, planted: false));   // picked already
        }

        [Fact]
        public void What_a_player_planted_is_left_even_when_it_grows_back()
        {
            // Review: a bush a player planted (a planting mod, or on cultivated ground) grows back too, but it's his.
            Assert.False(Foraging.IsHis(growsBack: true, wanted: true, ready: true, planted: true));
        }
    }
}
