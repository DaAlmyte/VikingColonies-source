using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class MoveRulesTests
    {
        [Theory]
        [InlineData(0.4f, 0.8f)]
        [InlineData(0.8f, 0.8f)]
        [InlineData(2.5f, 2.5f)]
        public void Stop_distance_is_never_below_the_minimum(float requested, float expected)
        {
            Assert.Equal(expected, MoveRules.StopDistance(requested));
        }

        [Fact]
        public void Arrives_within_the_stop_distance()
        {
            Assert.True(MoveRules.Arrived(false, 1.0f, 1.0f));
            Assert.False(MoveRules.Arrived(false, 1.2f, 1.0f));
        }

        [Fact]
        public void Once_arrived_it_stays_arrived_within_the_slack()
        {
            Assert.True(MoveRules.Arrived(true, 1.4f, 1.0f));
        }

        [Fact]
        public void Arrival_ends_beyond_the_slack()
        {
            Assert.False(MoveRules.Arrived(true, 1.6f, 1.0f));
        }

        [Theory]
        [InlineData(false, 13f, true)]
        [InlineData(false, 12f, false)]
        [InlineData(false, 10f, false)]
        [InlineData(true, 10f, true)]
        [InlineData(true, 7.9f, false)]
        public void When_hurrying_it_sprints_far_jogs_near_and_keeps_its_gait_in_between(bool runningNow, float distance, bool expected)
        {
            Assert.Equal(expected, MoveRules.ShouldRun(runningNow, distance, true));
        }

        [Theory]
        [InlineData(false, 50f)]
        [InlineData(true, 50f)]
        public void Everyday_movement_never_sprints(bool runningNow, float distance)
        {
            // Sprinting is the player's 7 m/s burst; settlers jog like a player normally does unless fleeing.
            Assert.False(MoveRules.ShouldRun(runningNow, distance, false));
        }

        [Theory]
        [InlineData(true, 6f, true)]     // can't get closer, but close enough: deliver from here
        [InlineData(true, 9f, false)]    // too far to pretend: keep trying, say why
        [InlineData(false, 6f, false)]   // still making progress: keep walking
        public void A_settler_that_cant_get_closer_delivers_only_from_close_by(bool stuck, float distance, bool expected)
        {
            Assert.Equal(expected, MoveRules.DeliverFromHere(stuck, distance));
        }

        [Theory]
        [InlineData(true, 3.0f, 1.0f, true)]
        [InlineData(true, 3.5f, 1.0f, false)]
        [InlineData(false, 1.5f, 1.0f, false)]
        public void Reaching_the_end_of_a_path_counts_only_close_to_the_target(bool pathEnded, float distance, float stop, bool expected)
        {
            Assert.Equal(expected, MoveRules.ReachedPathEnd(pathEnded, distance, stop));
        }

        [Fact]
        public void A_chest_behind_a_wall_is_not_within_reach_from_outside()
        {
            Assert.False(MoveRules.InReach(wasArrived: false, distance: 1.35f, stopDistance: 2.5f, wallBetween: true));
            Assert.True(MoveRules.InReach(wasArrived: false, distance: 1.35f, stopDistance: 2.5f, wallBetween: false));
            Assert.True(MoveRules.InReach(wasArrived: true, distance: 2.8f, stopDistance: 2.5f, wallBetween: true));   // once there, it stays so
            Assert.False(MoveRules.InReach(wasArrived: false, distance: 4f, stopDistance: 2.5f, wallBetween: false));
        }
    }
}
