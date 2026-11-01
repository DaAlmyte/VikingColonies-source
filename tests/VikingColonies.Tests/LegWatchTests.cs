using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// A straight leg to a hut's door or round its corner (1.5.1): walked straight while it takes him on, given up for the
    /// game's own way once he stands against something the look along it missed.
    /// </summary>
    public class LegWatchTests
    {
        [Fact]
        public void Walking_on_he_keeps_to_the_straight_leg()
        {
            var watch = new LegWatch();
            for (int i = 0; i <= 10; i++) Assert.False(watch.Stuck(i * 0.35f, 0f, i * 0.1));   // 3.5 m/s
        }

        [Fact]
        public void Standing_against_something_a_second_he_gives_it_up()
        {
            var watch = new LegWatch();
            Assert.False(watch.Stuck(0f, 0f, 0.0));
            Assert.False(watch.Stuck(0.1f, 0f, 0.5));
            Assert.True(watch.Stuck(0.15f, 0f, LegWatch.StuckSeconds + 0.01));
        }

        [Fact]
        public void A_new_walk_starts_its_clock_again()
        {
            var watch = new LegWatch();
            Assert.False(watch.Stuck(0f, 0f, 0.0));
            Assert.False(watch.Stuck(0f, 0f, 0.5));
            watch.Start();
            Assert.False(watch.Stuck(0f, 0f, 1.2));   // without the new start: 1.2 s standing, stuck
            Assert.True(watch.Stuck(0f, 0f, 1.2 + LegWatch.StuckSeconds + 0.01));
        }

        [Fact]
        public void A_long_gap_between_looks_is_a_pause_not_a_stand()
        {
            var watch = new LegWatch();
            watch.Stuck(0f, 0f, 0.0);
            Assert.False(watch.Stuck(0f, 0f, 30.0));   // the walk stopped meanwhile (waiting his turn at the door)
            Assert.False(watch.Stuck(0.1f, 0f, 30.5));   // the clock started again at 30 s
            Assert.True(watch.Stuck(0.1f, 0f, 31.1));
        }
    }
}
