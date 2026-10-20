using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class ErrorBackoffTests
    {
        [Fact]
        public void A_first_error_is_reported_and_pauses_the_settler_five_seconds()
        {
            var errors = new ErrorBackoff();
            Assert.False(errors.Paused(100f));
            Assert.Equal(1, errors.Fail(100f));
            Assert.Equal(5f, errors.LastPause);
            Assert.True(errors.Paused(104.9f));
            Assert.False(errors.Paused(105f));
        }

        [Fact]
        public void Errors_in_a_row_double_the_pause_up_to_a_minute()
        {
            var errors = new ErrorBackoff();
            float now = 0f;
            float[] expected = { 5f, 10f, 20f, 40f, 60f, 60f };
            foreach (float pause in expected)
            {
                errors.Fail(now);
                Assert.Equal(pause, errors.LastPause);
                now += pause;   // fails again as soon as he starts over
            }
        }

        [Fact]
        public void A_calm_minute_resets_the_pause()
        {
            var errors = new ErrorBackoff();
            errors.Fail(0f);
            errors.Fail(5f);
            Assert.Equal(10f, errors.LastPause);
            errors.Fail(5f + 10f + 61f);   // more than a minute after he started over
            Assert.Equal(5f, errors.LastPause);
        }

        [Fact]
        public void Repeated_errors_are_reported_once_a_minute_with_how_many_there_were()
        {
            var errors = new ErrorBackoff();
            Assert.Equal(1, errors.Fail(0f));
            Assert.Equal(0, errors.Fail(5f));
            Assert.Equal(0, errors.Fail(15f));
            Assert.Equal(0, errors.Fail(35f));
            Assert.Equal(4, errors.Fail(75f));   // the three quiet ones and this one
            Assert.Equal(0, errors.Fail(80f));
        }
    }
}
