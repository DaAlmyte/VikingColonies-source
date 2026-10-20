using System;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class NorseNamesTests
    {
        [Fact]
        public void A_new_settler_gets_a_name_nobody_in_the_colony_has_while_there_is_one()
        {
            var taken = new System.Collections.Generic.HashSet<string>(NorseNames.Female.Take(NorseNames.Female.Count - 1));
            string last = NorseNames.Female.Last();
            for (int seed = 0; seed < 20; seed++)
                Assert.Equal(last, NorseNames.PickUnused(new Random(seed), true, taken));
            var all = new System.Collections.Generic.HashSet<string>(NorseNames.Male);
            Assert.Contains(NorseNames.PickUnused(new Random(1), false, all), NorseNames.Male);   // all taken: any will do
        }

        [Fact]
        public void Pick_is_repeatable_with_the_same_seed_and_comes_from_the_list()
        {
            string a = NorseNames.Pick(new Random(42), false);
            string b = NorseNames.Pick(new Random(42), false);
            Assert.Equal(a, b);
            Assert.Contains(a, NorseNames.All);
        }

        [Fact]
        public void All_names_are_unique_and_not_blank()
        {
            Assert.All(NorseNames.All, n => Assert.False(string.IsNullOrWhiteSpace(n)));
            Assert.Equal(NorseNames.All.Count, NorseNames.All.Distinct().Count());
        }

        [Fact]
        public void WithJob_formats_name_and_title()
        {
            Assert.Equal("Bjorn the Lumberjack", NorseNames.WithJob("Bjorn", "Lumberjack"));
        }

        [Fact]
        public void A_female_body_gets_a_female_name_and_a_male_body_a_male_name()
        {
            var rng = new Random(7);
            for (int i = 0; i < 50; i++)
            {
                Assert.Contains(NorseNames.Pick(rng, true), NorseNames.Female);
                Assert.Contains(NorseNames.Pick(rng, false), NorseNames.Male);
            }
        }

        [Theory]
        [InlineData(1, "Beard5", "BeardNone")]   // women never have a beard (old settlers could have one)
        [InlineData(1, "BeardNone", "BeardNone")]
        [InlineData(0, "Beard5", "Beard5")]
        [InlineData(0, "BeardNone", "BeardNone")]
        public void Women_never_have_a_beard(int model, string beard, string expected)
        {
            Assert.Equal(expected, NorseNames.BeardFor(model, beard));
        }

        [Theory]
        [InlineData("Sigrid", 0, 1)]   // a woman's name on a man's body: the body follows the name
        [InlineData("Ulf", 1, 0)]
        [InlineData("Astrid", 1, 1)]
        [InlineData("Zed", 1, 1)]      // an unknown name keeps its body
        public void The_body_follows_a_known_name(string name, int model, int expected)
        {
            Assert.Equal(expected, NorseNames.BodyFor(name, model));
        }
    }

    public class TargetBlacklistTests
    {
        [Fact]
        public void Unknown_target_is_not_blocked()
        {
            Assert.False(new TargetBlacklist(300).IsBlocked("tree1", 0));
        }

        [Fact]
        public void Target_is_blocked_until_the_duration_passes()
        {
            var list = new TargetBlacklist(300);
            list.Add("tree1", 100);
            Assert.True(list.IsBlocked("tree1", 399.9));
            Assert.False(list.IsBlocked("tree1", 400));
            Assert.False(list.IsBlocked("tree2", 150));
        }
    }

    public class StuckDetectorTests
    {
        [Fact]
        public void Making_progress_is_never_stuck()
        {
            var detector = new StuckDetector(0.5f, 10);
            for (int t = 0; t <= 60; t++) Assert.False(detector.Update(100f - t, t));
        }

        [Fact]
        public void No_real_progress_for_the_timeout_is_stuck()
        {
            var detector = new StuckDetector(0.5f, 10);
            Assert.False(detector.Update(20f, 0));
            Assert.False(detector.Update(19.8f, 5));   // only 0.2 m closer: not progress
            Assert.True(detector.Update(19.9f, 10));
        }

        [Fact]
        public void Reset_starts_the_clock_over()
        {
            var detector = new StuckDetector(0.5f, 10);
            detector.Update(20f, 0);
            Assert.True(detector.Update(20f, 11));
            detector.Reset();
            Assert.False(detector.Update(20f, 12));
            Assert.False(detector.Update(20f, 21));
            Assert.True(detector.Update(20f, 22));
        }
    }
}
