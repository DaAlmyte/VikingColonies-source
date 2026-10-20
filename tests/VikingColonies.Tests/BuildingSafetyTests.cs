using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class BuildingSafetyTests
    {
        [Theory]
        [InlineData(true, false, false, true)]    // a falling tree or log
        [InlineData(true, false, true, true)]     // a tree hit, whoever caused it
        [InlineData(false, true, false, true)]    // a rolling log's impact (no attacker)
        [InlineData(false, true, true, false)]    // an impact somebody caused (a thrown or swung thing)
        [InlineData(false, false, false, false)]  // e.g. lost support or fire
        [InlineData(false, false, true, false)]   // a monster's attack
        public void Colony_buildings_ignore_only_falling_trees_and_rolling_logs(bool treeHit, bool impactHit, bool hasAttacker, bool ignored)
        {
            Assert.Equal(ignored, BuildingSafety.IgnoresHit(treeHit, impactHit, hasAttacker));
        }

        [Fact]
        public void A_settlers_tool_swing_never_damages_a_building()
        {
            Assert.True(BuildingSafety.IgnoresSettlerHit(attackerIsSettler: true));
            Assert.False(BuildingSafety.IgnoresSettlerHit(attackerIsSettler: false));   // players and monsters still do damage
        }
    }
}
