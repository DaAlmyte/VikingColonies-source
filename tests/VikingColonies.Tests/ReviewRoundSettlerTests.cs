using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The review round of 23 October 2026, the settlers' review: an evening that never ended, a status saved every tick.</summary>
    public class ReviewRoundSettlerTests
    {
        [Fact]   // Important: a lumberjack who couldn't reach the hut with a load never went in; the night waited on them for good
        public void The_last_errand_before_night_is_waited_for_a_minute_at_most()
        {
            var clock = new FinishingClock();
            Assert.True(clock.Asks);
            Assert.True(clock.Wait(true, 100f));
            Assert.True(clock.Wait(true, 100f + FinishingClock.Limit - 1f));
            Assert.False(clock.Wait(true, 100f + FinishingClock.Limit));   // given up: in they go, the load still in hand
            Assert.False(clock.Asks);
            Assert.False(clock.Wait(true, 200f));   // and it stays given up for the night
            clock.Reset();   // the morning
            Assert.True(clock.Asks);
            Assert.True(clock.Wait(true, 300f));
        }

        [Fact]
        public void An_errand_done_starts_the_minute_afresh()
        {
            var clock = new FinishingClock();
            Assert.True(clock.Wait(true, 0f));
            Assert.False(clock.Wait(false, 30f));
            Assert.True(clock.Wait(true, 50f));
            Assert.True(clock.Wait(true, 50f + FinishingClock.Limit - 1f));
        }

        [Fact]   // Important: "Delivering" and "Can't reach hut" took turns every tick, each saving the whole record (~100 a second)
        public void A_flipping_status_is_saved_a_few_times_a_second_at_most()
        {
            var throttle = new SaveThrottle();
            int saves = 0;
            for (int tick = 0; tick < 100; tick++)   // 2 s at 50 ticks a second
            {
                float now = tick * 0.02f;
                if (throttle.SaveNow(now)) saves++;   // a change
                if (throttle.SaveNow(now)) saves++;   // and back
                if (throttle.Due(now))
                {
                    saves++;
                    throttle.Saved(now);
                }
            }
            Assert.InRange(saves, 1, 2 + (int)(2f / SaveThrottle.Gap));
        }

        [Fact]
        public void The_last_change_is_saved_once_the_gap_is_over()
        {
            var throttle = new SaveThrottle();
            Assert.True(throttle.SaveNow(0f));      // the first at once
            Assert.False(throttle.SaveNow(0.1f));   // too soon: kept for later
            Assert.False(throttle.Due(0.2f));
            Assert.True(throttle.Due(SaveThrottle.Gap));
            throttle.Saved(SaveThrottle.Gap);
            Assert.False(throttle.Due(5f));         // nothing waiting
            Assert.True(throttle.SaveNow(5f));      // a lone change much later: at once
        }

        [Fact]   // its review (Critical): a change kept back, saved after the settler came back from another computer, wrote back
                 // this computer's old copy of the record (what they carry duplicated or lost)
        public void A_change_kept_back_is_dropped_when_the_settler_changes_computers()
        {
            var throttle = new SaveThrottle();
            Assert.True(throttle.SaveNow(0f));
            Assert.False(throttle.SaveNow(0.1f));   // kept back
            throttle.Forget();                      // another computer runs the settler now
            Assert.False(throttle.Due(10f));        // never saved from the old copy
        }
    }
}
