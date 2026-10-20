using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // Huts, round 2 (2026-10-18): the player's gallery notes.
    public class HutsRoundTwoTests
    {
        [Fact]
        public void The_guards_post_is_in_front_of_the_towers_door()
        {
            // The new tower: its chest against the back wall, the door 3.4 m in front of it and 1 m to its right.
            GuardPosts.PostSpot(true, 1f, 3.4f, out float x, out float z);
            Assert.Equal(1f, x);
            Assert.Equal(6.2f, z, 3);
            // The old tower: the door 2 m in front of the chest.
            GuardPosts.PostSpot(true, 0f, 2f, out x, out z);
            Assert.Equal(4.8f, z, 3);
            // No door to go by: 3.5 m in front of the chest.
            GuardPosts.PostSpot(false, 0f, 0f, out x, out z);
            Assert.Equal(GuardPosts.ChestOut, z);
        }

        // The review of 2026-10-19: at 1.5 m the first guard stood where anyone leaving the tower steps out to
        // (DoorRules.StepBeyond); they waited, gave up and tried again, for good under Hold orders.
        [Fact]
        public void The_first_guard_stands_clear_of_where_the_door_lets_out()
        {
            Assert.True(GuardPosts.DoorOut - DoorRules.StepBeyond > 1f);
            Assert.Equal(0f, GuardPosts.Offset(0));   // the first guard on the post itself
        }
    }
}
