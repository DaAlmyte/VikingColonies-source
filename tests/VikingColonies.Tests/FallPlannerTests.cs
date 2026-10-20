using System;
using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class FallPlannerTests
    {
        private const float Length = 20f;
        private const float Clearance = 3f;
        private static readonly GroundPoint Tree = new GroundPoint(0f, 0f);
        private static readonly GroundPoint East = new GroundPoint(1f, 0f);

        private static float DistanceToTrunk(GroundPoint building, GroundPoint dir)
        {
            // distance from building to the segment Tree -> Tree + dir * Length
            float t = Math.Max(0f, Math.Min(Length, building.X * dir.X + building.Z * dir.Z));
            float dx = building.X - dir.X * t, dz = building.Z - dir.Z * t;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        [Fact]
        public void With_no_buildings_the_tree_falls_the_preferred_way()
        {
            GroundPoint? dir = FallPlanner.ChooseDirection(Tree, new List<GroundPoint>(), East, Length, Clearance);
            Assert.NotNull(dir);
            Assert.Equal(1f, dir.Value.X, 3);
            Assert.Equal(0f, dir.Value.Z, 3);
        }

        // Review I4: a felled tree must not land on the player's buildings.
        [Fact]
        public void A_building_in_the_fall_line_makes_it_fall_another_way()
        {
            var wall = new GroundPoint(10f, 0f);
            GroundPoint? dir = FallPlanner.ChooseDirection(Tree, new List<GroundPoint> { wall }, East, Length, Clearance);
            Assert.NotNull(dir);
            Assert.True(DistanceToTrunk(wall, dir.Value) >= Clearance);
        }

        [Fact]
        public void A_building_behind_the_tree_does_not_change_the_direction()
        {
            GroundPoint? dir = FallPlanner.ChooseDirection(Tree, new List<GroundPoint> { new GroundPoint(-8f, 0f) }, East, Length, Clearance);
            Assert.NotNull(dir);
            Assert.Equal(1f, dir.Value.X, 3);
        }

        [Fact]
        public void Buildings_all_around_mean_the_tree_is_left_alone()
        {
            var ring = new List<GroundPoint>();
            for (int i = 0; i < 12; i++)
            {
                double a = i * Math.PI / 6;
                ring.Add(new GroundPoint((float)(2 * Math.Cos(a)), (float)(2 * Math.Sin(a))));
            }
            Assert.Null(FallPlanner.ChooseDirection(Tree, ring, East, Length, Clearance));
        }

        [Fact]
        public void The_result_is_a_unit_direction()
        {
            GroundPoint? dir = FallPlanner.ChooseDirection(Tree, new List<GroundPoint> { new GroundPoint(10f, 1f) }, new GroundPoint(3f, 4f), Length, Clearance);
            Assert.NotNull(dir);
            Assert.Equal(1f, (float)Math.Sqrt(dir.Value.X * dir.Value.X + dir.Value.Z * dir.Value.Z), 3);
        }
    }
}
