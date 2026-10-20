using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The real builders' run (2026-10-18): a builder stuck on a roof went through the door below 92 times on his way to
    /// another hut without getting any closer; each pass reset his job's stuck clock, so it never gave up.
    /// </summary>
    public class DoorLoopTests
    {
        [Fact]
        public void Going_through_a_door_that_brings_him_no_closer_gives_it_up_for_that_goal()
        {
            var loop = new DoorLoop();
            Assert.False(loop.Passed("door@goal", 20f, 0));
            Assert.False(loop.Passed("door@goal", 21f, 4));
            Assert.True(loop.Passed("door@goal", 20.5f, 8));     // the third pass in a row, no nearer than the first
        }

        [Fact]
        public void A_door_that_gets_him_nearer_each_time_is_no_loop()
        {
            var loop = new DoorLoop();
            Assert.False(loop.Passed("door@goal", 20f, 0));
            Assert.False(loop.Passed("door@goal", 15f, 4));
            Assert.False(loop.Passed("door@goal", 9f, 8));
            Assert.False(loop.Passed("door@goal", 3f, 12));
        }

        // The review of 2026-10-19: three ordinary trips to the same chest within 5 minutes counted as a loop, so a worker
        // whose chest stands 8 m inside couldn't deliver, and sheltering could fail in a raid.
        [Fact]
        public void Three_separate_trips_to_the_same_chest_are_no_loop()
        {
            var loop = new DoorLoop();
            for (int trip = 0; trip < 3; trip++)
            {
                double t = trip * 60.0;
                Assert.False(loop.Passed("door@chest", 6f, t));        // in to the chest
                Assert.False(loop.Passed("door@site", 12f, t + 10));   // out again, to the building site
            }
        }

        [Fact]
        public void Trips_through_the_same_door_well_apart_are_no_loop()
        {
            var loop = new DoorLoop();
            Assert.False(loop.Passed("door@chest", 6f, 0));
            Assert.False(loop.Passed("door@chest", 6f, DoorLoop.LoopGap + 1));
            Assert.False(loop.Passed("door@chest", 6f, 2 * DoorLoop.LoopGap + 2));
        }

        [Fact]
        public void Arriving_anywhere_ends_a_run_of_passes()
        {
            var loop = new DoorLoop();
            Assert.False(loop.Passed("door@chest", 6f, 0));
            Assert.False(loop.Passed("door@chest", 6f, 4));
            loop.Arrived();
            Assert.False(loop.Passed("door@chest", 6f, 8));
            Assert.False(loop.Passed("door@chest", 6f, 12));
            Assert.True(loop.Passed("door@chest", 6f, 16));
        }
    }
}
