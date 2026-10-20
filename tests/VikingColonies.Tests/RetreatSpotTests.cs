using System;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class RetreatSpotTests
    {
        private static readonly GroundPoint Stump = new GroundPoint(0f, 0f);
        private static readonly GroundPoint North = new GroundPoint(0f, 1f);

        private static void Near(float x, float z, GroundPoint p)
        {
            Assert.True(Math.Abs(p.X - x) < 0.01f && Math.Abs(p.Z - z) < 0.01f, $"expected ({x}, {z}), got ({p.X}, {p.Z})");
        }

        [Fact]
        public void Behind_the_stump_and_to_the_settlers_side()
        {
            Near(2.83f, -2.83f, RetreatSpot.FromFall(Stump, North, new GroundPoint(1f, -1f), 4f));    // he stands south-east
            Near(-2.83f, -2.83f, RetreatSpot.FromFall(Stump, North, new GroundPoint(-1f, -1f), 4f));  // south-west
        }

        [Fact]
        public void On_the_fall_line_he_still_gets_a_side()
        {
            GroundPoint spot = RetreatSpot.FromFall(Stump, North, new GroundPoint(0f, -2f), 4f);
            Assert.Equal(4.0, (double)ColonyRules.Distance(Stump, spot), 2);
            Assert.True(spot.Z < 0f && Math.Abs(spot.X) > 2.8f);   // behind the stump and off the line
        }

        [Fact]
        public void A_longer_fall_direction_gives_the_same_spot()
        {
            Near(2.83f, -2.83f, RetreatSpot.FromFall(Stump, new GroundPoint(0f, 7f), new GroundPoint(1f, -1f), 4f));
        }

        [Fact]
        public void Without_a_fall_direction_he_steps_straight_away_from_the_stump()
        {
            Near(0f, -4f, RetreatSpot.FromFall(Stump, new GroundPoint(0f, 0f), new GroundPoint(0f, -2f), 4f));
            Near(4f, 0f, RetreatSpot.FromFall(Stump, new GroundPoint(0f, 0f), Stump, 4f));
        }

        // A log lying east-west from (-5, 0) to (5, 0), 0.5 m thick, rolling south.
        private static readonly GroundPoint West = new GroundPoint(-5f, 0f), East = new GroundPoint(5f, 0f), South = new GroundPoint(0f, -1f);

        [Fact]
        public void Out_of_a_rolling_logs_path_past_its_nearer_end()
        {
            Near(7f, -3f, RetreatSpot.OutOfPath(West, East, 0.5f, South, new GroundPoint(1f, -3f), 1.5f));
            Near(-7f, -3f, RetreatSpot.OutOfPath(West, East, 0.5f, South, new GroundPoint(-2f, -3f), 1.5f));
        }

        [Fact]
        public void Out_of_the_path_of_a_log_sliding_lengthwise()
        {
            Near(2f, -8f, RetreatSpot.OutOfPath(new GroundPoint(0f, -5f), new GroundPoint(0f, 5f), 0.5f, South, new GroundPoint(0.3f, -8f), 1.5f));
        }

        [Fact]
        public void A_log_moving_away_or_standing_still_leaves_him_be()
        {
            Near(8f, -3f, RetreatSpot.OutOfPath(West, East, 0.5f, South, new GroundPoint(8f, -3f), 1.5f));                 // beside its path
            Near(1f, 3f, RetreatSpot.OutOfPath(West, East, 0.5f, South, new GroundPoint(1f, 3f), 1.5f));                   // behind it
            Near(1f, -3f, RetreatSpot.OutOfPath(West, East, 0.5f, new GroundPoint(0f, 0f), new GroundPoint(1f, -3f), 1.5f)); // not moving
        }
    }
}
