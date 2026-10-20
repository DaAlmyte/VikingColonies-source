using System;
using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class GroundPlanTests
    {
        // An 8 m by 6 m site; cells of at most 3.54 m make it 3 by 2 operations.
        private static readonly Footprint Site = new Footprint(new Vec3(10, 0, 20), Quat.Identity, 4f, 3f);

        [Fact]
        public void Ground_already_at_its_target_needs_nothing()
        {
            Assert.Empty(GroundPlan.Plan(Site, 5f, (x, z) => 5f));
            Assert.Empty(GroundPlan.Plan(Site, 5f, (x, z) => 5.05f));   // within 10 cm counts as flat
        }

        [Fact]
        public void High_ground_is_dug_down_everywhere()
        {
            List<GroundOp> ops = GroundPlan.Plan(Site, 5f, (x, z) => 5.5f);
            Assert.Equal(6, ops.Count);
            Assert.All(ops, o => Assert.True(o.Dig && !o.Raise));
            Assert.All(ops, o => Assert.Equal(5f, o.Position.Y, 3));
            Assert.Equal(0f, ops.Sum(o => o.Fill), 3);
        }

        [Fact]
        public void Low_ground_is_raised_and_the_fill_is_measured()
        {
            List<GroundOp> ops = GroundPlan.Plan(Site, 5f, (x, z) => 4.5f);
            Assert.All(ops, o => Assert.True(o.Raise && !o.Dig));
            Assert.Equal(24f, ops.Sum(o => o.Fill), 1);   // 8 m × 6 m × 0.5 m
        }

        [Fact]
        public void A_slope_is_dug_on_its_high_side_and_raised_on_its_low_side()
        {
            List<GroundOp> ops = GroundPlan.Plan(Site, 5f, (x, z) => 5f + (x - 10f) * 0.5f);
            Assert.Contains(ops, o => o.Dig && o.Position.X > 10f);
            Assert.Contains(ops, o => o.Raise && o.Position.X < 10f);
        }

        [Fact]
        public void The_operations_cover_every_part_of_a_turned_site()
        {
            var site = new Footprint(new Vec3(-3, 0, 7), Quat.Yaw(33), 5.5f, 2.2f);
            List<GroundOp> ops = GroundPlan.Plan(site, 1f, (x, z) => 3f);
            for (float a = -1f; a <= 1f; a += 0.25f)
                for (float b = -1f; b <= 1f; b += 0.25f)
                {
                    Vec3 p = site.ToWorld(a * site.HalfX, b * site.HalfZ);
                    Assert.Contains(ops, o => Vec3.Distance(o.Position.Flat, p.Flat) <= o.Radius);
                }
            Assert.All(ops, o => Assert.True(site.Contains(o.Position)));
        }

        [Fact]
        public void Only_ground_more_than_half_a_metre_too_high_needs_a_pickaxe()
        {
            List<GroundOp> shallow = GroundPlan.Plan(Site, 5f, (x, z) => 5.3f);
            Assert.All(shallow, o => Assert.True(o.Dig && !o.DeepDig));   // the hoe levels it
            List<GroundOp> deep = GroundPlan.Plan(Site, 5f, (x, z) => 5.8f);
            Assert.All(deep, o => Assert.True(o.Dig && o.DeepDig));
        }

        [Fact]
        public void Spots_whose_work_would_reach_another_building_are_left_alone()
        {
            Assert.Equal(6, GroundPlan.Plan(Site, 5f, (x, z) => 5.5f).Count);
            List<GroundOp> ops = GroundPlan.Plan(Site, 5f, (x, z) => 5.5f, keepOff: (x, z, radius) => x + radius > 13.5f);
            Assert.NotEmpty(ops);
            Assert.True(ops.Count < 6);
            Assert.All(ops, o => Assert.True(o.Position.X + o.Radius <= 13.5f));
        }

        [Fact]
        public void No_operation_is_wider_than_the_level_operation_the_mod_registers()
        {
            foreach (float half in new[] { 0.5f, 1.3f, 3f, 4.4f, 7.9f, 20f, 50f })
                Assert.All(GroundPlan.Plan(new Footprint(Vec3.Zero, Quat.Yaw(20), half, half * 0.6f), 0f, (x, z) => 1f),
                    o => Assert.True(o.Radius <= GroundPlan.LevelRadius, $"radius {o.Radius} for a site of {half}"));
        }

        [Fact]
        public void Stone_is_charged_for_the_fill_at_the_games_rate()
        {
            List<GroundOp> ops = GroundPlan.Plan(Site, 5f, (x, z) => 4.5f);
            Assert.Equal(12, GroundPlan.Stone(ops, 0.5f));
            Assert.Equal(0, GroundPlan.Stone(GroundPlan.Plan(Site, 5f, (x, z) => 5.5f), 0.5f));
            Assert.Equal(0, GroundPlan.Stone(ops, 0f));   // a game where raising ground is free
            Assert.Equal(1, GroundPlan.Stone(new[] { new GroundOp(Vec3.Zero, 1f, false, true, 0.01f) }, 0.5f));
        }

        [Fact]
        public void The_range_finds_the_lowest_and_highest_ground()
        {
            GroundPlan.Range(Site, (x, z) => z - 20f, out float lowest, out float highest);
            Assert.InRange(lowest, -3f, -2f);
            Assert.InRange(highest, 2f, 3f);
        }

        [Fact]
        public void A_very_large_design_is_never_sampled_thousands_of_times()
        {
            foreach (float half in new[] { 20f, 50f })
            {
                var site = new Footprint(Vec3.Zero, Quat.Yaw(10), half, half);
                int calls = 0;
                GroundPlan.Plan(site, 0f, (x, z) => { calls++; return 1f; });
                Assert.InRange(calls, 1, (int)(GroundPlan.MaxSamples * 1.3f));
                calls = 0;
                GroundPlan.Range(site, (x, z) => { calls++; return 1f; }, out _, out _);
                Assert.InRange(calls, 1, GroundPlan.MaxSamples);
            }
        }

        [Fact]
        public void The_edges_are_smoothed_just_outside_the_site()
        {
            List<Vec3> spots = GroundPlan.SmoothSpots(Site, 3f);
            Assert.True(spots.Count >= 8);
            Assert.All(spots, s => Assert.False(Site.Contains(s)));
            Assert.All(spots, s => Assert.True(Site.Grow(GroundPlan.SmoothOutside + 0.1f).Contains(s)));
        }
    }
}
