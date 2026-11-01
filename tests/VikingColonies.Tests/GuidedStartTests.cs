using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Hugin's first steps (1.5.1, review 4): seven steps read from the colony.</summary>
    public class GuidedStartTests
    {
        private static StartFacts Fresh() => new StartFacts { HasCamp = true, FoodEach = 5, CampLevel = 1 };

        [Fact]
        public void A_fresh_colony_builds_a_builders_hut_next()
        {
            StartFacts f = Fresh();
            Assert.True(GuidedStart.Done(StartStep.Camp, f));
            Assert.Equal(StartStep.BuilderHut, GuidedStart.Next(f));
            Assert.Equal(StartStep.Camp, GuidedStart.LastDone(f));
        }

        [Fact]
        public void Without_a_camp_the_camp_comes_first()
        {
            var f = new StartFacts();
            Assert.Equal(StartStep.Camp, GuidedStart.Next(f));
            Assert.Equal(StartStep.None, GuidedStart.LastDone(f));
        }

        [Fact]
        public void Each_fact_makes_its_step_done()
        {
            StartFacts f = Fresh();
            f.BuiltBuilderHut = true;
            Assert.Equal(StartStep.Worker, GuidedStart.Next(f));
            f.HasWorker = true;
            Assert.Equal(StartStep.Longhouse, GuidedStart.Next(f));
            f.BuiltLonghouse = true;
            Assert.Equal(StartStep.Food, GuidedStart.Next(f));
            f.CampFood = 4;
            Assert.Equal(StartStep.Food, GuidedStart.Next(f));
            f.CampFood = 5;
            Assert.Equal(StartStep.Watchtower, GuidedStart.Next(f));
            f.BuiltWatchtower = true;
            Assert.Equal(StartStep.CampLevel2, GuidedStart.Next(f));
            f.CampLevel = 2;
            Assert.Equal(StartStep.None, GuidedStart.Next(f));
            Assert.Equal(StartStep.CampLevel2, GuidedStart.LastDone(f));
        }

        [Fact]
        public void A_later_step_done_early_is_ticked_and_the_first_undone_comes_next()
        {
            StartFacts f = Fresh();
            f.BuiltBuilderHut = true;
            f.HasWorker = true;
            f.BuiltWatchtower = true;
            Assert.True(GuidedStart.Done(StartStep.Watchtower, f));
            Assert.Equal(StartStep.Longhouse, GuidedStart.Next(f));
            Assert.Equal(StartStep.Worker, GuidedStart.LastDone(f));
        }

        [Fact]
        public void Food_needs_at_least_one()
        {
            var f = new StartFacts { HasCamp = true, FoodEach = 0, CampFood = 0 };
            Assert.False(GuidedStart.Done(StartStep.Food, f));
            f.CampFood = 1;
            Assert.True(GuidedStart.Done(StartStep.Food, f));
        }

        [Fact]
        public void The_builder_alone_is_not_a_settler_with_a_job()
        {
            // The Builder's Hut hires its builder before it stands (review of 1.5.1): only another trade ticks step 3.
            Assert.False(GuidedStart.CountsAsWorker(SaveKeys.BuilderJob));
            Assert.False(GuidedStart.CountsAsWorker(""));
            Assert.False(GuidedStart.CountsAsWorker(null));
            Assert.True(GuidedStart.CountsAsWorker("lumberjack"));
        }

        [Fact]
        public void With_the_camps_levels_off_the_last_step_is_done()
        {
            StartFacts f = Fresh();
            f.BuiltBuilderHut = f.HasWorker = f.BuiltLonghouse = f.BuiltWatchtower = true;
            f.CampFood = 5;
            Assert.Equal(StartStep.CampLevel2, GuidedStart.Next(f));
            f.LevelsOn = false;
            Assert.True(GuidedStart.Done(StartStep.CampLevel2, f));
            Assert.Equal(StartStep.None, GuidedStart.Next(f));
        }

        [Fact]
        public void A_done_steps_line_is_stale_and_the_next_steps_is_not()
        {
            StartFacts f = Fresh();
            f.BuiltBuilderHut = true;
            var stale = GuidedStart.StaleLines(f).ToList();
            Assert.Contains("$vc_step_builder_hut_line", stale);
            Assert.DoesNotContain("$vc_step_worker_line", stale);
            Assert.DoesNotContain("$vc_step_done_line", stale);
            Assert.DoesNotContain("", stale);
        }

        [Fact]
        public void The_builders_hut_line_is_not_said_after_the_colony_tip()
        {
            // The colony tip (0.11) already says to build a Builder's Hut with its tools (review of 1.5.1).
            Assert.Equal("", GuidedStart.LineToken(StartStep.BuilderHut, colonyTipSaid: true));
            Assert.Equal("$vc_step_builder_hut_line", GuidedStart.LineToken(StartStep.BuilderHut, colonyTipSaid: false));
            Assert.Equal("$vc_step_worker_line", GuidedStart.LineToken(StartStep.Worker, colonyTipSaid: true));
        }

        [Fact]
        public void The_words()
        {
            Assert.Equal("$vc_step_camp_level_2", GuidedStart.Token(StartStep.CampLevel2));
            Assert.Equal("$vc_step_builder_hut", GuidedStart.Token(StartStep.BuilderHut));
            Assert.Equal("$vc_step_builder_hut_line", GuidedStart.LineToken(StartStep.BuilderHut));
            Assert.Equal("", GuidedStart.LineToken(StartStep.Camp));
            Assert.Equal("$vc_step_done_line", GuidedStart.LineToken(StartStep.None));
        }
    }
}
