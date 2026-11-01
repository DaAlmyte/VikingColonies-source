using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// A player's report (1.4.3, "NPC's"): StarLevelSystem made starred settlers bigger, too big for a door. A settler keeps
    /// their own size whatever another mod sets.
    /// </summary>
    public class SettlerSizeTests
    {
        [Fact]
        public void A_settler_made_bigger_or_smaller_by_another_mod_is_changed()
        {
            Assert.True(SettlerSize.Changed(1.2f, 1.2f, 1.2f, 1f, 1f, 1f));   // two stars of 10%
            Assert.True(SettlerSize.Changed(0.8f, 0.8f, 0.8f, 1f, 1f, 1f));
            Assert.True(SettlerSize.Changed(1f, 1.3f, 1f, 1f, 1f, 1f));
        }

        [Fact]
        public void Their_own_size_give_or_take_a_hair_is_left_alone()
        {
            Assert.False(SettlerSize.Changed(1f, 1f, 1f, 1f, 1f, 1f));
            Assert.False(SettlerSize.Changed(1.005f, 0.996f, 1f, 1f, 1f, 1f));
        }
    }
}
