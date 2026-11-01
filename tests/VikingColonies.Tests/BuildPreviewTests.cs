using System;
using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>vc_preview (the build tools, 2026-10-25): a design's pictures from set angles and its load, in a minute.</summary>
    public class BuildPreviewTests
    {
        private static BlueprintPiece Piece(string name, Vec3 at, Quat turn) => new BlueprintPiece(name, "Building", at, turn, new Vec3(1, 1, 1), "");

        private static void Near(Vec3 expected, Vec3 actual)
        {
            Assert.Equal(expected.X, actual.X, 3);
            Assert.Equal(expected.Y, actual.Y, 3);
            Assert.Equal(expected.Z, actual.Z, 3);
        }

        [Fact]
        public void A_design_is_laid_out_round_its_own_origin_without_a_chest()
        {
            var file = new BlueprintFile
            {
                Pieces =
                {
                    Piece("wood_floor", new Vec3(2, 0, 0), Quat.Identity),
                    Piece("vc_hut_builder", new Vec3(5, 0, 5), Quat.Identity),   // a hut's chest line: left out
                    Piece("woodwall", new Vec3(0, 1, 3), Quat.Yaw(90)),
                },
            };
            List<PlannedPiece> plan = BlueprintPlan.PlaceAround(file, new Vec3(100, 40, 50), Quat.Yaw(90));
            Assert.Equal(new[] { "wood_floor", "woodwall" }, plan.Select(p => p.Prefab));
            Assert.Equal(new[] { 0, 1 }, plan.Select(p => p.Index));
            Near(new Vec3(100, 40, 48), plan[0].Position);          // (2, 0, 0) turned a quarter to the east: (0, 0, -2)
            Near(new Vec3(103, 41, 50), plan[1].Position);
            Assert.Equal(180f, Quat.Angle(Quat.Identity, plan[1].Rotation), 1);
        }

        [Fact]
        public void The_mods_own_pieces_are_put_up_and_only_a_huts_chest_or_the_town_hall_left_out()
        {
            var file = new BlueprintFile
            {
                Pieces =
                {
                    Piece("vc_scarecrow", new Vec3(0, 0, 0), Quat.Identity),        // the mod's own piece: put up (the scarecrow's preview had none)
                    Piece("vc_field_marker", new Vec3(1, 0, 0), Quat.Identity),
                    Piece("vc_hut_farm", new Vec3(2, 0, 0), Quat.Identity),
                    Piece("vc_colony_hall", new Vec3(3, 0, 0), Quat.Identity),
                    Piece("wood_pole2", new Vec3(4, 1, 0), Quat.Identity),
                },
            };
            List<PlannedPiece> plan = BlueprintPlan.PlaceAround(file, Vec3.Zero, Quat.Identity);
            Assert.Equal(new[] { "vc_scarecrow", "vc_field_marker", "wood_pole2" }, plan.Select(p => p.Prefab));
        }

        [Fact]
        public void An_empty_or_missing_design_is_an_empty_plan()
        {
            Assert.Empty(BlueprintPlan.PlaceAround(null, Vec3.Zero, Quat.Identity));
            Assert.Empty(BlueprintPlan.PlaceAround(new BlueprintFile(), Vec3.Zero, Quat.Identity));
        }

        [Theory]
        [InlineData("great_hall", true)]
        [InlineData("Hall-2", true)]
        [InlineData("", false)]
        [InlineData("../evil", false)]
        [InlineData("a b", false)]
        [InlineData("C:", false)]
        public void Only_plain_names_are_read(string name, bool safe) => Assert.Equal(safe, PreviewName.IsSafe(name));

        [Fact]
        public void A_name_its_pictures_can_hold_whole()
        {
            Assert.True(PreviewName.IsSafe(new string('a', PreviewName.Longest)));
            Assert.False(PreviewName.IsSafe(new string('a', PreviewName.Longest + 1)));
            Assert.True(("p-" + new string('a', PreviewName.Longest) + "-load-frontright").Length <= ShotFile.MaxLabel);
        }

        [Fact]
        public void The_views_are_the_set_angles_in_order()
        {
            List<PreviewView> views = PreviewViews.For(new Vec3(-5, 0, -40), new Vec3(5, 12, 40), 65f, 16f / 9f);
            Assert.Equal(new[] { "front", "back", "left", "right", "frontright", "backleft", "above", "inside" }, views.Select(v => v.Name));
            Assert.Equal(new[] { "frontright", "backleft", "above", "inside" }, PreviewViews.LoadViews);
        }

        [Fact]
        public void Each_outside_view_sees_the_whole_design_and_stands_outside_it()
        {
            var min = new Vec3(-7, 0, -40);
            var max = new Vec3(7, 13, 40);
            const float fov = 65f, aspect = 16f / 9f;
            foreach (PreviewView v in PreviewViews.For(min, max, fov, aspect).Where(v => v.Name != "inside"))
            {
                Assert.False(v.Eye.X > min.X && v.Eye.X < max.X && v.Eye.Y > min.Y && v.Eye.Y < max.Y && v.Eye.Z > min.Z && v.Eye.Z < max.Z, v.Name);
                foreach (Vec3 corner in Corners(min, max))
                    Assert.True(OnScreen(v, corner, fov, aspect), $"{v.Name} misses {corner}");
            }
        }

        [Fact]
        public void The_front_view_comes_close_to_a_long_hall_and_the_side_view_stands_back()
        {
            List<PreviewView> views = PreviewViews.For(new Vec3(-7, 0, -40), new Vec3(7, 13, 40), 65f, 16f / 9f);
            PreviewView front = views.First(v => v.Name == "front"), left = views.First(v => v.Name == "left");
            Assert.True(front.Eye.Z > 40 && front.Eye.Z < 40 + 40, $"front at {front.Eye}");   // its gable fills the picture
            Assert.True(left.Eye.X < -7 - 20, $"left at {left.Eye}");                        // the whole 80 m side in view
            Assert.True(front.Eye.Y > 0);
        }

        [Fact]
        public void The_inside_view_stands_at_eye_height_looking_down_the_long_way()
        {
            PreviewView along = PreviewViews.For(new Vec3(-7, 0, -40), new Vec3(7, 13, 40), 65f, 1.7f).First(v => v.Name == "inside");
            Near(new Vec3(0, 1.7f, 24), along.Eye);
            Assert.True(along.Look.Z < along.Eye.Z && Math.Abs(along.Look.X) < 0.01f);
            PreviewView across = PreviewViews.For(new Vec3(-40, -0.5f, -7), new Vec3(40, 13, 7), 65f, 1.7f).First(v => v.Name == "inside");
            Near(new Vec3(24, 1.7f, 0), across.Eye);
            Assert.True(across.Look.X < across.Eye.X);
        }

        [Fact]
        public void A_hut_s_inside_view_stands_in_front_of_its_chest_looking_at_it()
        {
            // A farm with its field and windmill beside it: the whole box's middle is out in the field, its far end a wall.
            var chest = new Vec3(-2, 0, -6);
            PreviewView inside = PreviewViews.For(new Vec3(-18, 0, -12), new Vec3(10, 9, 8), 65f, 1.7f, chest).First(v => v.Name == "inside");
            Near(new Vec3(-2, 1.7f, -1.5f), inside.Eye);
            Assert.True(inside.Look.Z < chest.Z && Math.Abs(inside.Look.X - chest.X) < 0.01f);
            // A small hut: no further than most of the way to its front.
            PreviewView small = PreviewViews.For(new Vec3(-2, 0, -1), new Vec3(2, 4, 3), 65f, 1.7f, new Vec3(0, 0, 0)).First(v => v.Name == "inside");
            Assert.True(small.Eye.Z > 0.5f && small.Eye.Z < 3f);
            // A chest at the design's front edge: never inside the chest; one on an upper floor: the eye on that floor.
            PreviewView edge = PreviewViews.For(new Vec3(-2, 0, -4), new Vec3(2, 4, 0), 65f, 1.7f, new Vec3(0, 0, 0)).First(v => v.Name == "inside");
            Assert.True(edge.Eye.Z >= PreviewViews.ChestViewMin - 0.01f);
            PreviewView upstairs = PreviewViews.For(new Vec3(-4, 0, -4), new Vec3(4, 8, 4), 65f, 1.7f, new Vec3(0, 3, -2)).First(v => v.Name == "inside");
            Assert.Equal(3f + PreviewViews.EyeHeight, upstairs.Eye.Y, 2);
        }

        [Fact]
        public void Support_has_settled_when_nothing_changes_for_a_while()
        {
            var settle = new PreviewSettle();
            settle.Add(0f, 100, 5000f);
            settle.Add(1f, 98, 4800f);
            settle.Add(2f, 98, 4700f);
            Assert.False(settle.Settled);
            for (int s = 3; s <= 6; s++) settle.Add(s, 98, 4700.4f);
            Assert.True(settle.Settled);
            Assert.False(settle.GaveUp);
        }

        [Fact]
        public void It_waits_five_seconds_at_least_and_gives_up_after_two_minutes()
        {
            var early = new PreviewSettle();
            for (int s = 0; s <= 4; s++) early.Add(s, 10, 100f);
            Assert.False(early.Settled);
            var restless = new PreviewSettle();
            for (int s = 0; s <= 121; s++) restless.Add(s, 10, s);
            Assert.True(restless.Settled);
            Assert.True(restless.GaveUp);
        }

        [Fact]
        public void A_load_line_is_tab_separated_with_a_dot_for_decimals()
        {
            string line = PreviewLoad.Line(3, "wood_beam", new Vec3(1.25f, 2, -3.5f), true, 54.321f, 10f, 100f, 0.5432f);
            Assert.Equal("3\twood_beam\t1.25\t2\t-3.5\t1\t54.3\t10\t100\t0.543", line);
            Assert.Equal(10, PreviewLoad.Header.Split('\t').Length);
            Assert.Equal("7\tfire_pit\t0\t0\t0\t0\t\t\t\t", PreviewLoad.Line(7, "fire_pit", Vec3.Zero, false, null, null, null, null));
        }

        private static IEnumerable<Vec3> Corners(Vec3 a, Vec3 b)
        {
            foreach (float x in new[] { a.X, b.X })
            foreach (float y in new[] { a.Y, b.Y })
            foreach (float z in new[] { a.Z, b.Z })
                yield return new Vec3(x, y, z);
        }

        private static bool OnScreen(PreviewView v, Vec3 p, float fov, float aspect)
        {
            Vec3 forward = (v.Look - v.Eye).Normalized;
            Vec3 right = Cross(forward, new Vec3(0, 1, 0)).Normalized;
            Vec3 up = Cross(right, forward);
            Vec3 d = p - v.Eye;
            float depth = Vec3.Dot(d, forward);
            if (depth <= 0) return false;
            double tanV = Math.Tan(fov * Math.PI / 360.0), tanH = tanV * aspect;
            return Math.Abs(Vec3.Dot(d, right)) / depth <= tanH + 1e-3 && Math.Abs(Vec3.Dot(d, up)) / depth <= tanV + 1e-3;
        }

        private static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    }
}
