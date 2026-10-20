using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The real builders' run (2026-10-18): two builders both fetched the Bronze for the Builder's Hut, and the spare stayed
    /// in a pocket while he went on to other sites; the Farm then waited 40 game minutes for "1 Bronze" that had been
    /// supplied. A builder taking a new site keeps what it still needs and puts the rest back in a chest first.
    /// </summary>
    public class LeftoverTests
    {
        private static CarryLoad Load(params (string, int)[] items)
        {
            var load = new CarryLoad();
            foreach ((string item, int n) in items) load.Add(item, n);
            return load;
        }

        [Fact]
        public void What_the_next_site_still_needs_is_kept_the_rest_goes_back()
        {
            CarryLoad carrying = Load(("Bronze", 3), ("Wood", 20), ("Flint", 4));
            var needs = new Dictionary<string, int> { { "Wood", 12 }, { "Stone", 30 } };
            Dictionary<string, int> keep = Leftovers.KeepFor(carrying, needs);
            Assert.Equal(12, keep["Wood"]);
            Assert.False(keep.ContainsKey("Bronze"));
            Assert.False(keep.ContainsKey("Stone"));             // he doesn't carry any: nothing to keep
            Assert.True(Leftovers.AnyToPutBack(carrying, needs));
            Assert.Equal(3 + 8 + 4, carrying.Beyond(keep).Total);   // the Bronze, 8 Wood and the Flint go back
        }

        [Fact]
        public void A_load_the_next_site_uses_up_stays_with_him()
        {
            CarryLoad carrying = Load(("Bronze", 3));
            Assert.False(Leftovers.AnyToPutBack(carrying, new Dictionary<string, int> { { "Bronze", 5 } }));
            Assert.False(Leftovers.AnyToPutBack(new CarryLoad(), new Dictionary<string, int>()));
        }

        [Fact]
        public void With_no_next_site_everything_goes_back()
        {
            CarryLoad carrying = Load(("Bronze", 3), ("Wood", 2));
            Assert.True(Leftovers.AnyToPutBack(carrying, null));
            Assert.Empty(Leftovers.KeepFor(carrying, null));
        }
    }
}
