using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The security sweep of 27 Oct 2026: a place another computer sends is a real one, or it's refused.</summary>
    public class FinitePointTests
    {
        [Theory]
        [InlineData(0f, 0f, 0f)]
        [InlineData(-10500f, 3000f, 10500f)]
        public void A_real_place_is_finite(float x, float y, float z)
        {
            Assert.True(RecordCodec.IsFinite(x, y, z));
        }

        [Theory]
        [InlineData(float.NaN, 0f, 0f)]
        [InlineData(0f, float.NaN, 0f)]
        [InlineData(0f, 0f, float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity, 0f, 0f)]
        public void NaN_or_infinity_anywhere_is_not(float x, float y, float z)
        {
            Assert.False(RecordCodec.IsFinite(x, y, z));
        }
    }
}
