using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class FootprintTests
    {
        private static readonly List<Vec3> Corners = new List<Vec3> { new Vec3(-3, 0, -3), new Vec3(3, 0, 3), new Vec3(3, 2, -3) };

        [Fact]
        public void The_footprint_covers_every_piece_plus_a_margin()
        {
            Footprint f = Footprint.Around(Corners, Vec3.Zero, Quat.Identity, 1f);
            Assert.Equal(4f, f.HalfX, 3);
            Assert.Equal(4f, f.HalfZ, 3);
            Assert.True(f.Contains(new Vec3(3.9f, 0, -3.9f)));
            Assert.False(f.Contains(new Vec3(4.1f, 0, 0)));
        }

        [Fact]
        public void A_turned_building_has_a_turned_footprint()
        {
            Quat turn = Quat.Yaw(45);
            var points = Corners.Select(p => turn.Rotate(p)).ToList();
            Footprint f = Footprint.Around(points, Vec3.Zero, turn, 0f);
            Assert.True(f.Contains(turn.Rotate(new Vec3(2.9f, 0, 2.9f))));
            Assert.False(f.Contains(new Vec3(3.5f, 0, 3.5f)));
        }

        [Fact]
        public void Standing_spots_go_all_the_way_round_outside_the_footprint()
        {
            Footprint f = Footprint.Around(Corners, Vec3.Zero, Quat.Identity, 1f);
            List<Vec3> spots = StandSpots.Ring(f, 2f, 2f);
            Assert.True(spots.Count >= 12);
            Assert.All(spots, s => Assert.False(f.Contains(s)));
            Assert.Contains(spots, s => s.X > 5.9f);
            Assert.Contains(spots, s => s.X < -5.9f);
            Assert.Contains(spots, s => s.Z > 5.9f);
            Assert.Contains(spots, s => s.Z < -5.9f);
        }

        [Fact]
        public void He_picks_the_spot_nearest_himself_that_reaches_the_piece()
        {
            var spots = new List<Vec3> { new Vec3(-6, 0, 0), new Vec3(6, 0, 0), new Vec3(40, 0, 0) };
            var piece = new Vec3(3, 2, 0);
            Assert.Equal(1, StandSpots.Choose(spots, piece, 15f, new Vec3(10, 0, 0), i => true));
            Assert.Equal(0, StandSpots.Choose(spots, piece, 15f, new Vec3(-10, 0, 0), i => true));
            Assert.Equal(0, StandSpots.Choose(spots, piece, 15f, new Vec3(10, 0, 0), i => i != 1));
            Assert.Equal(-1, StandSpots.Choose(spots, piece, 2f, new Vec3(10, 0, 0), i => true));
        }

        [Fact]
        public void The_build_log_tells_the_first_ten_in_detail_then_every_fiftieth()
        {
            var tally = new BuildTally();
            for (int i = 1; i <= 10; i++) Assert.Equal(SwingLogKind.Detail, tally.Record(BuildOutcome.Placed));
            for (int i = 11; i < 50; i++) Assert.Equal(SwingLogKind.None, tally.Record(i % 7 == 0 ? BuildOutcome.Fell : BuildOutcome.Placed));
            Assert.Equal(SwingLogKind.Summary, tally.Record(BuildOutcome.Placed));
            Assert.Equal(50, tally.Placed + tally.Fell);
        }

        [Fact]
        public void A_footprint_has_four_corners_around_its_centre()
        {
            var f = new Footprint(new Vec3(1, 0, 1), Quat.Yaw(90), 2f, 1f);
            Vec3[] corners = f.Corners();
            Assert.Equal(4, corners.Length);
            foreach (Vec3 corner in corners) Assert.Equal(2.236f, Vec3.Distance(corner, f.Centre), 2);
        }

        [Fact]
        public void A_grown_footprint_is_wider_on_every_side()
        {
            Footprint f = new Footprint(Vec3.Zero, Quat.Yaw(30), 2f, 1f).Grow(0.5f);
            Assert.Equal(2.5f, f.HalfX, 3);
            Assert.Equal(1.5f, f.HalfZ, 3);
            Assert.Equal(0f, Quat.Angle(Quat.Yaw(30), f.Rotation), 1);   // still turned like before
        }

        [Fact]
        public void Footprints_that_share_ground_overlap_and_ones_apart_dont()
        {
            var a = new Footprint(Vec3.Zero, Quat.Identity, 3f, 2f);
            Assert.True(a.Overlaps(new Footprint(new Vec3(5, 0, 0), Quat.Identity, 2.5f, 2f)));
            Assert.True(new Footprint(new Vec3(5, 0, 0), Quat.Identity, 2.5f, 2f).Overlaps(a));
            Assert.False(a.Overlaps(new Footprint(new Vec3(6, 0, 0), Quat.Identity, 2.5f, 2f)));
        }

        [Fact]
        public void Turned_footprints_overlap_only_when_they_really_share_ground()
        {
            var upright = new Footprint(Vec3.Zero, Quat.Identity, 2f, 2f);
            // Turned 45°, its corner points at the upright one: their boxes seen from above overlap, the footprints don't.
            Assert.False(upright.Overlaps(new Footprint(new Vec3(4.6f, 0, 4.6f), Quat.Yaw(45), 2f, 2f)));
            Assert.True(upright.Overlaps(new Footprint(new Vec3(3.2f, 0, 3.2f), Quat.Yaw(45), 2f, 2f)));
        }

        [Fact]
        public void Grid_points_cover_a_footprint_up_to_its_corners()
        {
            var f = new Footprint(new Vec3(5, 0, 5), Quat.Yaw(30), 4f, 2.5f);
            List<Vec3> grid = f.GridPoints(2f);
            foreach (Vec3 corner in f.Corners()) Assert.Contains(grid, p => Vec3.Distance(p, corner) < 0.01f);
            Assert.All(grid, p => Assert.True(f.Grow(0.01f).Contains(p)));
        }

        [Fact]
        public void Every_part_of_a_large_footprint_is_near_a_grid_point()
        {
            var f = new Footprint(Vec3.Zero, Quat.Yaw(-15), 12f, 9f);
            List<Vec3> grid = f.GridPoints(2f);
            for (float a = -1f; a <= 1f; a += 0.05f)
                for (float b = -1f; b <= 1f; b += 0.05f)
                {
                    Vec3 p = f.ToWorld(a * f.HalfX, b * f.HalfZ);
                    Assert.Contains(grid, g => Vec3.Distance(g, p) <= 1.42f);
                }
        }
    }
}
