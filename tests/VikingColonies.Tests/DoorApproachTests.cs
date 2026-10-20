using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The bug hunt of 1.4.1 (every kept test-run log): settlers walking well towards a far door (58 m to 15 m, 38 m to
    /// 6 m) gave it up at the approach's flat 12 s and the door was "tried lately" after. The approach now ends when he stops
    /// getting closer, or after a time that grows with the distance.
    /// </summary>
    public class DoorApproachTests
    {
        [Fact]
        public void Walking_steadily_to_a_far_door_he_keeps_going()
        {
            Assert.False(DoorRules.ApproachGivesUp(elapsed: 30f, sinceCloser: 0.2f, startDistance: 55f));
            Assert.False(DoorRules.ApproachGivesUp(elapsed: 13f, sinceCloser: 0.1f, startDistance: 20f));
        }

        [Fact]
        public void Getting_no_closer_for_a_while_he_gives_up()
        {
            Assert.True(DoorRules.ApproachGivesUp(elapsed: 9f, sinceCloser: DoorRules.ApproachStallSeconds + 0.5f, startDistance: 55f));
            Assert.False(DoorRules.ApproachGivesUp(elapsed: 9f, sinceCloser: DoorRules.ApproachStallSeconds - 0.5f, startDistance: 55f));
        }

        [Fact]
        public void The_whole_passing_leaves_room_for_the_longest_approach()
        {
            // The whole-mod review of 1.4.1: the passing's own limit (no progress 25 s, all of it 60 s) still cut the longer
            // approach off. Getting closer is progress, and the whole passing outlasts the longest approach.
            Assert.True(DoorRules.MaxPassSeconds > DoorRules.ApproachMaxSeconds + 15);
            Assert.False(DoorRules.PassingOver(sinceStart: 80, sinceProgress: 0.5));
        }

        [Fact]
        public void A_near_door_keeps_the_old_short_limit_and_a_far_one_has_a_cap()
        {
            Assert.False(DoorRules.ApproachGivesUp(elapsed: 12f, sinceCloser: 1f, startDistance: 2f));
            Assert.True(DoorRules.ApproachGivesUp(elapsed: 16f, sinceCloser: 1f, startDistance: 2f));   // 12 s and a little for 2 m
            Assert.True(DoorRules.ApproachGivesUp(elapsed: DoorRules.ApproachMaxSeconds + 1f, sinceCloser: 0.1f, startDistance: 500f));
        }
    }
}
