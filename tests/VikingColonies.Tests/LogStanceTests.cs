using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class LogStanceTests
    {
        private static readonly GroundPoint A = new GroundPoint(0, 0);
        private static readonly GroundPoint B = new GroundPoint(10, 0);

        [Fact]
        public void The_nearest_axis_point_is_clamped_to_the_ends()
        {
            Assert.Equal(0f, LogStance.AxisParameter(A, B, new GroundPoint(-5, 3)));
            Assert.Equal(1f, LogStance.AxisParameter(A, B, new GroundPoint(15, 0)));
            Assert.Equal(0.5f, LogStance.AxisParameter(A, B, new GroundPoint(5, 7)), 3);
        }

        [Fact]
        public void Stand_spots_are_beside_the_log_at_radius_plus_stand_off()
        {
            LogStance.Candidates(A, B, 0.4f, new GroundPoint(3, 5), 1.2f, out GroundPoint first, out GroundPoint second);
            Assert.Equal(3f, first.X, 3);
            Assert.Equal(1.6f, first.Z, 3);
            Assert.Equal(3f, second.X, 3);
            Assert.Equal(-1.6f, second.Z, 3);
        }

        [Fact]
        public void The_spot_on_higher_ground_is_chosen()
        {
            var first = new GroundPoint(3, 1.6f);
            var second = new GroundPoint(3, -1.6f);
            var settler = new GroundPoint(3, -5);
            Assert.Equal(first.Z, LogStance.Choose(first, 10f, second, 9f, settler).Z);
            Assert.Equal(second.Z, LogStance.Choose(first, 9f, second, 10f, settler).Z);
        }

        [Fact]
        public void On_level_ground_the_spot_nearer_the_settler_is_chosen()
        {
            var first = new GroundPoint(3, 1.6f);
            var second = new GroundPoint(3, -1.6f);
            Assert.Equal(second.Z, LogStance.Choose(first, 10f, second, 10.2f, new GroundPoint(3, -5)).Z);
        }

        [Fact]
        public void A_zero_length_axis_puts_spots_on_the_settlers_side_and_opposite()
        {
            LogStance.Candidates(A, A, 0.3f, new GroundPoint(0, 5), 1.2f, out GroundPoint first, out GroundPoint second);
            Assert.Equal(1.5f, first.Z, 3);
            Assert.Equal(-1.5f, second.Z, 3);
            Assert.Equal(0f, first.X, 3);
        }

        [Fact]
        public void Distance_to_the_surface_subtracts_the_radius_and_never_goes_negative()
        {
            Assert.Equal(1.6f, LogStance.DistanceToSurface(A, B, 0.4f, new GroundPoint(3, 2)), 3);
            Assert.Equal(0f, LogStance.DistanceToSurface(A, B, 0.4f, new GroundPoint(3, 0.1f)));
        }

        [Theory]
        [InlineData(2.2f, 1.9f)]
        [InlineData(0f, 1.8f)]
        [InlineData(0.9f, 0.8f)]
        public void Reach_comes_from_the_axe(float attackRange, float expected)
        {
            Assert.Equal(expected, LogStance.ReachFor(attackRange), 3);
        }

        [Theory]
        [InlineData(2.5f, 1.2f)]
        [InlineData(1.9f, 1.0f)]
        [InlineData(1.2f, 0.3f)]
        public void The_stand_off_leaves_room_for_where_walking_stops(float reach, float expected)
        {
            Assert.Equal(expected, LogStance.StandOff(reach), 3);
        }

        [Theory]
        [InlineData(1.9f)]
        [InlineData(1.5f)]
        [InlineData(1.2f)]
        public void Stopping_anywhere_near_the_stand_spot_is_within_reach(float reach)
        {
            // Walking counts as arrived up to MinStopDistance from the spot; from there the surface must still be in reach.
            Assert.True(LogStance.StandOff(reach) + MoveRules.MinStopDistance <= reach);
        }

        [Theory]
        [InlineData(1.0f, 1.9f, true)]
        [InlineData(1.9f, 1.9f, true)]
        [InlineData(2.7f, 1.9f, false)]   // the old 1.5 m tolerance left him here: out of reach
        [InlineData(0f, 1.9f, false)]     // inside the log's radius: under it
        [InlineData(0.05f, 1.9f, false)]
        public void In_place_only_when_the_axe_reaches_and_he_is_not_under_it(float surfaceDistance, float reach, bool expected)
        {
            Assert.Equal(expected, LogStance.InPlace(surfaceDistance, reach));
        }
    }
}
