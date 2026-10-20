using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// 1.4.0's review, left for later: a guard's "raiders coming from the north-east!" went to every player on the server,
    /// wherever they were. It goes to those at the colony: inside its area or near it.
    /// </summary>
    public class RaidCallTests
    {
        [Fact]
        public void Players_in_the_colony_or_near_it_hear_the_call_and_those_far_off_dont()
        {
            Assert.True(GuardRules.HearsRaidCall(0f, 50f));                                  // at the Town Hall
            Assert.True(GuardRules.HearsRaidCall(50f, 50f));                                 // on its edge
            Assert.True(GuardRules.HearsRaidCall(50f + GuardRules.RaidCallReach, 50f));       // just outside, coming home
            Assert.False(GuardRules.HearsRaidCall(50f + GuardRules.RaidCallReach + 1f, 50f)); // further off
            Assert.False(GuardRules.HearsRaidCall(2000f, 90f));                              // across the world
        }

        [Fact]
        public void A_wider_colony_is_heard_further()
        {
            Assert.False(GuardRules.HearsRaidCall(110f, 50f));
            Assert.True(GuardRules.HearsRaidCall(110f, 90f));   // a level-5 hall's area
        }
    }
}
