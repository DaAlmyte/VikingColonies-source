using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The real builders' run (2026-10-18): re-roofing the Builder's Hut, a builder chased a ridge cap's drop onto the roof,
    /// 4 m up, and stayed stuck there, holding another hut's order.
    /// </summary>
    public class BuilderDropTests
    {
        [Fact]
        public void A_drop_on_the_floor_or_a_bench_is_picked_up_one_on_the_roof_is_left()
        {
            const float floor = 40f;   // the site's floor: the chest's (a raised hut floor sits over the ground)
            Assert.True(DropRules.WithinReach(40.0f, floor));
            Assert.True(DropRules.WithinReach(39.2f, floor));    // the ground outside a raised floor
            Assert.True(DropRules.WithinReach(41.2f, floor));    // on a bench or a stack
            Assert.False(DropRules.WithinReach(44.1f, floor));   // the ridge, where he went after it
            Assert.False(DropRules.WithinReach(42.0f, floor));   // a roof's eave at 2 m
        }
    }
}
