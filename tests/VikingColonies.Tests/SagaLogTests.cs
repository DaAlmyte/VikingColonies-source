using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // The colony's saga: what happened while you were away (arrivals, deaths, buildings), newest first.
    public class SagaLogTests
    {
        private static SagaLog LogOf(int count)
        {
            var log = new SagaLog();
            for (int i = 1; i <= count; i++) log.Add(new SagaEntry(i, "arrived", "Settler" + i));
            return log;
        }

        [Fact]
        public void Newest_comes_first()
        {
            SagaLog log = LogOf(3);
            Assert.Equal(new[] { 3, 2, 1 }, log.Page(0, 12).Select(e => e.Day).ToArray());
        }

        [Fact]
        public void Only_the_newest_sixty_are_kept()
        {
            SagaLog log = LogOf(75);
            Assert.Equal(SagaLog.MaxEntries, log.Count);
            Assert.Equal(75, log.Page(0, 12).First().Day);
            Assert.Equal(16, log.Page(log.PageCount(12) - 1, 12).Last().Day);
        }

        [Fact]
        public void Pages_split_the_saga_and_beyond_the_end_is_empty()
        {
            SagaLog log = LogOf(30);
            Assert.Equal(3, log.PageCount(12));
            Assert.Equal(12, log.Page(0, 12).Count);
            Assert.Equal(6, log.Page(2, 12).Count);
            Assert.Empty(log.Page(3, 12));
            Assert.Equal(1, new SagaLog().PageCount(12));
            Assert.Empty(new SagaLog().Page(0, 12));
        }

        [Fact]
        public void A_saved_saga_reads_back_the_same_even_with_separators_in_names()
        {
            var log = new SagaLog();
            log.Add(new SagaEntry(12, "arrived", "Astrid|the;Bold\\"));
            log.Add(new SagaEntry(14, "died", "Ragnar", "$vc_death_killed_by", "Greydwarf"));
            log.Add(new SagaEntry(15, "level", "$vc_lumberjack_hut", "2"));
            SagaLog back = SagaLog.Decode(log.Encode());
            Assert.Equal(3, back.Count);
            SagaEntry first = back.Page(0, 12).Last();
            Assert.Equal(12, first.Day);
            Assert.Equal("arrived", first.Kind);
            Assert.Equal(new[] { "Astrid|the;Bold\\" }, first.Args.ToArray());
            Assert.Equal(new[] { "Ragnar", "$vc_death_killed_by", "Greydwarf" }, back.Page(0, 12)[1].Args.ToArray());
        }

        [Fact]
        public void The_same_line_twice_in_a_row_is_kept_once()
        {
            // Seen in review: a settler stuck again and again (or Recall clicked again and again) would push arrivals
            // and deaths out of the saga.
            var log = new SagaLog();
            log.Add(new SagaEntry(7, "recalled", "Ivar"));
            log.Add(new SagaEntry(7, "recalled", "Ivar"));
            Assert.Equal(1, log.Count);
            log.Add(new SagaEntry(8, "recalled", "Ivar"));   // another day: a new line
            log.Add(new SagaEntry(8, "recalled", "Astrid"));
            Assert.Equal(3, log.Count);
        }

        [Fact]
        public void Only_known_kinds_of_line_are_taken()
        {
            Assert.True(SagaLog.IsKnownKind("died"));
            Assert.False(SagaLog.IsKnownKind("anything a modified game sends"));
            Assert.False(SagaLog.IsKnownKind(null));
        }

        [Fact]
        public void Damaged_text_keeps_the_good_entries_and_never_throws()
        {
            string good = new SagaLog().Tap(l => l.Add(new SagaEntry(5, "built", "$vc_forager_hut"))).Encode();
            SagaLog back = SagaLog.Decode("x|y|z\n12\n" + good + "\n7|arrived|cut short\\");
            Assert.Equal(2, back.Count);
            Assert.Contains(back.Page(0, 12), e => e.Day == 5 && e.Kind == "built");
            Assert.Equal(0, SagaLog.Decode(null).Count);
            Assert.Equal(0, SagaLog.Decode("").Count);
        }
    }

    internal static class SagaLogTestExtensions
    {
        public static SagaLog Tap(this SagaLog log, System.Action<SagaLog> act)
        {
            act(log);
            return log;
        }
    }
}
