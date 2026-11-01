using System.Collections.Generic;
using TestKit;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The mod-neutral test kit (26 Oct 2026): nothing in it is VikingColonies' own.</summary>
    public class TestKitTests
    {
        [Fact]
        public void A_rule_watch_names_its_lines_holds_looks_and_throttles()
        {
            var watch = new RuleWatch("boatdog", new[] { "sinking", "drift" }, 30);
            Assert.Equal("boatdog: FAIL sinking (the longship)", watch.Line("sinking", "the longship"));
            Assert.False(watch.Held("deep", "ship1", true, looks: 3));
            Assert.False(watch.Held("deep", "ship1", true, looks: 3));
            Assert.True(watch.Held("deep", "ship1", true, looks: 3));
            Assert.False(watch.Held("deep", "ship1", false));
            var lines = new List<string>();
            watch.Emit(lines, 0, "sinking", "ship1", "a");
            watch.Emit(lines, 10, "sinking", "ship1", "b");   // within 30 s: quiet
            watch.Emit(lines, 10, "sinking", "ship2", "c");
            watch.Emit(lines, 31, "sinking", "ship1", "d");
            Assert.Equal(new[] { "boatdog: FAIL sinking (a)", "boatdog: FAIL sinking (c)", "boatdog: FAIL sinking (d)" }, lines);
            Assert.Equal(3, watch.Seen["sinking"]);
            Assert.True(watch.SetRule("drift", false));
            Assert.False(watch.SetRule("nonsense", false));
            watch.Emit(lines, 100, "drift", "ship1", "e");
            Assert.Equal(3, lines.Count);
            watch.Reset();
            Assert.True(watch.IsOn("drift"));
            Assert.Equal(0, watch.Seen["sinking"]);
        }

        [Fact]
        public void A_rule_watch_forgets_what_is_gone()
        {
            var watch = new RuleWatch("x", new[] { "r" }, 60);
            watch.Held("k", "a", true);
            watch.Forget("k", new HashSet<string>());
            Assert.False(watch.Held("k", "a", true));   // a first look again
        }

        [Fact]
        public void Seeded_streams_stand_alone()
        {
            var a = new SeededStreams();
            var b = new SeededStreams();
            a.SetSeed(42);
            b.SetSeed(42);
            Assert.Equal(a.For("p", "q").Next(), b.For("p", "q").Next());
            Assert.Equal(a.NewId(), b.NewId());
            Assert.Null(new SeededStreams().NewId());
        }
    }
}
