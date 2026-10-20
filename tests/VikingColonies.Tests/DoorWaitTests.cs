using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // Self-test run 21 of 0.12: two settlers at a gate with walls on both sides, going opposite ways, waited for each
    // other: the one whose turn it wasn't found no spot beside the gate and stood where the other steps out.
    public class DoorWaitTests
    {
        [Fact]
        public void Waiting_straight_back_keeps_clear_of_where_the_other_steps_out()
        {
            const float landingReach = 0.6f, body = 0.5f;   // DoorUser's landing reach, and a settler's own width
            Assert.True(DoorRules.BackOff(0) >= DoorRules.StepBeyond + landingReach + body);
            Assert.True(DoorRules.BackOff(1) > DoorRules.BackOff(0));
            Assert.True(DoorRules.BackOff(0) > DoorRules.StandOffs[DoorRules.StandOffs.Length - 1]);   // beyond any spot he opens from
        }
    }
}
