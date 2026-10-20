using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // 1.4.0's self-test on master: a settler squeezed between a hut's wall and the life test's fire burned to death, 2 m
    // from it, in her place for the evening. The camp brings a fire (a bonfire from level 3) to every colony, and the
    // colony gathers round it every evening: one who keeps burning without getting away is moved off (the player's
    // "simple fail safes").
    public class BurnRulesTests
    {
        [Fact]
        public void Burning_for_a_few_seconds_without_getting_away_moves_him()
        {
            Assert.False(BurnRules.StuckBurning(burning: true, seconds: BurnRules.Seconds - 0.5f));
            Assert.True(BurnRules.StuckBurning(burning: true, seconds: BurnRules.Seconds));
        }

        [Fact]
        public void Not_burning_never_moves_him()
        {
            Assert.False(BurnRules.StuckBurning(burning: false, seconds: 60f));
        }

        [Fact]
        public void Getting_away_from_where_he_caught_fire_starts_again()
        {
            Assert.True(BurnRules.StillThere(dx: 0.5f, dz: 0.5f));
            Assert.False(BurnRules.StillThere(dx: BurnRules.Patch + 0.1f, dz: 0f));
        }

        [Fact]
        public void He_burns_long_enough_to_die_well_after_he_is_moved()
        {
            // A settler has 100 health; the fire took 10 a hit about once a second: moved after 4 s he lives.
            Assert.True(BurnRules.Seconds * 10f < 100f / 2f);
        }
    }
}
