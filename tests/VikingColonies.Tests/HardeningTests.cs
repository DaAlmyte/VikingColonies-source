using System;
using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>0.25: start-up that survives one failing part (review of 0.20, bug 1).</summary>
    public class StartupStepsTests
    {
        [Fact]
        public void One_failing_step_doesnt_stop_the_rest()
        {
            var ran = new List<string>();
            List<string> failed = StartupSteps.Run(new[]
            {
                new StartupStep("hooks: TreeHitPatches", "lumberjack swings", () => ran.Add("a")),
                new StartupStep("hooks: PlacementPatches", "the placement outline", () => throw new MissingMethodException("Player.UpdatePlacementGhost")),
                new StartupStep("command: vc_report", "a test tool", () => ran.Add("c")),
            });
            Assert.Equal(new[] { "a", "c" }, ran);
            Assert.Single(failed);
            Assert.Contains("hooks: PlacementPatches", failed[0]);
            Assert.Contains("the placement outline", failed[0]);
            Assert.Contains("Player.UpdatePlacementGhost", failed[0]);
        }

        [Fact]
        public void All_steps_well_no_failures()
        {
            Assert.Empty(StartupSteps.Run(new[] { new StartupStep("a", "b", () => { }) }));
            Assert.Empty(StartupSteps.Run(null));
        }
    }

    /// <summary>0.25: the farmer's plots looked up in a grid, with the same answers as before (review of 0.20).</summary>
    public class PlotIndexTests
    {
        private const float Tolerance = FieldPlots.OnPlotTolerance;

        [Fact]
        public void Same_answers_as_the_old_rule()
        {
            var random = new Random(25);
            for (int field = 0; field < 20; field++)
            {
                // A field of plots about a metre apart, turned and placed anywhere, with a few odd plots of other fields.
                var plots = new List<Vec3>();
                float x0 = random.Next(-2000, 2000), z0 = random.Next(-2000, 2000), turn = (float)(random.NextDouble() * Math.PI);
                int size = random.Next(1, 12);
                for (int i = 0; i < size; i++)
                    for (int j = 0; j < size; j++)
                    {
                        float dx = i * 1.1f, dz = j * 1.1f;
                        plots.Add(new Vec3(x0 + dx * (float)Math.Cos(turn) - dz * (float)Math.Sin(turn), 30f, z0 + dx * (float)Math.Sin(turn) + dz * (float)Math.Cos(turn)));
                    }
                for (int k = 0; k < 5; k++) plots.Add(new Vec3(x0 + random.Next(-15, 15) + (float)random.NextDouble(), 30f, z0 + random.Next(-15, 15)));
                var index = new PlotIndex(plots, Tolerance);
                var standing = new List<Vec3>();
                for (int n = 0; n < 400; n++)
                {
                    var at = new Vec3(x0 + (float)(random.NextDouble() * 30 - 8), 31f, z0 + (float)(random.NextDouble() * 30 - 8));
                    Assert.Equal(FieldPlots.OnPlot(at, plots), index.IndexOf(at) >= 0);
                    if (n % 3 == 0)
                    {
                        standing.Add(at);
                        index.MarkStanding(at);
                    }
                }
                for (int p = 0; p < plots.Count; p++)
                {
                    bool taken = standing.Any(s => Math.Sqrt((s.X - plots[p].X) * (s.X - plots[p].X) + (s.Z - plots[p].Z) * (s.Z - plots[p].Z)) <= Tolerance * 2f);
                    Assert.Equal(taken, index.IsTaken(p));
                }
            }
        }

        [Fact]
        public void The_plot_found_is_one_within_reach()
        {
            var plots = new List<Vec3> { new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(2, 0, 0) };
            var index = new PlotIndex(plots, Tolerance);
            Assert.Equal(1, index.IndexOf(new Vec3(1.1f, 5f, 0.1f)));
            Assert.Equal(-1, index.IndexOf(new Vec3(0.5f, 0f, 0f)));
            index.MarkStanding(new Vec3(2.7f, 0f, 0f));
            Assert.True(index.IsTaken(2));
            Assert.False(index.IsTaken(1));
            index.ClearStanding();
            Assert.False(index.IsTaken(2));
        }

        [Fact]
        public void Empty_field()
        {
            var index = new PlotIndex(new List<Vec3>(), Tolerance);
            Assert.Equal(-1, index.IndexOf(new Vec3(0, 0, 0)));
            index.MarkStanding(new Vec3(0, 0, 0));
            Assert.Equal(0, index.Count);
        }
    }
}
