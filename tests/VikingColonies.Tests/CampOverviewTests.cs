using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The Chieftain's Camp's Overview and a settler's page (1.5.1, review 8).</summary>
    public class CampOverviewTests
    {
        [Fact]
        public void Three_names_and_how_many_more()
        {
            int more;
            var shown = CampOverview.FirstNames(new[] { "Thora", "Leif", "Arne", "Ulf", "Sigrid" }, 3, out more);
            Assert.Equal(new[] { "Thora", "Leif", "Arne" }, shown);
            Assert.Equal(2, more);
            Assert.Equal(new[] { "Thora" }, CampOverview.FirstNames(new[] { "Thora" }, 3, out more));
            Assert.Equal(0, more);
        }

        [Fact]
        public void The_weightiest_reasons_summed_over_the_colony()
        {
            var a = new[] { new MoodReason("$vc_mood_home", 5), new MoodReason("$vc_mood_died", -10, "Ulf") };
            var b = new[] { new MoodReason("$vc_mood_home", 5), new MoodReason("$vc_mood_died", -10, "Ulf"), new MoodReason("$vc_mood_fire", 5) };
            var top = CampOverview.WeightiestReasons(new[] { a, b }, 2);
            Assert.Equal(2, top.Count);
            Assert.Equal("$vc_mood_died", top[0].Token);
            Assert.Equal(-20, top[0].Change);
            Assert.Equal("Ulf", top[0].Name);
            Assert.Equal(10, top[1].Change);
        }

        [Fact]
        public void Two_deaths_mourned_are_two_reasons()
        {
            var a = new[] { new MoodReason("$vc_mood_died", -10, "Ulf") };
            var b = new[] { new MoodReason("$vc_mood_died", -10, "Gudrun") };
            Assert.Equal(2, CampOverview.WeightiestReasons(new[] { a, b }, 3).Count);
        }

        [Fact]
        public void A_settlers_saga_lines_are_their_own_newest_first()
        {
            var saga = new SagaLog();
            saga.Add(new SagaEntry(1, "arrived", "Ulf"));
            saga.Add(new SagaEntry(2, "arrived", "Ulfhild"));
            saga.Add(new SagaEntry(3, "hired", "Ulf", "$vc_hut_lumberjack"));
            saga.Add(new SagaEntry(4, "recalled", "Ulf"));
            saga.Add(new SagaEntry(5, "recalled", "Ulf"));
            var lines = CampOverview.LinesNaming(saga, "Ulf", 3);
            Assert.Equal(new[] { 5, 4, 3 }, lines.Select(l => l.Day));
            Assert.DoesNotContain(CampOverview.LinesNaming(saga, "Ulf", 10), l => l.Args.Contains("Ulfhild"));
            Assert.Empty(CampOverview.LinesNaming(saga, "", 3));
        }
    }
}
