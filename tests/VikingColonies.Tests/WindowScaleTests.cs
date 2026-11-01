using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Windows.Scale (1.5.1, review 8: Hearthfolk's UI scale): every VikingColonies window at the player's size.</summary>
    public class WindowScaleTests
    {
        [Fact]
        public void The_setting_is_kept_between_three_quarters_and_one_and_a_half()
        {
            Assert.Equal(0.75f, WindowScale.Clamp(0.2f));
            Assert.Equal(1.5f, WindowScale.Clamp(3f));
            Assert.Equal(1.2f, WindowScale.Clamp(1.2f), 3);
            Assert.Equal(1f, WindowScale.Clamp(float.NaN));
        }

        [Fact]
        public void A_window_too_tall_for_the_screen_is_made_to_fit()
        {
            Assert.Equal(1.5f, WindowScale.Fit(1.5f, 600f, 1440f), 3);
            Assert.Equal(1080f * 0.95f / 720f, WindowScale.Fit(1.5f, 720f, 1080f), 3);   // 1.425: 720 x 1.5 is taller than a 1080p screen
            Assert.Equal(1f, WindowScale.Fit(1f, 720f, 1080f), 3);
            Assert.Equal(1f, WindowScale.Fit(1f, 720f, 0f), 3);   // no screen size known: as set
        }

        [Fact]
        public void Its_names_are_settings_names()
        {
            Assert.Contains(ConfigNames.WindowsSection, ConfigNames.All);
            Assert.Contains(ConfigNames.WindowScale, ConfigNames.All);
        }
    }
}
