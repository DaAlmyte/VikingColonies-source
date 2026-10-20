using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class LogSettleTests
    {
        [Fact]
        public void Settles_after_a_second_of_stillness()
        {
            var settle = new LogSettle(0);
            Assert.Equal(SettleState.Moving, settle.Update(2f, 0.0));
            Assert.Equal(SettleState.Moving, settle.Update(0.1f, 0.25));
            Assert.Equal(SettleState.Moving, settle.Update(0.1f, 0.75));
            Assert.Equal(SettleState.Settled, settle.Update(0.1f, 1.25));
        }

        [Fact]
        public void A_log_moving_again_starts_the_second_over()
        {
            var settle = new LogSettle(0);
            settle.Update(0.1f, 0.25);
            Assert.Equal(SettleState.Moving, settle.Update(1f, 0.75));
            Assert.Equal(SettleState.Moving, settle.Update(0.1f, 1.0));
            Assert.Equal(SettleState.Moving, settle.Update(0.1f, 1.75));
            Assert.Equal(SettleState.Settled, settle.Update(0.1f, 2.0));
        }

        [Fact]
        public void Gives_up_after_15_seconds_of_movement()
        {
            var settle = new LogSettle(10);
            for (double t = 10; t < 24.9; t += 0.25) Assert.Equal(SettleState.Moving, settle.Update(5f, t));
            Assert.Equal(SettleState.GaveUp, settle.Update(5f, 25.0));
        }

        [Fact]
        public void No_logs_at_all_settles_after_a_second()
        {
            var settle = new LogSettle(0);
            Assert.Equal(SettleState.Moving, settle.Update(0f, 0.0));
            Assert.Equal(SettleState.Settled, settle.Update(0f, 1.0));
        }

        [Fact]
        public void A_floating_log_never_counts_as_moving()
        {
            Assert.False(LogSettle.IsMoving(2f, afloat: true));    // it bobs on the waves for ever
            Assert.True(LogSettle.IsMoving(2f, afloat: false));
            Assert.False(LogSettle.IsMoving(0.1f, afloat: false));
        }

        [Fact]
        public void A_log_is_afloat_only_when_its_middle_is_at_the_water_surface_or_below()
        {
            Assert.True(LogSettle.IsAfloat(centreHeight: 28.35f, liquidLevel: 30f));    // bobbing in the sea
            Assert.True(LogSettle.IsAfloat(centreHeight: 30.3f, liquidLevel: 30f));     // riding a wave
            Assert.False(LogSettle.IsAfloat(centreHeight: 31.2f, liquidLevel: 30f));    // lying on the shore, a metre up
            Assert.False(LogSettle.IsAfloat(centreHeight: 34f, liquidLevel: 30f));      // falling on the hillside
            Assert.False(LogSettle.IsAfloat(centreHeight: 50f, liquidLevel: -10000f));  // no water anywhere near
        }

        [Fact]
        public void Moving_means_faster_than_0_3_metres_a_second()
        {
            Assert.False(LogSettle.IsMoving(0.3f));
            Assert.True(LogSettle.IsMoving(0.31f));
            Assert.True(LogSettle.IsMoving(float.MaxValue));   // a log seen for the first time
        }
    }
}
