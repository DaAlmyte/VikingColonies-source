using System;
using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// Worker polish (0.20): fields the player marks with a field marker, as MineColonies' farmer works fields marked with
    /// a scarecrow: a square of plots round the marker, 3 to 9 plots a side.
    /// </summary>
    public class FieldGridTests
    {
        private static double Flat(Vec3 a, Vec3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

        [Fact]
        public void A_5x5_field_has_24_plots_round_its_marker()
        {
            var centre = new Vec3(10f, 0f, 20f);
            List<Vec3> plots = FieldGrid.Plots(centre, Quat.Yaw(30f), 5, FieldPlots.Spacing);
            Assert.Equal(24, plots.Count);
            Assert.DoesNotContain(plots, p => Flat(p, centre) < 0.1);
            foreach (Vec3 p in plots)
            {
                double nearest = plots.Where(q => Flat(q, p) > 0.01).Min(q => Flat(q, p));
                Assert.InRange(nearest, FieldPlots.Spacing - 0.01, FieldPlots.Spacing + 0.01);
            }
        }

        [Fact]
        public void Every_size_has_its_plots()
        {
            Assert.Equal(8, FieldGrid.Plots(new Vec3(0, 0, 0), Quat.Identity, 3, FieldPlots.Spacing).Count);
            Assert.Equal(48, FieldGrid.Plots(new Vec3(0, 0, 0), Quat.Identity, 7, FieldPlots.Spacing).Count);
            Assert.Equal(80, FieldGrid.Plots(new Vec3(0, 0, 0), Quat.Identity, 9, FieldPlots.Spacing).Count);
            Assert.Equal(120, FieldGrid.Plots(new Vec3(0, 0, 0), Quat.Identity, 11, FieldPlots.Spacing).Count);
        }

        [Fact]
        public void Sizes_go_3_5_7_9_11_and_snap()
        {
            // 0.20.1: up to 11x11, MineColonies' largest field.
            Assert.Equal(new[] { 3, 5, 7, 9, 11 }, FieldSizes.All);
            Assert.Equal(5, FieldSizes.New);
            Assert.Equal(7, FieldSizes.Bigger(5));
            Assert.Equal(11, FieldSizes.Bigger(9));
            Assert.Equal(11, FieldSizes.Bigger(11));
            Assert.Equal(3, FieldSizes.Smaller(3));
            Assert.Equal(5, FieldSizes.Smaller(7));
            Assert.Equal(5, FieldSizes.Valid(4));
            Assert.Equal(5, FieldSizes.Valid(0));
            Assert.Equal(11, FieldSizes.Valid(11));
            Assert.Equal(11, FieldSizes.Valid(13));
            Assert.Equal(3, FieldSizes.Valid(3));
        }

        [Fact]
        public void Overlapping_plots_keep_the_first_owner()
        {
            // The Farm's ground: a 5x5 square at the origin; a field 5x5 two columns over, overlapping by three columns.
            List<PlotRule> ground = FieldGrid.Plots(new Vec3(0, 0, 0), Quat.Identity, 5, FieldPlots.Spacing)
                .Select(p => new PlotRule { At = p, Crop = "", FieldId = "" }).ToList();
            List<PlotRule> field = FieldGrid.Plots(new Vec3(2 * FieldPlots.Spacing, 0, 0), Quat.Identity, 5, FieldPlots.Spacing)
                .Select(p => new PlotRule { At = p, Crop = "sapling_carrot", FieldId = "f1" }).ToList();
            List<PlotRule> merged = FieldPlots.Merge(ground, field);
            Assert.Equal(24, merged.Count(r => r.FieldId == ""));
            for (int i = 0; i < merged.Count; i++)
                for (int j = i + 1; j < merged.Count; j++)
                    Assert.True(Flat(merged[i].At, merged[j].At) > 0.8, $"two plots on one spot: {merged[i].At} and {merged[j].At}");
            Assert.Contains(merged, r => r.FieldId == "f1");
        }

        [Fact]
        public void Corners_lie_half_a_plot_outside()
        {
            Vec3[] corners = FieldGrid.Corners(new Vec3(0, 0, 0), Quat.Identity, 3, 1f);
            Assert.Equal(4, corners.Length);
            foreach (Vec3 c in corners)
            {
                Assert.Equal(1.5f, Math.Abs(c.X), 3);
                Assert.Equal(1.5f, Math.Abs(c.Z), 3);
            }
        }
    }
}
