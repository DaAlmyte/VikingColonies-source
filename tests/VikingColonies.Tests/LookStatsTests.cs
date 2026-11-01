using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // The Valheim look pass (27 Oct 2026): vc_look measures the game's pieces with LookStats and tools/assets/look.py
    // measures our own models; both must give the same numbers. The fixture and its numbers are the same in
    // tools/assets/tests/test_look.py.
    public class LookStatsTests
    {
        // 4 x 2, row 0 at the bottom; the third texel of the top row is see-through (alpha 0) and doesn't count.
        private static readonly byte[] Fixture =
        {
            255, 0, 0, 255,   0, 255, 0, 255,   0, 0, 255, 255,   128, 128, 128, 255,
            20, 20, 20, 255,  200, 180, 160, 255,  90, 60, 30, 0,  100, 80, 60, 255,
        };

        [Fact]
        public void The_fixture_measures_as_in_the_python_tools()
        {
            LookStats s = LookStats.Measure(Fixture, 4, 2);
            Assert.Equal(7, s.Texels);
            Assert.Equal(0.374575, s.Luma, 4);
            Assert.Equal(0.255591, s.LumaSpread, 4);
            Assert.Equal(0.514286, s.Saturation, 4);
            Assert.Equal(0.285714, s.Dark, 4);
            Assert.Equal(0.285714, s.Light, 4);
            Assert.Equal(30.0, s.HueMean, 3);
            Assert.Equal(77.562943, s.HueSpread, 3);
            Assert.Equal(0.360987, s.Grain, 4);
        }

        [Fact]
        public void Lopsided_as_in_the_python_tools()
        {
            // the same fixture and number as tools/assets/tests/test_look.py's LopsidedTests
            var points = new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0.2f, 0.5f, 0.3f, 0.9f, 0.2f, 0.6f };
            Assert.Equal(0.214449, LookStats.Lopsided(points), 5);
            var box = new float[] { 0, 0, 0, 1, 0, 0, 0, 2, 0, 1, 2, 0, 0, 0, 0.5f, 1, 0, 0.5f, 0, 2, 0.5f, 1, 2, 0.5f };
            Assert.Equal(0.0, LookStats.Lopsided(box), 6);
        }

        [Fact]
        public void A_mask_leaves_texels_out()
        {
            var mask = new[] { true, true, false, true, true, true, true, true };
            LookStats s = LookStats.Measure(Fixture, 4, 2, mask);
            Assert.Equal(6, s.Texels);
            Assert.Equal(0.424971, s.Luma, 4);
            Assert.Equal(0.29083, s.Grain, 4);
            Assert.Equal(45.720267, s.HueSpread, 3);
        }

        [Fact]
        public void Nothing_to_measure_is_null()
        {
            Assert.Null(LookStats.Measure(Fixture, 4, 2, new bool[8]));
        }

        [Fact]
        public void A_uv_triangle_covers_its_lower_left_half()
        {
            bool[] mask = LookStats.UvMask(new[] { 0f, 0f, 1f, 0f, 0f, 1f }, new[] { 0, 1, 2 }, 4, 4);
            // row 0 (bottom) all four, row 3 (top) only the first
            Assert.True(mask[0] && mask[1] && mask[2] && mask[3]);
            Assert.True(mask[12] && !mask[13] && !mask[14] && !mask[15]);
            Assert.True(mask[8] && mask[9] && !mask[10]);
        }

        [Fact]
        public void Texels_per_metre_is_the_root_of_the_areas()
        {
            // a 2 m right triangle on half of a 64 px texture: 2048 px over 2 m²
            double d = LookStats.TexelsPerMetre(new[] { 0f, 0f, 0f, 2f, 0f, 0f, 0f, 2f, 0f }, new[] { 0f, 0f, 1f, 0f, 0f, 1f },
                new[] { 0, 1, 2 }, 64, 64);
            Assert.Equal(32.0, d, 6);
            Assert.Equal(64.0, LookStats.TexelsPerMetre(new[] { 0f, 0f, 0f, 2f, 0f, 0f, 0f, 2f, 0f }, new[] { 0f, 0f, 1f, 0f, 0f, 1f },
                new[] { 0, 1, 2 }, 64, 64, 2, 2), 6);
        }

        [Fact]
        public void A_flat_normal_map_has_no_strength_packed_or_not()
        {
            Assert.Equal(0.0, LookStats.NormalStrength(new byte[] { 128, 128, 255, 255 }, 1, 1), 1);
            Assert.Equal(0.0, LookStats.NormalStrength(new byte[] { 255, 128, 128, 128 }, 1, 1), 1);
            Assert.True(LookStats.NormalStrength(new byte[] { 230, 128, 200, 255 }, 1, 1) > 0.7);
        }
    }
}
