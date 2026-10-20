using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class GravesTests
    {
        [Fact]
        public void A_settler_who_carried_nothing_leaves_no_cairn()
        {
            Assert.False(Graves.HasGrave(new CarryLoad()));
            Assert.False(Graves.HasGrave(null));
        }

        [Fact]
        public void A_settler_who_carried_anything_leaves_a_cairn()
        {
            var carry = new CarryLoad();
            carry.Add("Resin", 1);
            Assert.True(Graves.HasGrave(carry));
        }

        [Fact]
        public void The_map_pin_is_named_after_him()
        {
            Assert.Equal("Ylva's cairn", Graves.PinName("Ylva"));
        }

        [Fact]
        public void A_cairn_holds_more_kinds_than_a_settler_carries()
        {
            Assert.True(Graves.Columns * Graves.Rows >= 16);
        }
    }
}
