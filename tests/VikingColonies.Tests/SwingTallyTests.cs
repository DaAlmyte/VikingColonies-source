using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class SwingTallyTests
    {
        [Fact]
        public void The_first_ten_swings_are_logged_in_detail()
        {
            var tally = new SwingTally();
            for (int i = 1; i <= 10; i++) Assert.Equal(SwingLogKind.Detail, tally.Record(SwingResult.HitTarget));
            Assert.Equal(SwingLogKind.None, tally.Record(SwingResult.HitTarget));
        }

        [Fact]
        public void After_that_a_summary_comes_every_fifty_swings()
        {
            var tally = new SwingTally();
            for (int i = 1; i <= 49; i++) tally.Record(SwingResult.HitNothing);
            Assert.Equal(SwingLogKind.Summary, tally.Record(SwingResult.HitNothing));
            for (int i = 51; i <= 99; i++) Assert.Equal(SwingLogKind.None, tally.Record(SwingResult.HitNothing));
            Assert.Equal(SwingLogKind.Summary, tally.Record(SwingResult.HitNothing));
        }

        [Fact]
        public void Each_result_is_counted()
        {
            var tally = new SwingTally();
            tally.Record(SwingResult.HitTarget);
            tally.Record(SwingResult.HitTarget);
            tally.Record(SwingResult.HitOther);
            tally.Record(SwingResult.HitNothing);
            Assert.Equal(4, tally.Swings);
            Assert.Equal(2, tally.HitTarget);
            Assert.Equal(1, tally.HitOther);
            Assert.Equal(1, tally.HitNothing);
        }
    }
}
