using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class SettlerSafetyTests
    {
        [Theory]
        [InlineData(true, false, false, true)]    // a falling tree or log
        [InlineData(true, false, true, true)]     // a tree hit, whoever caused it
        [InlineData(false, true, false, true)]    // a rolling log's impact (no attacker)
        [InlineData(false, true, true, false)]    // an impact somebody caused
        [InlineData(false, false, false, false)]  // a fall, fire, drowning
        [InlineData(false, false, true, false)]   // a monster's attack
        public void A_tree_hit_is_what_colony_buildings_shrug_off(bool treeHit, bool impactHit, bool hasAttacker, bool expected)
        {
            Assert.Equal(expected, SettlerSafety.IsTreeHit(treeHit, impactHit, hasAttacker));
        }

        [Fact]
        public void With_the_setting_on_settlers_ignore_tree_hits_and_nothing_else()
        {
            Assert.True(SettlerSafety.IgnoresHit(true, treeHit: true, impactHit: false, hasAttacker: false));
            Assert.True(SettlerSafety.IgnoresHit(true, treeHit: false, impactHit: true, hasAttacker: false));
            Assert.False(SettlerSafety.IgnoresHit(true, treeHit: false, impactHit: false, hasAttacker: true));
            Assert.False(SettlerSafety.IgnoresHit(true, treeHit: false, impactHit: false, hasAttacker: false));
        }

        [Fact]
        public void With_the_setting_off_tree_hits_hurt()
        {
            Assert.False(SettlerSafety.IgnoresHit(false, treeHit: true, impactHit: false, hasAttacker: false));
            Assert.False(SettlerSafety.IgnoresHit(false, treeHit: false, impactHit: true, hasAttacker: false));
        }
    }
}
