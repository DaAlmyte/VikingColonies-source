using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class SeedPlanTests
    {
        private static readonly string[] Spots = { "BeechSeeds", "BeechSeeds", "Acorn" };

        private static CarryLoad Carry()
        {
            var carry = new CarryLoad();
            carry.Add("BeechSeeds", 1);
            carry.Add("Acorn", 3);
            carry.Add("Wood", 20);
            return carry;
        }

        [Fact]
        public void Reserved_is_capped_by_what_is_carried()
        {
            var reserved = SeedPlan.Reserved(Carry(), Spots);
            Assert.Equal(1, reserved["BeechSeeds"]);
            Assert.Equal(1, reserved["Acorn"]);
            Assert.False(reserved.ContainsKey("Wood"));
            Assert.Equal(2, SeedPlan.Total(reserved));
        }

        // Review M9: every pending spot gets its seed, not just one per trip.
        [Fact]
        public void Missing_counts_seeds_still_to_fetch_for_every_spot()
        {
            var missing = SeedPlan.Missing(Carry(), Spots);
            Assert.Equal(1, missing["BeechSeeds"]);
            Assert.False(missing.ContainsKey("Acorn"));
        }

        [Fact]
        public void No_spots_reserves_and_misses_nothing()
        {
            Assert.Empty(SeedPlan.Reserved(Carry(), new string[0]));
            Assert.Empty(SeedPlan.Missing(Carry(), new string[0]));
        }
    }
}
