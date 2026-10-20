using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class BlueprintPlanTests
    {
        private static BlueprintFile Hut(Vec3 chestAt, Quat chestTurn) => new BlueprintFile
        {
            Pieces =
            {
                new BlueprintPiece("wood_floor", "Building", chestAt + chestTurn.Rotate(new Vec3(2, 0, 0)), chestTurn, new Vec3(1, 1, 1), ""),
                new BlueprintPiece("vc_hut_lumberjack", "Misc", chestAt, chestTurn, new Vec3(1, 1, 1), ""),
                new BlueprintPiece("woodwall", "Building", chestAt + chestTurn.Rotate(new Vec3(0, 0, 3)), chestTurn * Quat.Yaw(90), new Vec3(1, 1, 1), ""),
            },
        };

        private static void Near(Vec3 expected, Vec3 actual)
        {
            Assert.Equal(expected.X, actual.X, 3);
            Assert.Equal(expected.Y, actual.Y, 3);
            Assert.Equal(expected.Z, actual.Z, 3);
        }

        [Fact]
        public void Pieces_keep_their_place_relative_to_the_chest()
        {
            Assert.True(BlueprintPlan.TryPlace(Hut(Vec3.Zero, Quat.Identity), "vc_hut_lumberjack", new Vec3(100, 30, 50), Quat.Identity, out List<PlannedPiece> plan, out _));
            Assert.Equal(new[] { "wood_floor", "woodwall" }, plan.Select(p => p.Prefab));
            Assert.Equal(new[] { 0, 1 }, plan.Select(p => p.Index));
            Near(new Vec3(102, 30, 50), plan[0].Position);
            Near(new Vec3(100, 30, 53), plan[1].Position);
        }

        [Fact]
        public void The_building_turns_with_the_chest_even_when_the_file_turned_and_moved_the_chest()
        {
            // In the file the chest stands at (5, 1, -4) turned 30°; in the world it stands at the origin turned 22.5°.
            BlueprintFile file = Hut(new Vec3(5, 1, -4), Quat.Yaw(30));
            Assert.True(BlueprintPlan.TryPlace(file, "vc_hut_lumberjack", Vec3.Zero, Quat.Yaw(22.5f), out List<PlannedPiece> plan, out _));
            Near(Quat.Yaw(22.5f).Rotate(new Vec3(2, 0, 0)), plan[0].Position);
            Near(Quat.Yaw(22.5f).Rotate(new Vec3(0, 0, 3)), plan[1].Position);
            Assert.Equal(90f, Quat.Angle(Quat.Yaw(22.5f), plan[1].Rotation), 1);
        }

        [Fact]
        public void A_blueprint_without_the_chest_line_is_refused()
        {
            var file = new BlueprintFile { Pieces = { new BlueprintPiece("wood_floor", "", Vec3.Zero, Quat.Identity, new Vec3(1, 1, 1), "") } };
            Assert.False(BlueprintPlan.TryPlace(file, "vc_hut_lumberjack", Vec3.Zero, Quat.Identity, out _, out string problem));
            Assert.Contains("vc_hut_lumberjack", problem);
        }

        [Fact]
        public void Colony_buildings_inside_a_blueprint_are_never_built()
        {
            BlueprintFile file = Hut(Vec3.Zero, Quat.Identity);
            file.Pieces.Add(new BlueprintPiece("vc_colony_hall", "", new Vec3(4, 0, 4), Quat.Identity, new Vec3(1, 1, 1), ""));
            BlueprintPlan.TryPlace(file, "vc_hut_lumberjack", Vec3.Zero, Quat.Identity, out List<PlannedPiece> plan, out _);
            Assert.DoesNotContain(plan, p => p.Prefab.StartsWith("vc_"));
        }

        [Fact]
        public void A_saved_plan_reads_back_the_same()
        {
            BlueprintPlan.TryPlace(Hut(Vec3.Zero, Quat.Identity), "vc_hut_lumberjack", new Vec3(1.5f, 2, 3), Quat.Yaw(45), out List<PlannedPiece> plan, out _);
            List<PlannedPiece> copy = PlanCodec.Decode(PlanCodec.Encode(plan));
            Assert.Equal(plan.Count, copy.Count);
            Near(plan[1].Position, copy[1].Position);
            Assert.Equal(0f, Quat.Angle(plan[1].Rotation, copy[1].Rotation), 2);
        }

        [Fact]
        public void A_damaged_plan_line_keeps_the_other_pieces_numbers()
        {
            List<PlannedPiece> plan = PlanCodec.Decode("wood_floor;0;0;0;0;0;0;1\ngarbage\nwoodwall;1;0;0;0;0;0;1");
            Assert.Equal(3, plan.Count);
            Assert.False(plan[1].IsValid);
            Assert.Equal(2, plan[2].Index);
            Assert.Equal("woodwall", plan[2].Prefab);
        }
    }
}
